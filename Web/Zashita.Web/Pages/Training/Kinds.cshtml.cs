using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class KindsModel : PageModel
{
    private readonly TrainingKindStore _store;
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingConductStore _conduct;
    private readonly TrainingProgramStore _programs;

    public KindsModel(
        TrainingKindStore store,
        TrainingStandardsStore standards,
        TrainingConductStore conduct,
        TrainingProgramStore programs)
    {
        _store = store;
        _standards = standards;
        _conduct = conduct;
        _programs = programs;
    }

    [BindProperty(SupportsGet = true)]
    public string? FilterSection { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<TrainingKindNode> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<TrainingKindNode>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    /// <summary>Разделы для фильтра — только те, что есть в справочнике.</summary>
    public IReadOnlyList<string> SectionOptions { get; private set; } = [];
    /// <summary>Подсказки для колонки «Раздел» (дефолты + фактические).</summary>
    public IReadOnlyList<string> SectionSuggestions { get; private set; } = TrainingKindCatalogDefaults.SectionOptions;
    public IReadOnlyList<string> PersonnelCategoryOptions => TrainingKindCatalogDefaults.PersonnelCategoryOptions;
    public string? Message { get; private set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Message = TempData["Message"]?.ToString();
        var all = _store.Load();
        SectionOptions = TrainingKindCatalogDefaults.CollectUsedSections(all);
        SectionSuggestions = TrainingKindCatalogDefaults.MergeSectionOptions(all.Select(n => n.Section));
        Nodes = FilterNodes(all);
    }

    public IActionResult OnPostSave()
    {
        var names = Request.Form["Name"];
        var types = Request.Form["Type"];
        var periodicities = Request.Form["Periodicity"];
        var sections = Request.Form["Section"];
        var personnelCategories = Request.Form["PersonnelCategory"];
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var deletes = Request.Form["Delete"];

        var formIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var posted = new List<TrainingKindNode>();
        var existingById = _store.Load().ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var deletedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previousSections = existingById.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.Section ?? string.Empty).Trim(),
            StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < names.Count; i++)
        {
            var rowId = i < ids.Count ? ids[i].ToString()?.Trim() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(rowId))
            {
                formIds.Add(rowId);
            }

            if (IsMarkedForDelete(deletes, i))
            {
                if (!string.IsNullOrWhiteSpace(rowId)
                    && existingById.TryGetValue(rowId, out var deleted)
                    && !deleted.IsGroup
                    && !string.IsNullOrWhiteSpace(deleted.Name))
                {
                    deletedNames.Add(deleted.Name.Trim());
                }

                continue;
            }

            var name = names[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = !string.IsNullOrWhiteSpace(rowId) ? rowId : Guid.NewGuid().ToString("N");
            formIds.Add(id);
            existingById.TryGetValue(id, out var previous);

            posted.Add(new TrainingKindNode
            {
                Id = id,
                ParentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null),
                IsGroup = i < isGroups.Count && isGroups[i].ToString() == "true",
                Name = name,
                Code = previous?.Code ?? (i + 1).ToString("000000"),
                Type = i < types.Count ? types[i].ToString() ?? "Обучение" : "Обучение",
                Periodicity = i < periodicities.Count ? periodicities[i].ToString() ?? "" : "",
                Section = i < sections.Count ? sections[i].ToString() ?? "Охрана труда" : "Охрана труда",
                PersonnelCategory = i < personnelCategories.Count ? personnelCategories[i].ToString()?.Trim() ?? "" : ""
            });
        }

        var merged = existingById.Values
            .Where(node => !formIds.Contains(node.Id))
            .Concat(posted)
            .ToList();

        // Смена раздела у группы в таблице — обновляем вложенные виды (как в карточке).
        foreach (var group in posted.Where(n => n.IsGroup && !string.IsNullOrWhiteSpace(n.Section)))
        {
            previousSections.TryGetValue(group.Id, out var previousSection);
            var newSection = group.Section.Trim();
            if (!string.Equals(previousSection, newSection, StringComparison.OrdinalIgnoreCase))
            {
                TrainingKindCatalogDefaults.PropagateSectionToDescendants(
                    merged, group.Id, newSection, previousSection);
            }
        }

        _store.Save(merged);

        var activeProgramNames = merged
            .Where(node => !node.IsGroup && !node.MarkedForDeletion && !string.IsNullOrWhiteSpace(node.Name))
            .Select(node => node.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Чистим стандарты и проведение от видов, которых больше нет в справочнике
        // (в т.ч. старые «хвосты» после удаления).
        var retained = _standards.RetainOnlyProgramNames(activeProgramNames);
        var orphanConduct = 0;
        if (deletedNames.Count > 0)
        {
            orphanConduct = _conduct.DeleteByPrograms(deletedNames);
        }

        // Дополнительно: проведение по программам, которых нет в актуальном справочнике.
        var allConductPrograms = _conduct.LoadAll()
            .Select(r => (r.Program ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !activeProgramNames.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (allConductPrograms.Count > 0)
        {
            orphanConduct += _conduct.DeleteByPrograms(allConductPrograms);
        }

        var removedCourses = deletedNames.Count > 0
            ? _programs.RemoveByNames(deletedNames)
            : 0;

        TempData["Message"] = retained > 0 || orphanConduct > 0 || removedCourses > 0 || deletedNames.Count > 0
            ? $"Справочник видов обучения сохранён. Обновлено стандартов: {retained}, записей проведения: {orphanConduct}, курсов: {removedCourses}."
            : "Справочник видов обучения сохранён.";

        return RedirectToPage(new { filterSection = FilterSection, q = Q });
    }

    private static bool IsMarkedForDelete(IReadOnlyList<string> deletes, int index) =>
        index < deletes.Count &&
        string.Equals(deletes[index].ToString(), "true", StringComparison.OrdinalIgnoreCase);

    private List<TrainingKindNode> FilterNodes(IReadOnlyList<TrainingKindNode> source)
    {
        IEnumerable<TrainingKindNode> current = source;
        if (!string.IsNullOrWhiteSpace(FilterSection))
        {
            var section = FilterSection.Trim();
            var groupsById = source
                .Where(k => k.IsGroup && !k.MarkedForDeletion)
                .GroupBy(k => k.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var visibleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in source)
            {
                var resolved = TrainingNeedsCalendarBuilder.ResolveSection(node, groupsById);
                var own = (node.Section ?? string.Empty).Trim();
                var matches = resolved.Equals(section, StringComparison.OrdinalIgnoreCase)
                              || own.Equals(section, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    continue;
                }

                visibleIds.Add(node.Id);
                AddAncestors(node, source, visibleIds);
            }

            current = source.Where(node => visibleIds.Contains(node.Id));
        }

        var list = current.ToList();
        if (string.IsNullOrWhiteSpace(Q))
        {
            return list;
        }

        var query = Q.Trim();
        var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in list)
        {
            if (node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || node.Type.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (node.Section ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
                || node.Periodicity.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matchedIds.Add(node.Id);
                AddAncestors(node, list, matchedIds);
            }
        }

        return list.Where(node => matchedIds.Contains(node.Id)).ToList();
    }

    private static void AddAncestors(
        TrainingKindNode node,
        IReadOnlyList<TrainingKindNode> source,
        HashSet<string> matchedIds)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId))
        {
            return;
        }

        var parent = source.FirstOrDefault(item => item.Id == node.ParentId);
        if (parent is null || !matchedIds.Add(parent.Id))
        {
            return;
        }

        AddAncestors(parent, source, matchedIds);
    }
}

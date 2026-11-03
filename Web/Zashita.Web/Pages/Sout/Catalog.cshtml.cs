using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

[RequireLogin]
public class CatalogModel : PageModel
{
    private readonly SoutCatalogStore _store;
    private readonly OrganizationStore _orgs;

    public CatalogModel(SoutCatalogStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "equipment";

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public string Title { get; private set; } = "Оборудование";
    public string StubMessage { get; private set; } = string.Empty;
    public List<SoutCatalogRecord> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<SoutCatalogRecord>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public string? Message { get; private set; }

    public bool IsEquipment => string.Equals(Kind, "equipment", StringComparison.OrdinalIgnoreCase);

    public IActionResult OnGet()
    {
        if (!LoadPage())
        {
            return RedirectToPage("/Sout/Workplaces");
        }

        return Page();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (!string.Equals(Kind, "equipment", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Kind, "materials", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/Sout/Workplaces");
        }

        Title = IsEquipment ? "Оборудование" : "Материалы и сырье";
        StubMessage = IsEquipment
            ? "Справочник оборудования. Редактирование и создание внутри выбранной папки или группы."
            : "Справочник материалов и сырья. Редактирование и создание внутри выбранной папки или группы.";
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = Title;
        ViewData["Subtitle"] = "общий для всех ЮЛ";

        Nodes = IsEquipment
            ? _store.LoadEquipment(Organization)
            : _store.LoadMaterials(Organization);

        var list = BuildRecordsFromForm();
        if (IsEquipment)
        {
            _store.SaveEquipment(Organization, list);
        }
        else
        {
            _store.SaveMaterials(Organization, list);
        }

        Message = "Изменения сохранены.";
        Nodes = list;
        return Page();
    }

    private bool LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;

        if (IsEquipment)
        {
            Title = "Оборудование";
            StubMessage = "Общий справочник оборудования для всех юридических лиц. Редактирование и создание внутри выбранной папки или группы.";
            Nodes = _store.LoadEquipment(Organization);
        }
        else if (string.Equals(Kind, "materials", StringComparison.OrdinalIgnoreCase))
        {
            Title = "Материалы и сырье";
            StubMessage = "Общий справочник материалов и сырья для всех юридических лиц. Редактирование и создание внутри выбранной папки или группы.";
            Nodes = _store.LoadMaterials(Organization);
        }
        else
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Nodes)
            {
                if (MatchesQuery(node, query))
                {
                    matchedIds.Add(node.Id);
                    AddAncestors(node, matchedIds);
                }
            }

            Nodes = Nodes.Where(node => matchedIds.Contains(node.Id)).ToList();
        }

        ViewData["Title"] = Title;
        ViewData["Subtitle"] = "общий для всех ЮЛ";
        return true;
    }

    private List<SoutCatalogRecord> BuildRecordsFromForm()
    {
        var existingById = Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var list = new List<SoutCatalogRecord>();
        var names = Request.Form["Name"];
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var typeNames = Request.Form["TypeName"];
        var inventoryNumbers = Request.Form["InventoryNumber"];
        var deletes = Request.Form["Delete"];

        for (var i = 0; i < names.Count; i++)
        {
            if (TrainingFormHelpers.IsMarkedForDelete(deletes, i))
            {
                continue;
            }

            var name = names[i].ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i])
                ? ids[i].ToString()!
                : Guid.NewGuid().ToString("N");
            existingById.TryGetValue(id, out var existing);
            var isGroup = i < isGroups.Count && isGroups[i].ToString() == "true";

            list.Add(new SoutCatalogRecord
            {
                Id = id,
                Organization = Organization,
                ParentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null),
                IsGroup = isGroup,
                Code = existing?.Code ?? string.Empty,
                Name = name,
                FullName = existing?.FullName ?? string.Empty,
                TypeName = isGroup
                    ? string.Empty
                    : i < typeNames.Count ? typeNames[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                InventoryNumber = isGroup
                    ? string.Empty
                    : i < inventoryNumbers.Count ? inventoryNumbers[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                RegistrationNumber = existing?.RegistrationNumber ?? string.Empty,
                StatePlate = existing?.StatePlate ?? string.Empty,
                FactoryNumber = existing?.FactoryNumber ?? string.Empty,
                SerialNumber = existing?.SerialNumber ?? string.Empty,
                PassportNumber = existing?.PassportNumber ?? string.Empty,
                ReleaseDate = existing?.ReleaseDate,
                InstallationDate = existing?.InstallationDate,
                Manufacturer = existing?.Manufacturer ?? string.Empty,
                FactorsSet = existing?.FactorsSet ?? string.Empty,
                InstallationLocation = existing?.InstallationLocation ?? string.Empty,
                WorkplaceIds = existing?.WorkplaceIds ?? [],
                Description = existing?.Description ?? string.Empty,
                Instruction = existing?.Instruction ?? string.Empty,
                Comment = existing?.Comment ?? string.Empty,
                CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
            });
        }

        return list;
    }

    private static bool MatchesQuery(SoutCatalogRecord node, string query) =>
        node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.InventoryNumber.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void AddAncestors(SoutCatalogRecord node, HashSet<string> matchedIds)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId))
        {
            return;
        }

        var parent = Nodes.FirstOrDefault(item => item.Id == node.ParentId);
        if (parent is null || !matchedIds.Add(parent.Id))
        {
            return;
        }

        AddAncestors(parent, matchedIds);
    }
}

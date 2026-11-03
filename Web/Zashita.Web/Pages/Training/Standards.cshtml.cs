using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class StandardsModel : PageModel
{
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingStandardHistoryStore _history;
    private readonly TrainingKindStore _kinds;
    private readonly OrganizationStore _orgs;
    private readonly PpeEmployeesService _employees;
    private readonly TrainingPersonStandardAssignmentStore _assignments;
    private readonly TrainingConductStore _conduct;

    public static readonly string[] PersonnelCategories = TrainingKindCatalogDefaults.PersonnelCategoryOptions.ToArray();

    public static readonly string[] TrainingMethods =
    [
        "Своими силами",
        "Внешняя организация"
    ];

    public StandardsModel(
        TrainingStandardsStore standards,
        TrainingStandardHistoryStore history,
        TrainingKindStore kinds,
        OrganizationStore orgs,
        PpeEmployeesService employees,
        TrainingPersonStandardAssignmentStore assignments,
        TrainingConductStore conduct)
    {
        _standards = standards;
        _history = history;
        _kinds = kinds;
        _orgs = orgs;
        _employees = employees;
        _assignments = assignments;
        _conduct = conduct;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<TrainingStandard> Standards { get; private set; } = [];
    public IReadOnlyList<string> Professions { get; private set; } = [];
    public IReadOnlyList<TrainingProgramRow> ProgramRows { get; private set; } = [];
    public IReadOnlyList<string> PersonnelCategoryOptions => PersonnelCategories;
    public IReadOnlyList<string> TrainingMethodOptions => TrainingMethods;
    public IReadOnlyList<string> SectionOptions { get; private set; } = TrainingKindCatalogDefaults.SectionOptions;
    public IReadOnlyList<TrainingStandardHistoryEntry> VersionHistory { get; private set; } = [];
    /// <summary>Периодичности программ текущего стандарта (имя → период).</summary>
    public Dictionary<string, string> ProgramPeriodicityMap { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Section { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ProgramQ { get; set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public List<string> SelectedProfessions { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public string PersonnelCategory { get; set; } = PersonnelCategories[0];

    [BindProperty]
    public string TrainingMethod { get; set; } = TrainingMethods[0];

    [BindProperty]
    public List<string> SelectedPrograms { get; set; } = [];

    public int Version { get; set; } = 1;

    public DateTime EffectiveFrom { get; set; } = DateTime.Today;

    public IActionResult OnGetExportExcel()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;

        if (string.IsNullOrWhiteSpace(Organization)
            || string.Equals(Organization, "Все", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "Выберите организацию, чтобы выгрузить стандарты.";
            return RedirectToPage(new { q = Q, editId = EditId });
        }

        var rows = LoadStandardsForOrganization(Organization);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows
                .Where(s =>
                    s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || s.AllProfessions.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase))
                    || (s.PersonnelCategory ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
                    || s.Items.Any(i => i.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var bytes = TrainingStandardsExcelExporter.Export(rows, Organization);
        var fileName = TrainingStandardsExcelExporter.BuildFileName(Organization, DateTime.Today);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;

        // Категория из query (?PersonnelCategory=...) — явный выбор пользователя;
        // не перетираем её сохранённой категорией стандарта.
        var categoryFromQuery = Request.Query.ContainsKey("PersonnelCategory")
                                || Request.Query.ContainsKey("personnelCategory");
        var requestedCategory = PersonnelCategory?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            // Сначала загружаем список стандартов, затем применяем шаблон.
            Standards = LoadStandardsForOrganization(Organization);

            var std = Standards.FirstOrDefault(s => s.Id == EditId);
            if (std is not null)
            {
                ApplyStandard(std);
                if (categoryFromQuery && !string.IsNullOrWhiteSpace(requestedCategory))
                {
                    PersonnelCategory = requestedCategory;
                }

                VersionHistory = _history.Get(EditId);
            }
        }

        // Список программ строим уже после выбора категории и SelectedPrograms.
        LoadLists();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        try
        {
            LoadLists();

            SelectedProfessions = Request.Form["SelectedProfessions"]
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => ProfessionMatcher.Normalize(p) is { Length: > 0 } n ? n : p!.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var names = Request.Form["SelectedPrograms"];
            var periodValues = Request.Form["ProgramPeriodicity"];
            var items = new List<string>();
            var periodicities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i]?.Trim() ?? string.Empty;
                if (name.Length == 0 || items.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                items.Add(name);
                var period = i < periodValues.Count && !string.IsNullOrWhiteSpace(periodValues[i])
                    ? periodValues[i]!.Trim()
                    : DefaultPeriodFor(name);
                periodicities[name] = period;
            }

            items = KeepOnlyExistingKinds(items);
            SelectedPrograms = items;
            ProgramPeriodicityMap = TrainingProgramCatalog.CopyPeriodicitiesIgnoreCase(
                periodicities.Where(kv => items.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)));

            var standardName = ResolveStandardName(Name, SelectedProfessions);
            Name = standardName;

            if (SelectedProfessions.Count == 0)
            {
                Message = "Выберите хотя бы одну профессию.";
                LoadLists();
                return Page();
            }

            if (items.Count == 0)
            {
                Message = "Выберите хотя бы одну программу обучения.";
                LoadLists();
                return Page();
            }

            if (string.IsNullOrWhiteSpace(standardName))
            {
                Message = "Укажите название или выберите профессию — название подставится автоматически.";
                LoadLists();
                return Page();
            }

            var existing = string.IsNullOrWhiteSpace(EditId)
                ? null
                : _standards.LoadAll().FirstOrDefault(s => s.Id == EditId);

            // Пересобрать периоды только по оставшимся после KeepOnlyExistingKinds программам.
            periodicities = items.ToDictionary(
                name => name,
                name => ProgramPeriodicityMap.TryGetValue(name, out var p) && !string.IsNullOrWhiteSpace(p)
                    ? p.Trim()
                    : DefaultPeriodFor(name),
                StringComparer.OrdinalIgnoreCase);
            ProgramPeriodicityMap = periodicities;

            var category = BuildCategory(items);
            var nextVersion = existing?.Version > 0 ? existing.Version : 1;
            var nextEffectiveFrom = existing?.EffectiveFrom ?? DateTime.UtcNow;

            if (existing is not null && HasMeaningfulChanges(
                    existing,
                    items,
                    periodicities,
                    KindPeriodicityLookup,
                    standardName,
                    SelectedProfessions,
                    PersonnelCategory,
                    TrainingMethod))
            {
                _history.Append(existing.Id, existing);
                nextVersion = existing.Version + 1;
                nextEffectiveFrom = DateTime.UtcNow;
            }

            // Сохраняем под каноническим юрлицом (полное ФИО ИП), чтобы список и отчёт не расходились.
            var organizationToSave = ResolveOrganizationForSave(Organization, existing?.Organization);
            var previousOrg = (existing?.Organization ?? string.Empty).Trim();

            var record = new TrainingStandard(
                existing?.Id ?? Guid.NewGuid().ToString("N"),
                standardName,
                organizationToSave,
                SelectedProfessions[0],
                items,
                existing?.CreatedAt ?? DateTime.UtcNow,
                existing?.Workers,
                category,
                SelectedProfessions,
                PersonnelCategory.Trim(),
                TrainingMethod.Trim(),
                periodicities,
                nextVersion,
                nextEffectiveFrom);

            _standards.Upsert(record);

            // После смены написания юрлица — переносим назначения и проведения, иначе люди дублируются в отчёте.
            if (previousOrg.Length > 0
                && !string.Equals(previousOrg, organizationToSave, StringComparison.OrdinalIgnoreCase))
            {
                _assignments.RemapOrganization(previousOrg, organizationToSave);
                _conduct.RenameOrganization(previousOrg, organizationToSave);
            }

            // Подразделение, ошибочно записанное как Organization после импорта/сохранения.
            _conduct.RepairMisfiledOrganizations(organizationToSave);

            StatusModel.InvalidateReportCache();
            return RedirectToPage(new
            {
                editId = record.Id,
                personnelCategory = PersonnelCategory,
                q = Q,
                section = Section,
                programQ = ProgramQ
            });
        }
        catch (Exception)
        {
            Message = "Не удалось сохранить стандарт. Проверьте программы и профессии и попробуйте ещё раз.";
            try
            {
                LoadLists();
            }
            catch
            {
                Standards = [];
                Professions = [];
                ProgramRows = [];
            }

            return Page();
        }
    }

    public IActionResult OnPostDelete(string id, string? q = null)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var org = _orgs.GetSelectedOrganization(HttpContext);
            var deleted = _standards.LoadAll()
                .FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            var isIeStandard = deleted is not null
                && (ContainsIeProfession(deleted.Name)
                    || ContainsIeProfession(deleted.Profession)
                    || deleted.AllProfessions.Any(ContainsIeProfession));

            _standards.Delete(id);
            _history.Remove(id);

            if (isIeStandard)
            {
                TrainingIndividualEntrepreneurStandards.RememberUserDeleted(
                    string.IsNullOrWhiteSpace(deleted!.Organization) ? org : deleted.Organization,
                    id,
                    _standards,
                    _assignments);
            }
            else
            {
                // Снять назначения удалённого стандарта.
                var list = _assignments.LoadAll();
                var next = list
                    .Where(a => !string.Equals(a.StandardId, id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (next.Count != list.Count)
                {
                    _assignments.SaveAll(next);
                }
            }

            StatusModel.InvalidateReportCache();
        }

        return RedirectToPage(new { q });
    }

    private static bool ContainsIeProfession(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Contains("индивидуальн", StringComparison.OrdinalIgnoreCase)
               && text.Contains("предпринимател", StringComparison.OrdinalIgnoreCase);
    }

    public IActionResult OnPostCopy(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return RedirectToPage();
        }

        var source = _standards.LoadAll().FirstOrDefault(s => s.Id == id);
        if (source is null)
        {
            return RedirectToPage();
        }

        var copyName = (source.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(copyName))
        {
            copyName = "Стандарт";
        }

        if (!copyName.EndsWith("(копия)", StringComparison.OrdinalIgnoreCase))
        {
            copyName = $"{copyName} (копия)";
        }

        var now = DateTime.UtcNow;
        var copy = new TrainingStandard(
            Guid.NewGuid().ToString("N"),
            copyName,
            source.Organization,
            source.Profession,
            source.Items?.ToList() ?? [],
            now,
            source.Workers?.ToList(),
            source.Category,
            source.Professions?.ToList(),
            source.PersonnelCategory,
            source.TrainingMethod,
            source.ProgramPeriodicities is { Count: > 0 }
                ? TrainingProgramCatalog.CopyPeriodicitiesIgnoreCase(source.ProgramPeriodicities)
                : null,
            Version: 1,
            EffectiveFrom: now);

        _standards.Upsert(copy);
        StatusModel.InvalidateReportCache();
        return RedirectToPage(new
        {
            editId = copy.Id,
            personnelCategory = copy.PersonnelCategory,
            q = Q,
            section = Section,
            programQ = ProgramQ
        });
    }

    public string PeriodicityFor(string programName)
    {
        if (ProgramPeriodicityMap.TryGetValue(programName, out var saved)
            && !string.IsNullOrWhiteSpace(saved))
        {
            return saved.Trim();
        }

        return DefaultPeriodFor(programName);
    }

    public IReadOnlyList<string> PeriodicityOptionsFor(string? current) =>
        TrainingProgramCatalog.PeriodicityOptionsFor(current);

    public bool IsRenamedProgram(string programName) =>
        ProgramRows.FirstOrDefault(row => row.Name.Equals(programName, StringComparison.OrdinalIgnoreCase))?.IsRenamed == true;

    public string SectionFor(string programName) =>
        ProgramRows.FirstOrDefault(row => row.Name.Equals(programName, StringComparison.OrdinalIgnoreCase))?.Section
        ?? "Прочее";

    private void ApplyStandard(TrainingStandard std)
    {
        Name = std.Name;
        SelectedProfessions = std.AllProfessions.ToList();
        SelectedPrograms = std.Items.ToList();
        ProgramPeriodicityMap = std.ProgramPeriodicities is { Count: > 0 }
            ? TrainingProgramCatalog.CopyPeriodicitiesIgnoreCase(std.ProgramPeriodicities)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        PersonnelCategory = string.IsNullOrWhiteSpace(std.PersonnelCategory)
            ? PersonnelCategories[0]
            : std.PersonnelCategory.Trim();
        TrainingMethod = string.IsNullOrWhiteSpace(std.TrainingMethod)
            ? TrainingMethods[0]
            : std.TrainingMethod.Trim();
        Version = std.Version > 0 ? std.Version : 1;
        EffectiveFrom = (std.EffectiveFrom ?? std.CreatedAt).ToLocalTime().Date;
    }

    private void LoadLists()
    {
        var rows = LoadStandardsForOrganization(Organization);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows
                .Where(s =>
                    s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.AllProfessions.Any(p => p.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (s.PersonnelCategory ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Items.Any(i => i.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        Standards = rows;
        Professions = _employees.GetByProfession(Organization).Keys
            .Select(p => ProfessionMatcher.Normalize(p) is { Length: > 0 } n ? n : p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allProgramRows = BuildProgramRows();
        SectionOptions = TrainingKindCatalogDefaults.CollectUsedSections(_kinds.Load());
        ProgramRows = FilterProgramRows(allProgramRows);
    }

    /// <summary>
    /// Как в отчёте статуса: «ИП Шварц Б.Э.» и «ИП Шварц Борис Эдуардович» — одна организация.
    /// </summary>
    private List<TrainingStandard> LoadStandardsForOrganization(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        return _standards.LoadAll()
            .Where(s =>
            {
                var standardOrg = (s.Organization ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(standardOrg))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(org))
                {
                    return false;
                }

                return string.Equals(standardOrg, org, StringComparison.OrdinalIgnoreCase)
                       || OrganizationMatcher.Matches(org, standardOrg);
            })
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private string ResolveOrganizationForSave(string selectedOrganization, string? existingOrganization)
    {
        var selected = (selectedOrganization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(existingOrganization)
            && OrganizationMatcher.Matches(selected, existingOrganization))
        {
            return OrganizationMatcher.PickPreferredOrganizationName(selected, existingOrganization.Trim());
        }

        var candidates = _standards.LoadAll()
            .Select(s => (s.Organization ?? string.Empty).Trim())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var resolved = OrganizationMatcher.ResolveOrganizationName(selected, candidates);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return OrganizationMatcher.PickPreferredOrganizationName(selected, resolved);
        }

        return selected;
    }

    private List<TrainingProgramRow> FilterProgramRows(IReadOnlyList<TrainingProgramRow> rows)
    {
        IEnumerable<TrainingProgramRow> filtered = rows;
        if (!string.IsNullOrWhiteSpace(Section))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.Section, Section.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(ProgramQ))
        {
            var query = ProgramQ.Trim();
            filtered = filtered.Where(row =>
                row.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .OrderBy(row => row.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private List<TrainingProgramRow> BuildProgramRows()
    {
        var allKinds = _kinds.Load();
        var groupsById = allKinds
            .Where(node => node.IsGroup && !node.MarkedForDeletion)
            .GroupBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var kindCatalog = allKinds
            .Where(node => !node.IsGroup && !node.MarkedForDeletion && !string.IsNullOrWhiteSpace(node.Name))
            .Where(node => TrainingKindCatalogDefaults.MatchesPersonnelCategory(node.PersonnelCategory, PersonnelCategory))
            .ToList();

        var rows = kindCatalog
            .GroupBy(node => node.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var kindNode = TrainingProgramCatalog.SelectKindForCategory(group, PersonnelCategory);
                var period = TrainingProgramCatalog.FromKindPeriodicity(kindNode.Periodicity);
                if (string.IsNullOrWhiteSpace(period))
                {
                    period = TrainingProgramCatalog.DefaultPeriodicity(kindNode.Name);
                }

                var section = TrainingNeedsCalendarBuilder.ResolveSection(kindNode, groupsById);

                return new TrainingProgramRow(
                    kindNode.Name.Trim(),
                    period,
                    false,
                    kindNode.PersonnelCategory,
                    section);
            })
            .ToList();

        var rowNames = rows.Select(row => row.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var savedName in SelectedPrograms
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Select(name => name.Trim())
                     .Where(name => !rowNames.Contains(name)))
        {
            rows.Add(new TrainingProgramRow(
                savedName,
                DefaultPeriodFor(savedName),
                true,
                string.Empty,
                "Прочее"));
        }

        return rows
            .OrderBy(row => row.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool HasMeaningfulChanges(
        TrainingStandard existing,
        IReadOnlyList<string> items,
        Dictionary<string, string> periodicities,
        IReadOnlyDictionary<string, string> kindLookup,
        string name,
        IReadOnlyList<string> selectedProfessions,
        string personnelCategory,
        string trainingMethod)
    {
        if (!string.Equals(existing.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var oldPrograms = existing.Items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var newPrograms = items.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (!oldPrograms.SequenceEqual(newPrograms, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var item in items)
        {
            var oldPeriod = TrainingProgramCatalog.ResolvePeriodicity(item, kindLookup, existing.ProgramPeriodicities);
            var newPeriod = periodicities.TryGetValue(item, out var period)
                ? period
                : TrainingProgramCatalog.ResolvePeriodicity(item, kindLookup, null);
            if (!string.Equals(oldPeriod, newPeriod, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var oldProfessions = existing.AllProfessions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var newProfessions = selectedProfessions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (!oldProfessions.SequenceEqual(newProfessions, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(existing.PersonnelCategory?.Trim(), personnelCategory.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.Equals(existing.TrainingMethod?.Trim(), trainingMethod.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, string> KindPeriodicityLookup =>
        TrainingProgramCatalog.BuildKindPeriodicityLookup(_kinds.Load(), PersonnelCategory);

    private string DefaultPeriodFor(string programName)
    {
        return TrainingProgramCatalog.ResolvePeriodicityForKind(
            programName,
            _kinds.Load(),
            PersonnelCategory);
    }

    private string BuildCategory(IReadOnlyList<string> items)
    {
        var catalog = _kinds.Load()
            .Where(node => !node.MarkedForDeletion)
            .ToList();
        var types = items
            .Select(item => catalog.FirstOrDefault(node =>
                !node.IsGroup &&
                node.Name.Equals(item, StringComparison.OrdinalIgnoreCase) &&
                TrainingKindCatalogDefaults.MatchesPersonnelCategory(node.PersonnelCategory, PersonnelCategory))?.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return types.Count switch
        {
            0 => "Обучение",
            1 => types[0],
            _ => string.Join("+", types)
        };
    }

    /// <summary>Оставляет только программы, которые есть в справочнике видов (удалённые отбрасываем).</summary>
    private List<string> KeepOnlyExistingKinds(IReadOnlyList<string> programNames)
    {
        var kindNames = _kinds.Load()
            .Where(node => !node.IsGroup && !node.MarkedForDeletion && !string.IsNullOrWhiteSpace(node.Name))
            .Select(node => node.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return programNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Where(kindNames.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveStandardName(string? name, IReadOnlyList<string> professions)
    {
        var trimmed = ProfessionMatcher.Normalize(name);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = (name ?? string.Empty).Trim();
        }

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        var fromProfessions = professions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => ProfessionMatcher.Normalize(p) is { Length: > 0 } n ? n : p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return fromProfessions.Count > 0
            ? string.Join(", ", fromProfessions)
            : string.Empty;
    }

    public sealed record TrainingProgramRow(
        string Name,
        string DefaultPeriodicity,
        bool IsRenamed,
        string PersonnelCategory = "",
        string Section = "Охрана труда");
}

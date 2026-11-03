using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class StandardsModel : PageModel
{
    private readonly MedStandardStore _store;
    private readonly MedFactorStore _factors;
    private readonly MedContextService _med;
    private readonly MedicalOrganizationStore _medicalOrganizations;
    private readonly OrganizationEmployeesService _employees;

    public StandardsModel(
        MedStandardStore store,
        MedFactorStore factors,
        MedContextService med,
        MedicalOrganizationStore medicalOrganizations,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _factors = factors;
        _med = med;
        _medicalOrganizations = medicalOrganizations;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedStandardRecord> Rows { get; private set; } = [];
    public IReadOnlyList<FactorPickItem> FactorCatalog { get; private set; } = [];
    public IReadOnlyList<MedicalOrganizationRecord> MedicalOrganizations { get; private set; } = [];
    public IReadOnlyList<string> ProfessionOptions { get; private set; } = [];
    public IReadOnlyDictionary<string, string> ProfessionDepartments { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> SelectedProfessions { get; private set; } = [];
    public IReadOnlyList<string> PeriodicityOptions { get; } = MedPeriodicity.CatalogOptions;
    public IReadOnlyList<string> ExamKindOptions { get; } =
    [
        "Периодический",
        "Предсменный / Предрейсовый",
        "Внеочередной",
        "Психиатрическое освидетельствование"
    ];

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Department { get; set; } = string.Empty;
    [BindProperty] public string Profession { get; set; } = string.Empty;
    [BindProperty] public List<string> SelectedProfessionsForm { get; set; } = [];
    [BindProperty] public bool IsPeriodic { get; set; } = true;
    [BindProperty] public bool IsPreShift { get; set; }
    [BindProperty] public bool IsExtraordinary { get; set; } = true;
    [BindProperty] public bool HasPsychiatric { get; set; }
    [BindProperty] public string DefaultMedicalOrganizationId { get; set; } = string.Empty;
    [BindProperty] public string Comment { get; set; } = string.Empty;

    [BindProperty] public List<string> RuleItemNumber { get; set; } = [];
    [BindProperty] public List<string> RuleFactorName { get; set; } = [];
    [BindProperty] public List<string> RulePeriodicity { get; set; } = [];
    [BindProperty] public List<string> RuleExamKind { get; set; } = [];

    public IReadOnlyList<MedStandardRule> SelectedRules { get; private set; } = [];
    public string? Message { get; private set; }

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Стандарты медосмотров";
        ViewData["Subtitle"] = Organization;
        var rows = _store.LoadByOrganization(Organization);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows
                .Where(x =>
                    (x.Name ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.Department ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.Profession ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.Comment ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (x.Rules ?? []).Any(r =>
                        (r.ItemNumber ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (r.FactorName ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (r.ExamKind ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        Rows = rows;
        FactorCatalog = BuildCatalog(_factors.LoadAll());
        MedicalOrganizations = _medicalOrganizations.LoadByOrganization(Organization);
        LoadProfessionLookups();
        Message = TempData["Message"] as string;
        if (string.IsNullOrWhiteSpace(Organization) && string.IsNullOrWhiteSpace(Message))
        {
            Message =
                "Сейчас выбрано «Все организации». Чтобы создать стандарт, выберите конкретную организацию в шапке.";
        }

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var row = _store.GetById(EditId);
            if (row is not null)
            {
                Name = row.Name ?? string.Empty;
                Department = row.Department ?? string.Empty;
                Profession = row.Profession ?? string.Empty;
                SelectedProfessions = MedStandardAssignmentBuilder.SplitProfessionAliases(row.Profession);
                IsPeriodic = row.IsPeriodic;
                IsPreShift = row.IsPreShift;
                IsExtraordinary = row.IsExtraordinary || row.AllowsExtraordinaryManual;
                HasPsychiatric = row.HasPsychiatric;
                DefaultMedicalOrganizationId = row.DefaultMedicalOrganizationId ?? string.Empty;
                Comment = row.Comment ?? string.Empty;
                SelectedRules = row.Rules ?? [];
            }
        }
    }

    public IActionResult OnPostSave()
    {
        Organization = _med.GetOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(Organization))
        {
            TempData["Message"] =
                "Выберите конкретную организацию в шапке (не «Все организации») — иначе стандарт не попадёт в отчёт.";
            return RedirectToPage(new { editId = EditId });
        }

        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId!;
        var professions = SelectedProfessionsForm
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (professions.Count == 0 && !string.IsNullOrWhiteSpace(Profession))
        {
            professions = MedStandardAssignmentBuilder.SplitProfessionAliases(Profession).ToList();
        }

        if (professions.Count == 0 && !string.IsNullOrWhiteSpace(Name))
        {
            professions = MedStandardAssignmentBuilder.SplitProfessionAliases(Name).ToList();
        }

        // Убираем служебный хвост «(копия)» из профессий — иначе стандарт не совпадёт со штатом.
        professions = professions
            .SelectMany(MedStandardAssignmentBuilder.SplitProfessionAliases)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roster = _employees.LoadForMedReport(Organization);
        professions = MedStandardAssignmentBuilder
            .ResolveProfessionsAgainstRoster(professions, roster)
            .ToList();

        if (professions.Count == 0)
        {
            TempData["Message"] = "Добавьте хотя бы одну профессию — по ней факторы будут подставляться в направления.";
            return RedirectToPage(new { editId = string.IsNullOrWhiteSpace(EditId) ? null : id });
        }

        var name = (Name ?? string.Empty).Trim();
        if (name.StartsWith("Стандарт:", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Стандарт:".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = professions.Count > 0
                ? string.Join(", ", professions)
                : "Медосмотр";
        }

        _store.Upsert(new MedStandardRecord
        {
            Id = id,
            Organization = Organization,
            Name = name,
            Department = (Department ?? string.Empty).Trim(),
            Profession = string.Join("; ", professions),
            IsPeriodic = IsPeriodic,
            IsPreShift = IsPreShift,
            IsExtraordinary = IsExtraordinary,
            HasPsychiatric = HasPsychiatric,
            AllowsExtraordinaryManual = IsExtraordinary,
            IsPreliminaryPrimary = false,
            IsPreliminaryRepeat = false,
            DefaultMedicalOrganizationId = (DefaultMedicalOrganizationId ?? string.Empty).Trim(),
            Comment = (Comment ?? string.Empty).Trim(),
            Rules = BuildRulesFromForm()
        });
        StatusModel.InvalidateRowsCache();
        // Проверяем, что запись реально на диске (защита от «сохранено», но файл пуст).
        var verified = _store.GetById(id);
        var total = _store.LoadByOrganization(Organization).Count;
        if (verified is null)
        {
            TempData["Message"] =
                "Ошибка: стандарт не записался на диск. Проверьте права на папку App_Data/med и повторите сохранение.";
            return RedirectToPage(new { editId = id });
        }

        TempData["Message"] =
            $"Стандарт сохранён для «{Organization}» (стандартов в организации: {total}" +
            (verified.HasPsychiatric ? ", психиатрия: да" : "") +
            "). Откройте «Отчёт по медосмотрам» — пункты подтянутся по профессии.";
        return RedirectToPage(new { editId = id });
    }

    public IActionResult OnPostDelete(string deleteId, string? q = null)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
            StatusModel.InvalidateRowsCache();
            TempData["Message"] = "Стандарт удалён.";
        }

        return RedirectToPage(new { q });
    }

    public IActionResult OnPostCopy(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return RedirectToPage(new { q = Q });
        }

        var source = _store.GetById(id);
        if (source is null)
        {
            TempData["Message"] = "Стандарт не найден — возможно, он был удалён.";
            return RedirectToPage(new { q = Q });
        }

        Organization = _med.GetOrganization(HttpContext);
        var org = !string.IsNullOrWhiteSpace(Organization)
            ? Organization
            : source.Organization?.Trim() ?? string.Empty;

        var copyName = (source.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(copyName))
        {
            copyName = string.IsNullOrWhiteSpace(source.Profession) ? "Медосмотр" : source.Profession.Trim();
        }

        if (!copyName.EndsWith("(копия)", StringComparison.OrdinalIgnoreCase))
        {
            copyName = $"{copyName} (копия)";
        }

        var copy = new MedStandardRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Organization = org,
            Name = copyName,
            Department = source.Department ?? string.Empty,
            Profession = source.Profession ?? string.Empty,
            IsPeriodic = source.IsPeriodic,
            IsPreShift = source.IsPreShift,
            IsExtraordinary = source.IsExtraordinary,
            HasPsychiatric = source.HasPsychiatric,
            AllowsExtraordinaryManual = source.AllowsExtraordinaryManual,
            IsPreliminaryPrimary = source.IsPreliminaryPrimary,
            IsPreliminaryRepeat = source.IsPreliminaryRepeat,
            DefaultMedicalOrganizationId = source.DefaultMedicalOrganizationId ?? string.Empty,
            Comment = source.Comment ?? string.Empty,
            Rules = (source.Rules ?? [])
                .Select(rule => new MedStandardRule
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ItemNumber = rule.ItemNumber,
                    FactorName = rule.FactorName,
                    ExamKind = rule.ExamKind,
                    Periodicity = rule.Periodicity,
                    IsPsychiatricActivity = rule.IsPsychiatricActivity,
                    IsActive = rule.IsActive
                })
                .ToList()
        };

        _store.Upsert(copy);
        StatusModel.InvalidateRowsCache();
        TempData["Message"] = $"Создана копия «{copy.Name}». При необходимости измените профессию и сохраните.";
        return RedirectToPage(new { editId = copy.Id, q = Q });
    }

    private void LoadProfessionLookups()
    {
        // Только актуальный штат: уволенные и пометки «(удалено)» из 1С не в список профессий стандарта.
        var employees = _employees.LoadForMedReport(Organization)
            .Where(e => !string.IsNullOrWhiteSpace(e.Profession))
            .Where(e => !IsDeletedProfessionLabel(e.Profession))
            .ToList();

        ProfessionOptions = employees
            .Select(e => ProfessionMatcher.Normalize(e.Profession ?? string.Empty))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Where(p => !IsDeletedProfessionLabel(p))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ProfessionDepartments = employees
            .Where(e => !string.IsNullOrWhiteSpace(e.Department))
            .GroupBy(e => ProfessionMatcher.Normalize(e.Profession ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(e => e.Department!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDeletedProfessionLabel(string? profession)
    {
        var t = (profession ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return false;
        }

        return t.Contains("удален", StringComparison.OrdinalIgnoreCase)
               || t.Contains("удалён", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> SplitProfessions(string? value) =>
        MedStandardAssignmentBuilder.SplitProfessionAliases(value).ToList();

    private List<MedStandardRule> BuildRulesFromForm()
    {
        var rules = new List<MedStandardRule>();
        var count = Math.Max(
            Math.Max(RuleItemNumber.Count, RuleFactorName.Count),
            Math.Max(RulePeriodicity.Count, RuleExamKind.Count));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var rawItem = i < RuleItemNumber.Count ? RuleItemNumber[i]?.Trim() ?? "" : "";
            if (string.Equals(rawItem, "—", StringComparison.Ordinal))
            {
                rawItem = string.Empty;
            }

            var factorName = i < RuleFactorName.Count ? RuleFactorName[i]?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(rawItem) && string.IsNullOrWhiteSpace(factorName))
            {
                continue;
            }

            var normalized = MedItemNumber.Normalize(rawItem);
            var examKind = i < RuleExamKind.Count && !string.IsNullOrWhiteSpace(RuleExamKind[i])
                ? MedStandardAssignmentBuilder.NormalizeExamType(RuleExamKind[i])
                : "Периодический";
            // Один пункт — один раз на вид осмотра (имя фактора не создаёт второй ряд в отчёте).
            var key = string.IsNullOrWhiteSpace(normalized)
                ? $"{factorName}|{examKind}"
                : $"{normalized}|{examKind}";
            if (!seen.Add(key))
            {
                continue;
            }

            var periodicity = i < RulePeriodicity.Count
                ? MedPeriodicity.Normalize(RulePeriodicity[i])
                : MedPeriodicity.OnceAYear;

            rules.Add(new MedStandardRule
            {
                ItemNumber = normalized,
                FactorName = factorName,
                Periodicity = periodicity,
                ExamKind = examKind,
                IsPsychiatricActivity = examKind.Contains("псих", StringComparison.OrdinalIgnoreCase)
            });
        }

        return rules;
    }

    private static List<FactorPickItem> BuildCatalog(IReadOnlyList<MedFactorNode> nodes)
    {
        var groups = nodes
            .Where(n => n.IsGroup)
            .ToDictionary(n => n.Id, n => n.Name, StringComparer.Ordinal);

        var orderedLeaves = TrainingCatalogTree
            .Order(nodes.ToList(), n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name)
            .Where(item => !item.Node.IsGroup)
            .Select(item => item.Node)
            .ToList();

        return orderedLeaves
            .Select(x =>
            {
                var itemNumber = string.IsNullOrWhiteSpace(x.ItemNumber)
                    ? "—"
                    : MedItemNumber.Display(x.ItemNumber);
                var periodicity = string.IsNullOrWhiteSpace(x.Periodicity)
                    ? MedPeriodicity.OnceAYear
                    : MedPeriodicity.Normalize(x.Periodicity);
                var groupName = !string.IsNullOrWhiteSpace(x.ParentId) && groups.TryGetValue(x.ParentId, out var g)
                    ? g
                    : "—";

                return new FactorPickItem(
                    x.Id,
                    itemNumber,
                    string.IsNullOrWhiteSpace(x.Name) ? "—" : x.Name.Trim(),
                    periodicity,
                    groupName);
            })
            .ToList();
    }

    public sealed record FactorPickItem(
        string Id,
        string ItemNumber,
        string Name,
        string Periodicity,
        string GroupName);
}

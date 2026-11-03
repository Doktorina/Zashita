using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

public sealed class AnnualPlanEditTableModel
{
    public string Section { get; set; } = string.Empty;
    public string TbodyId { get; set; } = string.Empty;
    public string AddButtonId { get; set; } = string.Empty;
    public IReadOnlyList<string> Directions { get; set; } = [];
    public IReadOnlyList<string> Categories { get; set; } = [];
    public IReadOnlyList<string> Periodicities { get; set; } = [];
    public IReadOnlyList<string> HeadCounts { get; set; } = [];
    public IReadOnlyList<string> Providers { get; set; } = [];
    public IReadOnlyList<string> Months { get; set; } = [];
    public IReadOnlyList<string> MonthLabels { get; set; } = [];

    public string Prefix => Section.Equals("profession", StringComparison.OrdinalIgnoreCase) ? "Profession" : "General";
    public int RowCount => new[] { Directions.Count, Categories.Count, Periodicities.Count, HeadCounts.Count, Providers.Count, Months.Count }.Max();
}

[RequireLogin]
public class PlannedModel : PageModel
{
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingAnnualPlanStore _plans;
    private readonly OrganizationStore _orgs;
    private readonly PpeEmployeesService _employees;

    public PlannedModel(
        TrainingStandardsStore standards,
        TrainingAnnualPlanStore plans,
        OrganizationStore orgs,
        PpeEmployeesService employees)
    {
        _standards = standards;
        _plans = plans;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)]
    public int PlanYear { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty]
    public string ApproverPosition { get; set; } = "Руководитель организации";

    [BindProperty]
    public string ApproverName { get; set; } = string.Empty;

    [BindProperty]
    public List<string> GeneralDirection { get; set; } = [];

    [BindProperty]
    public List<string> GeneralCategory { get; set; } = [];

    [BindProperty]
    public List<string> GeneralPeriodicity { get; set; } = [];

    [BindProperty]
    public List<string> GeneralHeadCount { get; set; } = [];

    [BindProperty]
    public List<string> GeneralProvider { get; set; } = [];

    [BindProperty]
    public List<string> GeneralMonths { get; set; } = [];

    [BindProperty]
    public List<string> ProfessionDirection { get; set; } = [];

    [BindProperty]
    public List<string> ProfessionCategory { get; set; } = [];

    [BindProperty]
    public List<string> ProfessionPeriodicity { get; set; } = [];

    [BindProperty]
    public List<string> ProfessionHeadCount { get; set; } = [];

    [BindProperty]
    public List<string> ProfessionProvider { get; set; } = [];

    [BindProperty]
    public List<string> ProfessionMonths { get; set; } = [];

    public string Organization { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public string PreviewHtml { get; private set; } = string.Empty;
    public IReadOnlyList<string> EditMonthLabels => MonthLabels;

    public static readonly string[] MonthLabels =
        ["Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"];

    public void OnGet(string? message = null)
    {
        Message = message;
        LoadPage(regenerate: false);
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var record = BuildRecordFromForm();
        _plans.Upsert(record);
        return RedirectToPage(new { PlanYear, Department, message = "План сохранён." });
    }

    public IActionResult OnPostRegenerate()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var generated = TrainingAnnualPlanBuilder.FromDocument(
            TrainingAnnualPlanBuilder.Build(
                _standards.LoadAll(),
                Organization,
                _employees.GetByProfession(Organization),
                new DateTime(PlanYear, 12, 31)));
        _plans.Upsert(generated);
        return RedirectToPage(new { PlanYear, Department, message = "План сформирован заново из справочников." });
    }

    private void LoadPage(bool regenerate)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;

        TrainingAnnualPlanRecord record;
        if (regenerate)
        {
            record = TrainingAnnualPlanBuilder.FromDocument(
                TrainingAnnualPlanBuilder.Build(
                    _standards.LoadAll(),
                    Organization,
                    _employees.GetByProfession(Organization),
                    new DateTime(PlanYear, 12, 31)));
        }
        else
        {
            record = _plans.Get(Organization, PlanYear) ??
                     TrainingAnnualPlanBuilder.FromDocument(
                         TrainingAnnualPlanBuilder.Build(
                             _standards.LoadAll(),
                             Organization,
                             _employees.GetByProfession(Organization),
                             new DateTime(PlanYear, 12, 31)));
        }

        LoadFormFromRecord(record);
        PreviewHtml = TrainingAnnualPlanBuilder.BuildPreviewHtml(TrainingAnnualPlanBuilder.ToDocument(record));
    }

    private TrainingAnnualPlanRecord BuildRecordFromForm() =>
        TrainingAnnualPlanBuilder.FromForm(
            Organization,
            PlanYear,
            ApproverPosition,
            ApproverName,
            GeneralDirection,
            GeneralCategory,
            GeneralPeriodicity,
            GeneralHeadCount,
            GeneralProvider,
            GeneralMonths,
            ProfessionDirection,
            ProfessionCategory,
            ProfessionPeriodicity,
            ProfessionHeadCount,
            ProfessionProvider,
            ProfessionMonths);

    private void LoadFormFromRecord(TrainingAnnualPlanRecord record)
    {
        ApproverPosition = record.ApproverPosition;
        ApproverName = record.ApproverName;
        LoadSection(record.GeneralRows, GeneralDirection, GeneralCategory, GeneralPeriodicity, GeneralHeadCount, GeneralProvider, GeneralMonths);
        LoadSection(record.ProfessionRows, ProfessionDirection, ProfessionCategory, ProfessionPeriodicity, ProfessionHeadCount, ProfessionProvider, ProfessionMonths);
        EnsureEditRows();
    }

    private static void LoadSection(
        IReadOnlyList<TrainingAnnualPlanRowData> rows,
        List<string> directions,
        List<string> categories,
        List<string> periodicities,
        List<string> headCounts,
        List<string> providers,
        List<string> months)
    {
        directions.Clear();
        categories.Clear();
        periodicities.Clear();
        headCounts.Clear();
        providers.Clear();
        months.Clear();

        foreach (var row in rows)
        {
            directions.Add(row.Direction);
            categories.Add(row.Category);
            periodicities.Add(row.Periodicity);
            headCounts.Add(row.HeadCount.ToString());
            providers.Add(row.Provider);
            months.Add(TrainingAnnualPlanBuilder.FormatMonths(row.PlannedMonths));
        }
    }

    private void EnsureEditRows()
    {
        EnsureSection(GeneralDirection, GeneralCategory, GeneralPeriodicity, GeneralHeadCount, GeneralProvider, GeneralMonths, 1);
        EnsureSection(ProfessionDirection, ProfessionCategory, ProfessionPeriodicity, ProfessionHeadCount, ProfessionProvider, ProfessionMonths, 1);
    }

    private static void EnsureSection(
        List<string> directions,
        List<string> categories,
        List<string> periodicities,
        List<string> headCounts,
        List<string> providers,
        List<string> months,
        int minRows)
    {
        while (directions.Count < minRows) directions.Add(string.Empty);
        while (categories.Count < minRows) categories.Add(string.Empty);
        while (periodicities.Count < minRows) periodicities.Add("1 раз в год");
        while (headCounts.Count < minRows) headCounts.Add("1");
        while (providers.Count < minRows) providers.Add("Внутреннее");
        while (months.Count < minRows) months.Add(string.Empty);
    }
}

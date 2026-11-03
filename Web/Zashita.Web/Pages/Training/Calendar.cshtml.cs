using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Electrical;
using Zashita.Core.Employees;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class CalendarModel : PageModel
{
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingConductStore _conduct;
    private readonly TrainingPersonStandardAssignmentStore _assignments;
    private readonly TrainingKindStore _kinds;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly ElectricalKnowledgeCheckStore _electricalKnowledge;

    public CalendarModel(
        TrainingStandardsStore standards,
        TrainingConductStore conduct,
        TrainingPersonStandardAssignmentStore assignments,
        TrainingKindStore kinds,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        ElectricalKnowledgeCheckStore electricalKnowledge)
    {
        _standards = standards;
        _conduct = conduct;
        _assignments = assignments;
        _kinds = kinds;
        _orgs = orgs;
        _employees = employees;
        _electricalKnowledge = electricalKnowledge;
    }

    [BindProperty(SupportsGet = true)]
    public int PlanYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    /// <summary>Выбранные виды (программы), через запятую или повторённый query kinds.</summary>
    [BindProperty(SupportsGet = true)]
    public List<string> Kinds { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? DrillProgram { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DrillMonth { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public TrainingNeedsCalendarMatrix Matrix { get; private set; } = new();
    public IReadOnlyList<TrainingStatusRow> DrillRows { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Календарь потребностей в обучении";
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["Subtitle"] = Organization;

        if (PlanYear is < 2000 or > 2100)
        {
            PlanYear = DateTime.Today.Year;
        }

        var asOf = DateTime.Today;
        var statusRows = LoadStatusRows();
        Departments = statusRows
            .Select(r => r.Department)
            .Where(d => !string.IsNullOrWhiteSpace(d) && d != "—")
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Department))
        {
            statusRows = statusRows
                .Where(r => string.Equals(r.Department, Department, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var selected = ResolveSelectedKinds();
        Matrix = TrainingNeedsCalendarBuilder.BuildMatrix(
            statusRows,
            _kinds.Load(),
            PlanYear,
            asOf,
            selected);

        if (!string.IsNullOrWhiteSpace(DrillProgram) && DrillMonth is >= 1 and <= 12)
        {
            DrillRows = TrainingNeedsCalendarBuilder.DrillDown(Matrix, DrillProgram, DrillMonth.Value);
        }
    }

    public IActionResult OnPostApplyFilters(int planYear, string? department, List<string>? kinds)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        PlanYear = planYear is >= 2000 and <= 2100 ? planYear : DateTime.Today.Year;
        Department = department;
        TrainingFilterSession.SaveCalendarKinds(HttpContext, kinds);

        return RedirectToPage(new
        {
            planYear = PlanYear,
            department = Department
        });
    }

    private IReadOnlyList<string> ResolveSelectedKinds()
    {
        var fromQuery = ExpandKindsQuery();
        if (fromQuery.Count > 0)
        {
            TrainingFilterSession.SaveCalendarKinds(HttpContext, fromQuery);
            return fromQuery;
        }

        return TrainingFilterSession.LoadCalendarKinds(HttpContext);
    }

    private List<string> ExpandKindsQuery()
    {
        var fromList = Kinds
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .SelectMany(k => k.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return fromList;
    }

    private List<TrainingStatusRow> LoadStatusRows()
    {
        var employeeList = _employees.LoadForTrainingReport(Organization)
            .Select(e => (Organization, e.Name, e.Profession, e.Department))
            .ToList();
        var kinds = _kinds.Load();
        var standards = TrainingIndividualEntrepreneurStandards.EnsureForOrganization(
            Organization,
            _standards,
            _assignments,
            employeeList,
            kinds,
            out _);
        var assignmentMap = _assignments.LoadAll()
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First().StandardId, StringComparer.OrdinalIgnoreCase);

        var rows = TrainingReportBuilder.BuildManualStatusReport(
                standards,
                Organization,
                employeeList,
                DateTime.Today,
                _conduct.LoadAll(),
                assignmentMap,
                kinds)
            .ToList();
        rows.AddRange(ElectricalTrainingReportBridge.ToTrainingRows(
            _electricalKnowledge.Load(Organization),
            DateTime.Today,
            employeeList));
        return TrainingReportBuilder.ApplyStaffDepartments(
                TrainingReportBuilder.UnifyPersonDepartments(rows),
                employeeList)
            .Where(r => !string.IsNullOrWhiteSpace(r.Program))
            .ToList();
    }
}

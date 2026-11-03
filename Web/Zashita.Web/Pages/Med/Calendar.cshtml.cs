using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class CalendarModel : PageModel
{
    private readonly MedExamStore _exams;
    private readonly MedStandardStore _standards;
    private readonly MedDirectionStore _directions;
    private readonly MedContextService _med;
    private readonly OrganizationEmployeesService _employees;

    public CalendarModel(
        MedExamStore exams,
        MedStandardStore standards,
        MedDirectionStore directions,
        MedContextService med,
        OrganizationEmployeesService employees)
    {
        _exams = exams;
        _standards = standards;
        _directions = directions;
        _med = med;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)] public int PlanYear { get; set; }
    [BindProperty(SupportsGet = true)] public string? Department { get; set; }
    [BindProperty(SupportsGet = true)] public List<string> ExamTypes { get; set; } = [];

    public string Organization { get; private set; } = string.Empty;
    public MedNeedsCalendarMatrix Matrix { get; private set; } = new(DateTime.Today.Year, [], []);
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<string> ExamTypeOptions { get; private set; } = [];
    public IReadOnlyList<string> SelectedExamTypes { get; private set; } = [];

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Календарь медосмотров";
        ViewData["Subtitle"] = Organization;

        if (PlanYear is < 2000 or > 2100)
        {
            PlanYear = DateTime.Today.Year;
        }

        var asOf = DateTime.Today;
        var statusRows = MedStatusReportCleanup.Clean(LoadStatusRows());

        Departments = statusRows
            .Select(r => r.Department)
            .Where(d => !string.IsNullOrWhiteSpace(d) && d != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Department))
        {
            statusRows = statusRows
                .Where(r => string.Equals(r.Department, Department, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        SelectedExamTypes = (ExamTypes ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Matrix = MedNeedsCalendarBuilder.BuildMatrix(statusRows, PlanYear, asOf, SelectedExamTypes);
        ExamTypeOptions = Matrix.ExamTypeOptions;
    }

    private IReadOnlyList<MedStatusRow> LoadStatusRows()
    {
        var employees = _employees.LoadForMedReport(Organization)
            .Select(e => (Organization, e))
            .ToList();
        var exams = _exams.LoadByOrganization(Organization);
        var standards = _standards.LoadByOrganization(Organization);
        var directionKeys = MedDirectionMatcher.BuildIssuedKeysForOrganizations(
            _directions.LoadAll(),
            [Organization]);

        var synced = MedStandardAssignmentBuilder.CollapseDuplicateExams(
            MedStandardAssignmentBuilder.ExpandExamsFromStandards(employees, exams, standards));
        return MedStatusBuilder.Build(employees, synced, DateTime.Today, directionKeys);
    }
}

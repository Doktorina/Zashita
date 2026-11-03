using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class ScheduleModel : PageModel
{
    private readonly MedExamStore _exams;
    private readonly MedStandardStore _standards;
    private readonly MedScheduleStore _scheduleStore;
    private readonly MedDirectionStore _directions;
    private readonly MedContextService _med;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public ScheduleModel(
        MedExamStore exams,
        MedStandardStore standards,
        MedScheduleStore scheduleStore,
        MedDirectionStore directions,
        MedContextService med,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _exams = exams;
        _standards = standards;
        _scheduleStore = scheduleStore;
        _directions = directions;
        _med = med;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)] public string? Department { get; set; }
    [BindProperty(SupportsGet = true)] public string? ExamType { get; set; }
    [BindProperty(SupportsGet = true)] public string? NeedFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? DueMonth { get; set; }
    [BindProperty(SupportsGet = true)] public int? PlanYear { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedStatusRow> NeedsRows { get; private set; } = [];
    public IReadOnlyList<MedNeedPersonGroup> NeedPersonGroups { get; private set; } = [];
    public int NeedsOverdueCount { get; private set; }
    public int NeedsNotConductedCount { get; private set; }
    public int NeedsExpiringCount { get; private set; }
    public int NeedsPassedCount { get; private set; }
    public int NeedsPlannedCount { get; private set; }
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<string> ExamTypeOptions { get; private set; } = [];
    public int ScheduleDocumentsCount { get; private set; }
    public Dictionary<string, string> HrStatusByName { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Планирование медосмотров";
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(Organization) ? "Все организации" : Organization;

        HrStatusByName = string.IsNullOrWhiteSpace(Organization)
            ? EmployeeHrStatus.BuildMap(_orgs.GetVisibleOrganizations(HttpContext).SelectMany(o => _employees.LoadForMedReport(o)))
            : EmployeeHrStatus.BuildMap(_employees.LoadForMedReport(Organization));

        if (PlanYear is null or < 2000 or > 2100)
        {
            PlanYear = DateTime.Today.Year;
        }

        var asOf = DateTime.Today;
        var allRows = MedStatusReportCleanup.Clean(LoadStatusRows());

        Departments = allRows
            .Select(r => r.Department)
            .Where(d => !string.IsNullOrWhiteSpace(d) && d != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ExamTypeOptions = allRows
            .Select(r => r.ExamType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var withType = allRows
            .Where(r => !string.IsNullOrWhiteSpace(r.ExamType) || !string.IsNullOrWhiteSpace(r.EmployeeName))
            .ToList();
        NeedsOverdueCount = CountPeople(withType, asOf, MedNeedsCalendarBuilder.FilterOverdue);
        NeedsNotConductedCount = CountPeople(withType, asOf, MedNeedsCalendarBuilder.FilterNotConducted);
        NeedsExpiringCount = CountPeople(withType, asOf, MedNeedsCalendarBuilder.FilterExpiring);
        NeedsPassedCount = CountPeople(withType, asOf, MedNeedsCalendarBuilder.FilterPassed);
        NeedsPlannedCount = CountPeople(withType, asOf, MedNeedsCalendarBuilder.FilterPlanned);

        var filter = string.IsNullOrWhiteSpace(NeedFilter) ? MedNeedsCalendarBuilder.FilterAll : NeedFilter.Trim();
        var planYear = PlanYear.Value;

        NeedsRows = MedNeedsCalendarBuilder.FilterNeeds(withType, filter, asOf)
            .Where(r => string.IsNullOrWhiteSpace(Department) ||
                        string.Equals(r.Department, Department, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(ExamType) ||
                        string.Equals(r.ExamType, ExamType, StringComparison.OrdinalIgnoreCase))
            .Where(r => DueMonth is null or < 1 or > 12
                        || MedNeedsCalendarBuilder.ResolveDueMonth(r, planYear, asOf) == DueMonth.Value)
            .Where(r => string.IsNullOrWhiteSpace(Q) ||
                        r.EmployeeName.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                        r.Profession.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                        r.ExamType.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                        r.Department.Contains(Q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.ExamType, StringComparer.CurrentCultureIgnoreCase)
            .Take(500)
            .ToList();

        NeedPersonGroups = BuildNeedPersonGroups(NeedsRows);

        ScheduleDocumentsCount = string.IsNullOrWhiteSpace(Organization)
            ? _orgs.GetVisibleOrganizations(HttpContext).Sum(org => _scheduleStore.LoadByOrganization(org).Count)
            : _scheduleStore.LoadByOrganization(Organization).Count;
    }

    private static int CountPeople(IEnumerable<MedStatusRow> rows, DateTime asOf, string filter) =>
        MedNeedsCalendarBuilder.FilterNeeds(rows, filter, asOf)
            .Select(MedNeedsCalendarBuilder.PersonKey)
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static List<MedNeedPersonGroup> BuildNeedPersonGroups(IReadOnlyList<MedStatusRow> rows) =>
        rows
            .GroupBy(row => MedNeedsCalendarBuilder.PersonKey(row), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(group =>
            {
                var first = group.First();
                return new MedNeedPersonGroup(
                    group.Key,
                    first.EmployeeName?.Trim() ?? string.Empty,
                    first.Department?.Trim() ?? string.Empty,
                    first.Profession?.Trim() ?? string.Empty,
                    group
                        .OrderBy(r => r.ExamType, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(r => r.ItemNumber, StringComparer.CurrentCultureIgnoreCase)
                        .ToList());
            })
            .OrderBy(g => g.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Person, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private IReadOnlyList<MedStatusRow> LoadStatusRows()
    {
        var orgs = string.IsNullOrWhiteSpace(Organization)
            ? _employees.ResolveOrganizationsForAllReports(
                _orgs.GetVisibleOrganizations(HttpContext),
                _orgs.IsOrganizationHidden).ToList()
            : [Organization];

        var employeePairs = _employees.LoadPairsForMedReport(orgs).ToList();
        var exams = new List<MedExamRecord>();
        var standards = new List<MedStandardRecord>();

        foreach (var org in orgs.Where(o => !string.IsNullOrWhiteSpace(o)))
        {
            _med.EnsureInitialized(org);
            exams.AddRange(_exams.LoadByOrganization(org));
            standards.AddRange(_standards.LoadByOrganization(org));
        }

        var directionKeys = MedDirectionMatcher.BuildIssuedKeysForOrganizations(
            _directions.LoadAll(),
            orgs.Where(o => !string.IsNullOrWhiteSpace(o)));

        var synced = MedStandardAssignmentBuilder.CollapseDuplicateExams(
            MedStandardAssignmentBuilder.ExpandExamsFromStandards(employeePairs, exams, standards));
        return MedStatusBuilder.Build(employeePairs, synced, DateTime.Today, directionKeys);
    }
}

public sealed record MedNeedPersonGroup(
    string Key,
    string Person,
    string Department,
    string Profession,
    IReadOnlyList<MedStatusRow> Items);

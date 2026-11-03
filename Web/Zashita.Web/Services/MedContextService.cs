using Zashita.Core.Employees;
using Zashita.Core.Med;

namespace Zashita.Web.Services;

public sealed class MedContextService
{
    private readonly MedExamStore _exams;
    private readonly MedPersonListStore _personLists;
    private readonly MedRestrictionStore _restrictions;
    private readonly MedScheduleStore _schedules;
    private readonly MedStandardStore _standards;
    private readonly MedDirectionStore _directions;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly object _statsCacheSync = new();
    private string _statsCacheKey = string.Empty;
    private DateTime _statsCacheUtc = DateTime.MinValue;
    private MedDashboardStats? _statsCache;

    public MedContextService(
        MedExamStore exams,
        MedPersonListStore personLists,
        MedRestrictionStore restrictions,
        MedScheduleStore schedules,
        MedStandardStore standards,
        MedDirectionStore directions,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _exams = exams;
        _personLists = personLists;
        _restrictions = restrictions;
        _schedules = schedules;
        _standards = standards;
        _directions = directions;
        _orgs = orgs;
        _employees = employees;
    }

    public string GetOrganization(HttpContext httpContext) =>
        _orgs.GetSelectedOrganization(httpContext);

    public void EnsureInitialized(string organization)
    {
        var employees = _employees.GetByProfession(organization);
        _exams.EnsureSeeded(organization, employees);
        _personLists.EnsureSeeded(organization);
        _restrictions.EnsureSeeded(organization);
        _schedules.EnsureSeeded(organization);
    }

    public IReadOnlyList<MedExamRecord> GetExams(string organization)
    {
        EnsureInitialized(organization);
        return _exams.LoadByOrganization(organization);
    }

    public MedDashboardStats GetDashboardStats(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        var cacheKey = $"{org}|{DateTime.Today:yyyy-MM-dd}";
        lock (_statsCacheSync)
        {
            if (_statsCache is not null
                && string.Equals(_statsCacheKey, cacheKey, StringComparison.Ordinal)
                && DateTime.UtcNow - _statsCacheUtc < TimeSpan.FromSeconds(90))
            {
                return _statsCache;
            }
        }

        EnsureInitialized(org);
        var today = DateTime.Today;
        var employees = _employees.LoadForMedReport(org)
            .Select(e => (org, e))
            .ToList();
        var exams = _exams.LoadByOrganization(org);
        var standards = _standards.LoadByOrganization(org);
        var synced = MedStandardAssignmentBuilder.CollapseDuplicateExams(
            MedStandardAssignmentBuilder.ExpandExamsFromStandards(employees, exams, standards));
        var directionKeys = MedDirectionMatcher.BuildIssuedKeysForOrganizations(
            _directions.LoadAll(),
            [org]);
        var statusRows = MedStatusBuilder.Build(employees, synced, today, directionKeys);
        var stats = MedDashboardBuilder.BuildFromStatusRows(statusRows, today, synced);

        lock (_statsCacheSync)
        {
            _statsCacheKey = cacheKey;
            _statsCacheUtc = DateTime.UtcNow;
            _statsCache = stats;
        }

        return stats;
    }

    public IReadOnlyList<string> GetEmployeeNames(string organization) =>
        _employees.GetEmployeeNames(organization);

    public IReadOnlyList<OrganizationEmployee> GetEmployees(string organization) =>
        _employees.LoadForMedReport(organization);

    public IReadOnlyList<string> GetProfessions(string organization) =>
        _employees.GetByProfession(organization).Keys.OrderBy(x => x).ToList();

    public void UpsertExams(IEnumerable<MedExamRecord> records)
    {
        _exams.UpsertMany(records);
        lock (_statsCacheSync)
        {
            _statsCache = null;
            _statsCacheKey = string.Empty;
            _statsCacheUtc = DateTime.MinValue;
        }
    }
}

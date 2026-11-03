using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

[RequireLogin]
public class AssessmentListModel : PageModel
{
    private readonly RiskWorkplaceStore _workplaces;
    private readonly RiskPreparationStore _preparation;
    private readonly RiskAssessmentObjectStore _objects;
    private readonly RisksWorkspaceContext _risksWorkspace;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public AssessmentListModel(
        RiskWorkplaceStore workplaces,
        RiskPreparationStore preparation,
        RiskAssessmentObjectStore objects,
        RisksWorkspaceContext risksWorkspace,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _workplaces = workplaces;
        _preparation = preparation;
        _objects = objects;
        _risksWorkspace = risksWorkspace;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    /// <summary>all | overdue | notconducted | expiring | assessed | planned</summary>
    [BindProperty(SupportsGet = true)]
    public string? NeedFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public string SelectedObjectId { get; private set; } = RiskAssessmentObjectIds.OrganizationWide;
    public string SelectedObjectDisplayName { get; private set; } = RiskAssessmentObjectIds.OrganizationWideName;
    public string ActiveNeed { get; private set; } = RiskWorkplaceNeedsBuilder.NeedAll;
    public IReadOnlyList<RiskWorkplaceNeedRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<string> StatusOptions { get; } =
    [
        RiskHazardStatuses.NotConducted,
        RiskHazardStatuses.Overdue,
        "Истекает в течение 30 дней"
    ];
    public int OverdueCount { get; private set; }
    public int NotConductedCount { get; private set; }
    public int ExpiringCount { get; private set; }
    public int AssessedCount { get; private set; }
    public int PlannedCount { get; private set; }

    private static DateTime _lastStaffSyncUtc = DateTime.MinValue;
    private static DateTime _lastWorkerCountSyncUtc = DateTime.MinValue;
    private static readonly object StaffSyncGate = new();

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        RiskOrganizationBootstrap.Ensure(Organization, _workplaces, _preparation);
        MaybeSyncFromStaff();

        SelectedObjectId = _risksWorkspace.ResolveObjectId(HttpContext, Organization, ObjectId);
        SelectedObjectDisplayName = _risksWorkspace.DisplayName(Organization, SelectedObjectId);

        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Перечень рабочих мест, подлежащих оценке профессиональных рисков";
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(Organization)
            ? SelectedObjectDisplayName
            : $"{Organization} · {SelectedObjectDisplayName}";

        ActiveNeed = RiskWorkplaceNeedsBuilder.NormalizeNeedFilter(NeedFilter);
        var tree = _workplaces.LoadTreeByOrganization(Organization)
            .Where(n => n.IsGroup || !RiskPrintChrome.IsIndividualEntrepreneurWorkplace(n))
            .ToList();

        var projectionId = RisksWorkspaceContext.ProjectionObjectId(SelectedObjectId);
        var assessmentObject = _objects.Find(Organization, projectionId);
        tree = RisksWorkspaceContext.ApplyWorkplaceScope(
            tree,
            SelectedObjectId,
            assessmentObject,
            persistMembership: obj => _objects.SaveMembership(Organization, obj.Id, obj.WorkplaceIds ?? []));

        var today = DateTime.Today;

        OverdueCount = RiskWorkplaceNeedsBuilder.CountByBucket(
            tree, Organization, RiskWorkplaceNeedsBuilder.NeedOverdue, today);
        NotConductedCount = RiskWorkplaceNeedsBuilder.CountByBucket(
            tree, Organization, RiskWorkplaceNeedsBuilder.NeedNotConducted, today);
        ExpiringCount = RiskWorkplaceNeedsBuilder.CountByBucket(
            tree, Organization, RiskWorkplaceNeedsBuilder.NeedExpiring, today);
        AssessedCount = RiskWorkplaceNeedsBuilder.CountByBucket(
            tree, Organization, RiskWorkplaceNeedsBuilder.NeedAssessed, today);
        PlannedCount = RiskWorkplaceNeedsBuilder.CountByBucket(
            tree, Organization, RiskWorkplaceNeedsBuilder.NeedPlanned, today);

        var all = RiskWorkplaceNeedsBuilder.BuildFiltered(tree, Organization, ActiveNeed, today);

        Departments = RiskWorkplaceNeedsBuilder.BuildFiltered(tree, Organization, RiskWorkplaceNeedsBuilder.NeedAll, today)
            .Select(r => r.Department)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        IEnumerable<RiskWorkplaceNeedRow> query = all;

        if (!string.IsNullOrWhiteSpace(Department))
        {
            query = query.Where(r =>
                r.Department.Contains(Department, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(Status))
        {
            query = query.Where(r =>
                string.Equals(r.Status, Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            query = query.Where(r =>
                r.Profession.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.WorkplaceNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Department.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Status.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        Rows = query.ToList();
    }

    private void MaybeSyncFromStaff()
    {
        if (string.IsNullOrWhiteSpace(Organization))
        {
            return;
        }

        // Пустые дубли профессии убираем сразу (не ждём троттлинг 30 мин).
        var collapsed = SoutWorkplaceStaffSync.CollapseEmptyProfessionDuplicates(Organization, _workplaces);

        var leaves = _workplaces.LoadByOrganization(Organization).Count(w => !w.IsGroup);
        var needSync = leaves == 0 || collapsed > 0;
        lock (StaffSyncGate)
        {
            var now = DateTime.UtcNow;
            if (!needSync && now - _lastStaffSyncUtc < TimeSpan.FromMinutes(30))
            {
                return;
            }

            var staff = _employees.LoadForReport(Organization);
            _ = SoutWorkplaceStaffSync.SyncMissingFromStaff(Organization, staff, _workplaces);
            if (needSync || now - _lastWorkerCountSyncUtc >= TimeSpan.FromHours(6))
            {
                _ = SoutWorkplaceStaffSync.SyncWorkerCountsFromStaff(Organization, staff, _workplaces);
                _lastWorkerCountSyncUtc = now;
            }

            _lastStaffSyncUtc = now;
        }
    }
}

using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

/// <summary>Загрузка сводки отчётов для главной и страниц модулей.</summary>
public sealed class HomeReportSummaryService
{
    private readonly MedContextService _med;
    private readonly WorkplaceStore _workplaces;
    private readonly RiskWorkplaceStore _riskWorkplaces;
    private readonly TrainingStandardsStore _trainingStandards;
    private readonly TrainingConductStore _trainingConduct;
    private readonly TrainingPersonStandardAssignmentStore _trainingAssignments;
    private readonly TrainingKindStore _trainingKinds;
    private readonly TrainingPlanningStore _trainingPlanning;
    private readonly OrganizationEmployeesService _employees;
    private readonly SoutAnnualPlanStore _annualPlans;
    private readonly WorkplaceAssignmentStore _assignments;
    private readonly RiskAssessmentObjectStore _assessmentObjects;

    public HomeReportSummaryService(
        MedContextService med,
        WorkplaceStore workplaces,
        RiskWorkplaceStore riskWorkplaces,
        TrainingStandardsStore trainingStandards,
        TrainingConductStore trainingConduct,
        TrainingPersonStandardAssignmentStore trainingAssignments,
        TrainingKindStore trainingKinds,
        TrainingPlanningStore trainingPlanning,
        OrganizationEmployeesService employees,
        SoutAnnualPlanStore annualPlans,
        WorkplaceAssignmentStore assignments,
        RiskAssessmentObjectStore assessmentObjects)
    {
        _med = med;
        _workplaces = workplaces;
        _riskWorkplaces = riskWorkplaces;
        _trainingStandards = trainingStandards;
        _trainingConduct = trainingConduct;
        _trainingAssignments = trainingAssignments;
        _trainingKinds = trainingKinds;
        _trainingPlanning = trainingPlanning;
        _employees = employees;
        _annualPlans = annualPlans;
        _assignments = assignments;
        _assessmentObjects = assessmentObjects;
    }

    public HomeReportSummaries Load(string organization, DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var org = (organization ?? string.Empty).Trim();
        lock (_cacheSync)
        {
            if (string.Equals(_cacheOrg, org, StringComparison.OrdinalIgnoreCase)
                && _cacheDay == today
                && DateTime.UtcNow - _cacheUtc < TimeSpan.FromSeconds(90)
                && _cacheAll is not null)
            {
                return _cacheAll;
            }
        }

        var all = BuildAll(org, today);
        lock (_cacheSync)
        {
            _cacheOrg = org;
            _cacheDay = today;
            _cacheUtc = DateTime.UtcNow;
            _cacheAll = all;
            _trainingCacheOrg = org;
            _trainingCacheDay = today;
            _trainingCacheUtc = DateTime.UtcNow;
            _trainingCache = all.Training;
        }

        return all;
    }

    /// <summary>Только сводка нужного модуля — без полного отчёта по обучению, если он не нужен.</summary>
    public HomeReportSummaryBuilder.ModuleSummary LoadModule(
        string moduleKey,
        string organization,
        DateTime? asOf = null,
        string? riskObjectId = null)
    {
        var key = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
        var today = (asOf ?? DateTime.Today).Date;
        var org = (organization ?? string.Empty).Trim();

        return key switch
        {
            "med" or "медосмотры" => HomeReportSummaryBuilder.FromMed(_med.GetDashboardStats(org)),
            "sout" or "спецоценка" or "специальная оценка" or "специальная-оценка" =>
                HomeReportSummaryBuilder.FromSout(
                    _workplaces.LoadByOrganization(org),
                    today,
                    SoutWorkplaceNeedsBuilder.ResolvePlannedWorkplaceIds(
                        _annualPlans.LoadAll(), org, today.Year)),
            "risks" or "риски" => LoadRisksModule(org, riskObjectId, today),
            "training" or "обучение" => BuildTrainingSummary(org, today),
            _ => ModuleReportSummaryCatalog.Pick(key, Load(org, today))
        };
    }

    /// <summary>Круговые диаграммы ознакомления для главных страниц Рисков и СОУТ.</summary>
    public IReadOnlyList<FamiliarizationChartData> LoadFamiliarizationCharts(
        string moduleKey,
        string organization,
        DateTime? asOf = null,
        string? riskObjectId = null)
    {
        var key = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return [];
        }

        var today = (asOf ?? DateTime.Today).Date;
        return key switch
        {
            "risks" or "риски" => LoadRiskFamiliarizationCharts(org, riskObjectId),
            "sout" or "спецоценка" or "специальная оценка" or "специальная-оценка" =>
            [
                WorkplaceFamiliarizationChartBuilder.ForSout(
                    org,
                    _workplaces.LoadTreeByOrganization(org),
                    _assignments.LoadDocumentsByOrganization(org),
                    _employees.Load(org),
                    today)
            ],
            _ => []
        };
    }

    /// <summary>Сводка рисков с учётом выбранного объекта (карты и статусы — свои на каждом объекте).</summary>
    private HomeReportSummaryBuilder.ModuleSummary LoadRisksModule(
        string organization,
        string? riskObjectId,
        DateTime today)
    {
        var workplaces = ScopeRiskWorkplaces(organization, riskObjectId);
        return HomeReportSummaryBuilder.FromRisks(workplaces, today);
    }

    private IReadOnlyList<FamiliarizationChartData> LoadRiskFamiliarizationCharts(
        string organization,
        string? riskObjectId)
    {
        var all = WorkplaceFamiliarizationChartBuilder.ForRisksByObjects(
            organization,
            _riskWorkplaces.LoadTreeByOrganization(organization),
            _assessmentObjects.Load(organization),
            _employees.LoadForReport(organization).Select(e => (organization, e)),
            _employees.LoadIncludingInactive(organization));

        if (RisksWorkspaceContext.IsAllObjects(riskObjectId))
        {
            return all;
        }

        var focusId = RisksWorkspaceContext.ProjectionObjectId(riskObjectId);
        var focused = all
            .Where(c => string.Equals(c.ObjectId, focusId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return focused.Count > 0 ? focused : all;
    }

    private List<WorkplaceRecord> ScopeRiskWorkplaces(string organization, string? riskObjectId)
    {
        var org = (organization ?? string.Empty).Trim();
        var tree = _riskWorkplaces.LoadTreeByOrganization(org).ToList();
        if (org.Length == 0 || RisksWorkspaceContext.IsAllObjects(riskObjectId))
        {
            // «Все объекты» / без выбора — как вкладка «по организации».
            return RisksWorkspaceContext.ApplyWorkplaceScope(
                tree,
                RiskAssessmentObjectIds.OrganizationWide,
                _assessmentObjects.Find(org, RiskAssessmentObjectIds.OrganizationWide));
        }

        var objectId = RisksWorkspaceContext.ProjectionObjectId(riskObjectId);
        var assessmentObject = _assessmentObjects.Find(org, objectId);
        return RisksWorkspaceContext.ApplyWorkplaceScope(
            tree,
            objectId,
            assessmentObject,
            persistMembership: obj => _assessmentObjects.SaveMembership(org, obj.Id, obj.WorkplaceIds ?? []));
    }

    /// <summary>Сводка покрытия по каждой организации (режим «Все»).</summary>
    public IReadOnlyList<HomeReportSummaryBuilder.OrgCoverageRow> LoadModuleOrgCoverage(
        string moduleKey,
        IReadOnlyList<string> organizations,
        DateTime? asOf = null)
    {
        var key = (moduleKey ?? string.Empty).Trim().ToLowerInvariant();
        var today = (asOf ?? DateTime.Today).Date;
        var orgs = (organizations ?? [])
            .Select(o => (o ?? string.Empty).Trim())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cacheKey = $"{key}|{string.Join('\u001e', orgs)}";
        lock (_cacheSync)
        {
            if (string.Equals(_coverageCacheKey, cacheKey, StringComparison.Ordinal)
                && _coverageCacheDay == today
                && DateTime.UtcNow - _coverageCacheUtc < TimeSpan.FromSeconds(90)
                && _coverageCache is not null)
            {
                return _coverageCache;
            }
        }

        var rows = orgs
            .Select(org => HomeReportSummaryBuilder.ClassifyOrganization(org, key, LoadModule(key, org, today)))
            .ToList();

        lock (_cacheSync)
        {
            _coverageCacheKey = cacheKey;
            _coverageCacheDay = today;
            _coverageCacheUtc = DateTime.UtcNow;
            _coverageCache = rows;
        }

        return rows;
    }

    private HomeReportSummaryBuilder.ModuleSummary BuildTrainingSummary(string organization, DateTime today)
    {
        lock (_cacheSync)
        {
            if (string.Equals(_cacheOrg, organization, StringComparison.OrdinalIgnoreCase)
                && _cacheDay == today
                && DateTime.UtcNow - _cacheUtc < TimeSpan.FromSeconds(90)
                && _cacheAll is not null)
            {
                return _cacheAll.Training;
            }

            if (string.Equals(_trainingCacheOrg, organization, StringComparison.OrdinalIgnoreCase)
                && _trainingCacheDay == today
                && DateTime.UtcNow - _trainingCacheUtc < TimeSpan.FromSeconds(90)
                && _trainingCache is not null)
            {
                return _trainingCache;
            }
        }

        var summary = ComputeTrainingSummary(organization, today);
        lock (_cacheSync)
        {
            _trainingCacheOrg = organization;
            _trainingCacheDay = today;
            _trainingCacheUtc = DateTime.UtcNow;
            _trainingCache = summary;
        }

        return summary;
    }

    private HomeReportSummaryBuilder.ModuleSummary ComputeTrainingSummary(string organization, DateTime today)
    {
        var employeeList = _employees.LoadForTrainingReport(organization)
            .Select(e => (organization, e.Name, e.Profession, e.Department))
            .ToList();
        var assignmentMap = _trainingAssignments.LoadAll()
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First().StandardId, StringComparer.OrdinalIgnoreCase);

        var trainingRows = TrainingReportBuilder.BuildManualStatusReport(
            _trainingStandards.LoadAll(),
            organization,
            employeeList,
            today,
            _trainingConduct.LoadAll(),
            assignmentMap,
            _trainingKinds.Load());

        var plannedTraining = _trainingPlanning.LoadAll()
            .Count(r => string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase));

        return HomeReportSummaryBuilder.FromTraining(trainingRows, today, plannedTraining);
    }

    private HomeReportSummaries BuildAll(string organization, DateTime today)
    {
        var workplaces = _workplaces.LoadByOrganization(organization);
        var riskWorkplaces = _riskWorkplaces.LoadByOrganization(organization);
        var medStats = _med.GetDashboardStats(organization);
        var training = ComputeTrainingSummary(organization, today);

        var plannedSoutIds = SoutWorkplaceNeedsBuilder.ResolvePlannedWorkplaceIds(
            _annualPlans.LoadAll(),
            organization,
            today.Year);

        return new HomeReportSummaries(
            HomeReportSummaryBuilder.FromMed(medStats),
            training,
            HomeReportSummaryBuilder.FromSout(workplaces, today, plannedSoutIds),
            HomeReportSummaryBuilder.FromRisks(riskWorkplaces, today));
    }

    private readonly object _cacheSync = new();
    private string _cacheOrg = string.Empty;
    private DateTime _cacheDay = DateTime.MinValue;
    private DateTime _cacheUtc = DateTime.MinValue;
    private HomeReportSummaries? _cacheAll;
    private string _trainingCacheOrg = string.Empty;
    private DateTime _trainingCacheDay = DateTime.MinValue;
    private DateTime _trainingCacheUtc = DateTime.MinValue;
    private HomeReportSummaryBuilder.ModuleSummary? _trainingCache;
    private string _coverageCacheKey = string.Empty;
    private DateTime _coverageCacheDay = DateTime.MinValue;
    private DateTime _coverageCacheUtc = DateTime.MinValue;
    private IReadOnlyList<HomeReportSummaryBuilder.OrgCoverageRow>? _coverageCache;
}

public sealed record ModuleOrgCoverageSummaryView(
    string ModuleKey,
    string Title,
    string ReportUrl,
    string ReportButtonLabel,
    IReadOnlyList<HomeReportSummaryBuilder.OrgCoverageRow> Rows,
    int CompleteCount,
    int PartialCount,
    int NoneCount,
    int OverdueCount,
    int EmptyCount);

public sealed record HomeReportSummaries(
    HomeReportSummaryBuilder.ModuleSummary Med,
    HomeReportSummaryBuilder.ModuleSummary Training,
    HomeReportSummaryBuilder.ModuleSummary Sout,
    HomeReportSummaryBuilder.ModuleSummary Risks);

public sealed record ModuleReportSummaryView(
    string ModuleKey,
    string Title,
    IReadOnlyList<ModuleReportSummaryMetric> Metrics,
    string ReportUrl = "",
    string ReportButtonLabel = "Просмотреть таблицы отчёта",
    IReadOnlyList<FamiliarizationChartData>? FamiliarizationCharts = null,
    string? ScopeHint = null);

public sealed record ModuleReportSummaryMetric(string Label, int Value, string Tone, string? FilterUrl = null);

public static class ModuleReportSummaryCatalog
{
    public static ModuleReportSummaryView Build(
        string moduleKey,
        HomeReportSummaryBuilder.ModuleSummary data,
        string? reportUrl = null,
        IReadOnlyList<FamiliarizationChartData>? familiarizationCharts = null,
        string? scopeHint = null)
    {
        var url = reportUrl?.Trim() ?? string.Empty;
        var charts = familiarizationCharts is { Count: > 0 } ? familiarizationCharts : null;
        return moduleKey.ToLowerInvariant() switch
        {
            "med" or "медосмотры" => new ModuleReportSummaryView(
                "med",
                "Медосмотры",
                [
                    Metric("истекло", data.Line1, "danger", MedScheduleUrl("overdue")),
                    Metric("истекает (30 дней)", data.Line2, "warn", MedScheduleUrl("expiring")),
                    Metric("не пройдено", data.Line3, "danger", MedScheduleUrl("notconducted")),
                    Metric("пройдено", data.Line4, "success", MedScheduleUrl("passed")),
                    Metric("запланировано", data.Line5, "neutral", MedScheduleUrl("planned"))
                ],
                "/Med/Schedule",
                "Открыть планирование"),
            "training" or "обучение" => new ModuleReportSummaryView(
                "training",
                "Обучение",
                [
                    Metric("просрочено", data.Line1, "danger", PlanningUrl("overdue")),
                    Metric("истекает", data.Line2, "warn", PlanningUrl("expiring")),
                    Metric("не обучено", data.Line3, "danger", PlanningUrl("notconducted")),
                    Metric("обучено", data.Line4, "success", PlanningUrl("trained")),
                    Metric("запланировано", data.Line5, "neutral", PlanningUrl("planned"))
                ],
                "/Training/Planning",
                "Открыть планирование"),
            "sout" or "спецоценка" or "специальная оценка" or "специальная-оценка" => new ModuleReportSummaryView(
                "sout",
                "СОУТ",
                [
                    Metric("нет специальной оценки", data.Line1, "danger", SoutStatusUrl("notconducted")),
                    Metric("просрочена", data.Line2, "danger", SoutStatusUrl("overdue")),
                    Metric("истекает", data.Line3, "warn", SoutStatusUrl("expiring")),
                    Metric("оценено", data.Line4, "success", SoutStatusUrl("assessed")),
                    Metric("запланировано", data.Line5, "neutral", SoutStatusUrl("planned"))
                ],
                string.IsNullOrWhiteSpace(url) ? "/Sout/Status" : url,
                "Просмотреть таблицы отчёта",
                charts),
            "risks" or "риски" => new ModuleReportSummaryView(
                "risks",
                "Риски",
                [
                    Metric("нет оценки рисков", data.Line1, "danger", AssessmentListUrl("notconducted")),
                    Metric("просрочена", data.Line2, "danger", AssessmentListUrl("overdue")),
                    Metric("истекает", data.Line3, "warn", AssessmentListUrl("expiring")),
                    Metric("оценено", data.Line4, "success", AssessmentListUrl("assessed")),
                    Metric("запланировано", data.Line5, "neutral", AssessmentListUrl("planned"))
                ],
                string.IsNullOrWhiteSpace(url) ? "/Risks/AssessmentList" : url,
                "Открыть перечень РМ",
                charts,
                scopeHint),
            _ => new ModuleReportSummaryView(moduleKey, moduleKey, [], url)
        };
    }

    public static HomeReportSummaryBuilder.ModuleSummary Pick(string moduleKey, HomeReportSummaries all) =>
        moduleKey.ToLowerInvariant() switch
        {
            "med" or "медосмотры" => all.Med,
            "training" or "обучение" => all.Training,
            "sout" or "спецоценка" or "специальная оценка" or "специальная-оценка" => all.Sout,
            "risks" or "риски" => all.Risks,
            _ => new HomeReportSummaryBuilder.ModuleSummary(0, 0, 0, 0, 0)
        };

    private static ModuleReportSummaryMetric Metric(string label, int value, string tone, string? filterUrl = null)
    {
        var applied = tone switch
        {
            "danger" or "warn" when value > 0 => tone,
            "success" when value > 0 => "success",
            _ => "neutral"
        };
        return new ModuleReportSummaryMetric(
            label,
            value,
            applied,
            string.IsNullOrWhiteSpace(filterUrl) ? null : filterUrl.Trim());
    }

    private static string PlanningUrl(string needFilter) =>
        $"/Training/Planning?needFilter={Uri.EscapeDataString(needFilter)}";

    private static string MedScheduleUrl(string needFilter) =>
        $"/Med/Schedule?needFilter={Uri.EscapeDataString(needFilter)}";

    private static string AssessmentListUrl(string needFilter) =>
        $"/Risks/AssessmentList?needFilter={Uri.EscapeDataString(needFilter)}";

    private static string SoutStatusUrl(string needFilter) =>
        $"/Sout/Status?needFilter={Uri.EscapeDataString(needFilter)}";
}

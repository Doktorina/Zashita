using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core;
using Zashita.Core.Employees;
using Zashita.Core.Excel;
using Zashita.Core.Incidents;
using Zashita.Core.Med;
using Zashita.Core.Ppe;
using Zashita.Core.Risks;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages;

[RequireLogin]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class IndexModel : PageModel
{
    private readonly ExcelWorkbookStore _store;
    private readonly MedContextService _med;
    private readonly WorkplaceStore _workplaces;
    private readonly RiskWorkplaceStore _riskWorkplaces;
    private readonly TrainingStandardsStore _trainingStandards;
    private readonly TrainingConductStore _trainingConduct;
    private readonly TrainingPersonStandardAssignmentStore _trainingAssignments;
    private readonly TrainingKindStore _trainingKinds;
    private readonly PpeEmployeesService _employees;
    private readonly OrganizationEmployeesService _orgEmployees;
    private readonly IssueStandardsStore _issueStandards;
    private readonly SavedSiosCardsStore _ppeCards;
    private readonly MedExamStore _medExams;
    private readonly WorkplaceAccidentStore _accidents;
    private readonly AccidentInvestigationStore _investigations;
    private readonly MicrotraumaStore _microtraumas;
    private readonly TrainingPlanningStore _trainingPlanning;
    private readonly MicrotraumaInvestigationStore _microInvestigations;
    private readonly StaffingOnboardingQuery _staffingOnboarding;
    private readonly OrganizationStore _orgs;
    private readonly HomeDeadlinesService _homeDeadlines;
    private readonly SoutAnnualPlanStore _annualPlans;

    public IndexModel(
        ExcelWorkbookStore store,
        MedContextService med,
        WorkplaceStore workplaces,
        RiskWorkplaceStore riskWorkplaces,
        TrainingStandardsStore trainingStandards,
        TrainingConductStore trainingConduct,
        TrainingPersonStandardAssignmentStore trainingAssignments,
        TrainingKindStore trainingKinds,
        TrainingPlanningStore trainingPlanning,
        PpeEmployeesService employees,
        OrganizationEmployeesService orgEmployees,
        IssueStandardsStore issueStandards,
        SavedSiosCardsStore ppeCards,
        MedExamStore medExams,
        WorkplaceAccidentStore accidents,
        AccidentInvestigationStore investigations,
        MicrotraumaStore microtraumas,
        MicrotraumaInvestigationStore microInvestigations,
        StaffingOnboardingQuery staffingOnboarding,
        OrganizationStore orgs,
        HomeDeadlinesService homeDeadlines,
        SoutAnnualPlanStore annualPlans)
    {
        _store = store;
        _med = med;
        _workplaces = workplaces;
        _riskWorkplaces = riskWorkplaces;
        _trainingStandards = trainingStandards;
        _trainingConduct = trainingConduct;
        _trainingAssignments = trainingAssignments;
        _trainingKinds = trainingKinds;
        _trainingPlanning = trainingPlanning;
        _employees = employees;
        _orgEmployees = orgEmployees;
        _issueStandards = issueStandards;
        _ppeCards = ppeCards;
        _medExams = medExams;
        _accidents = accidents;
        _investigations = investigations;
        _microtraumas = microtraumas;
        _microInvestigations = microInvestigations;
        _staffingOnboarding = staffingOnboarding;
        _orgs = orgs;
        _homeDeadlines = homeDeadlines;
        _annualPlans = annualPlans;
    }

    public string? FileName => _store.FileName;
    public MedDashboardStats MedStats { get; private set; } = new();
    public IncidentHubStats IncidentStats { get; private set; } = new();
    public IReadOnlyList<HomeDeadlineRow> Deadlines { get; private set; } = [];
    public IReadOnlyList<HomeDeadlineSection> DeadlineSections { get; private set; } = [];
    public IReadOnlyList<HomeTimelineRow> Timeline { get; private set; } = [];
    public IReadOnlyList<StaffingOnboardingItem> StaffingItems { get; private set; } = [];
    public IReadOnlyList<StaffingChangeEvent> StaffingAlerts { get; private set; } = [];
    public IReadOnlyDictionary<string, string> StaffingGaps { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public int OpenTasks { get; private set; }
    public int OverdueTasks { get; private set; }
    public int TrainingOverdue { get; private set; }
    public MedDashboardStats DisplayMedStats { get; private set; } = new();
    public HomeReportSummaryBuilder.ModuleSummary MedSummary { get; private set; } = new(0, 0, 0, 0, 0);
    public HomeReportSummaryBuilder.ModuleSummary TrainingSummary { get; private set; } = new(0, 0, 0, 0, 0);
    public int TrainingPlannedCount { get; private set; }
    public HomeReportSummaryBuilder.ModuleSummary SoutSummary { get; private set; } = new(0, 0, 0, 0, 0);
    public HomeReportSummaryBuilder.ModuleSummary RisksSummary { get; private set; } = new(0, 0, 0, 0, 0);
    public int DaysWithoutAccident { get; private set; }
    public DirectionReadinessBuilder.DirectionReadiness Readiness { get; private set; } =
        new(0, 0, 0, 0, 0, "нет данных", "нет данных", "нет данных", "нет данных", "нет данных");

    public void OnGet()
    {
        var organization = _med.GetOrganization(HttpContext);
        var today = DateTime.Today;

        if (TryApplyHomeCache(organization, today))
        {
            return;
        }

        var staffingItems = _staffingOnboarding.LoadOpen(organization, take: 20);
        StaffingItems = staffingItems;
        StaffingAlerts = staffingItems.Select(i => i.Event).ToList();
        StaffingGaps = staffingItems.ToDictionary(
            i => i.Event.Id,
            i => i.Gap.Label,
            StringComparer.OrdinalIgnoreCase);

        var exams = _medExams.LoadByOrganization(organization);
        var workplaces = _workplaces.LoadByOrganization(organization);
        var riskWorkplaces = _riskWorkplaces.LoadByOrganization(organization);
        var trainingConduct = _trainingConduct.LoadAll();
        var trainingStandards = _trainingStandards.LoadAll();

        MedStats = _med.GetDashboardStats(organization);
        DisplayMedStats = MedStats;
        IncidentStats = IncidentDashboardBuilder.Build(
            organization,
            _accidents.LoadAll(),
            _investigations.LoadAll(),
            _microtraumas.LoadAll(),
            _microInvestigations.LoadAll(),
            today);

        var employees = _employees.GetByProfession(organization);
        var assignmentMap = _trainingAssignments.LoadAll()
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First().StandardId, StringComparer.OrdinalIgnoreCase);

        Readiness = DirectionReadinessBuilder.Build(
            organization,
            workplaces,
            trainingStandards,
            trainingConduct,
            employees,
            _issueStandards.LoadAll(),
            _ppeCards.LoadAll(),
            exams,
            today,
            assignmentMap,
            riskWorkplaces);

        var trainingRows = BuildTrainingStatusRows(organization, today, assignmentMap, trainingStandards, trainingConduct);
        var allDeadlines = _homeDeadlines.Load(organization, today);
        DeadlineSections = _homeDeadlines.PreviewBySection(allDeadlines, perSection: 3);
        Deadlines = DeadlineSections
            .SelectMany(s => s.Items)
            .Select(c => new HomeDeadlineRow(c.Title, c.Subtitle, c.Badge, c.BadgeKind))
            .ToList();
        if (DeadlineSections.Count == 0)
        {
            DeadlineSections =
            [
                new HomeDeadlineSection(
                    "Сроки",
                    "/Tasks",
                    [
                        new HomeDeadlineItem(
                            "Сроки",
                            0,
                            "Нет ближайших сроков",
                            "Медосмотры, обучение и спецоценка появятся здесь автоматически",
                            "—",
                            "soon",
                            today,
                            "/Tasks")
                    ],
                    0)
            ];
            Deadlines =
            [
                new HomeDeadlineRow(
                    "Нет ближайших сроков",
                    "Медосмотры, обучение и спецоценка появятся здесь автоматически",
                    "—",
                    "soon")
            ];
        }

        Timeline = BuildTimeline();

        var summary = TrainingReportBuilder.SummarizeStatusRows(trainingRows);
        TrainingOverdue = summary.Overdue;

        OverdueTasks = allDeadlines.Count(i =>
            string.Equals(i.BadgeKind, "danger", StringComparison.OrdinalIgnoreCase));
        OpenTasks = allDeadlines.Count;
        DaysWithoutAccident = IncidentStats.DaysWithoutAccident;

        var plannedTraining = _trainingPlanning.LoadAll()
            .Count(r => string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase));

        MedSummary = HomeReportSummaryBuilder.FromMed(MedStats);
        TrainingSummary = HomeReportSummaryBuilder.FromTraining(trainingRows, today, plannedTraining);
        TrainingPlannedCount = plannedTraining;
        var plannedSoutIds = SoutWorkplaceNeedsBuilder.ResolvePlannedWorkplaceIds(
            _annualPlans.LoadAll(),
            organization,
            today.Year);
        SoutSummary = HomeReportSummaryBuilder.FromSout(workplaces, today, plannedSoutIds);
        RisksSummary = HomeReportSummaryBuilder.FromRisks(riskWorkplaces, today);

        StoreHomeCache(organization, today);
    }

    private static readonly object HomeCacheGate = new();
    private static string _homeCacheOrg = string.Empty;
    private static DateTime _homeCacheUtc = DateTime.MinValue;
    private static HomeCacheSnapshot? _homeCache;

    private bool TryApplyHomeCache(string organization, DateTime today)
    {
        lock (HomeCacheGate)
        {
            if (_homeCache is null
                || !string.Equals(_homeCacheOrg, organization, StringComparison.OrdinalIgnoreCase)
                || DateTime.UtcNow - _homeCacheUtc > TimeSpan.FromSeconds(90)
                || _homeCache.Day != today)
            {
                return false;
            }

            ApplyHomeCache(_homeCache);
            return true;
        }
    }

    private void StoreHomeCache(string organization, DateTime today)
    {
        lock (HomeCacheGate)
        {
            _homeCacheOrg = organization;
            _homeCacheUtc = DateTime.UtcNow;
            _homeCache = CaptureHomeCache(today);
        }
    }

    private void ApplyHomeCache(HomeCacheSnapshot cache)
    {
        StaffingItems = cache.StaffingItems;
        StaffingAlerts = cache.StaffingAlerts;
        StaffingGaps = cache.StaffingGaps;
        MedStats = cache.MedStats;
        DisplayMedStats = cache.MedStats;
        IncidentStats = cache.IncidentStats;
        Readiness = cache.Readiness;
        DeadlineSections = cache.DeadlineSections;
        Deadlines = cache.Deadlines;
        Timeline = cache.Timeline;
        TrainingOverdue = cache.TrainingOverdue;
        OverdueTasks = cache.OverdueTasks;
        OpenTasks = cache.OpenTasks;
        DaysWithoutAccident = cache.DaysWithoutAccident;
        MedSummary = cache.MedSummary;
        TrainingSummary = cache.TrainingSummary;
        TrainingPlannedCount = cache.TrainingPlannedCount;
        SoutSummary = cache.SoutSummary;
        RisksSummary = cache.RisksSummary;
    }

    private HomeCacheSnapshot CaptureHomeCache(DateTime today) =>
        new(
            today,
            StaffingItems,
            StaffingAlerts,
            StaffingGaps,
            MedStats,
            IncidentStats,
            Readiness,
            DeadlineSections,
            Deadlines,
            Timeline,
            TrainingOverdue,
            OverdueTasks,
            OpenTasks,
            DaysWithoutAccident,
            MedSummary,
            TrainingSummary,
            TrainingPlannedCount,
            SoutSummary,
            RisksSummary);

    private sealed record HomeCacheSnapshot(
        DateTime Day,
        IReadOnlyList<StaffingOnboardingItem> StaffingItems,
        IReadOnlyList<StaffingChangeEvent> StaffingAlerts,
        IReadOnlyDictionary<string, string> StaffingGaps,
        MedDashboardStats MedStats,
        IncidentHubStats IncidentStats,
        DirectionReadinessBuilder.DirectionReadiness Readiness,
        IReadOnlyList<HomeDeadlineSection> DeadlineSections,
        IReadOnlyList<HomeDeadlineRow> Deadlines,
        IReadOnlyList<HomeTimelineRow> Timeline,
        int TrainingOverdue,
        int OverdueTasks,
        int OpenTasks,
        int DaysWithoutAccident,
        HomeReportSummaryBuilder.ModuleSummary MedSummary,
        HomeReportSummaryBuilder.ModuleSummary TrainingSummary,
        int TrainingPlannedCount,
        HomeReportSummaryBuilder.ModuleSummary SoutSummary,
        HomeReportSummaryBuilder.ModuleSummary RisksSummary);

    private IReadOnlyList<TrainingStatusRow> BuildTrainingStatusRows(
        string organization,
        DateTime today,
        IReadOnlyDictionary<string, string> assignmentMap,
        IReadOnlyList<TrainingStandard> trainingStandards,
        IReadOnlyList<TrainingConductRecord> trainingConduct)
    {
        var employeeList = _orgEmployees.Load(organization)
            .Select(e => (organization, e.Name, e.Profession, e.Department))
            .ToList();
        if (employeeList.Count == 0)
        {
            employeeList = _employees.GetByProfession(organization)
                .SelectMany(pair => pair.Value.Select(name => (organization, name, pair.Key, "—")))
                .ToList();
        }

        return TrainingReportBuilder.BuildManualStatusReport(
            trainingStandards,
            organization,
            employeeList,
            today,
            trainingConduct,
            assignmentMap,
            _trainingKinds.Load());
    }

    private IReadOnlyList<HomeTimelineRow> BuildTimeline() =>
    [
        new("Печать журнала инструктажа", "Обучение", "сегодня"),
        new("Загрузка Excel из 1С", FileName ?? "workers.xlsx", "вчера"),
        new("Обновление карт СОУТ", "Спецоценка", "недавно")
    ];

    public sealed record HomeDeadlineRow(string Title, string Subtitle, string Badge, string BadgeKind);

    public sealed record HomeTimelineRow(string Title, string Subtitle, string Time);
}

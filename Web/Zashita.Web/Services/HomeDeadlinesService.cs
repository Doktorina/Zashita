using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Core.Risks;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

/// <summary>Сроки и задачи главной: медосмотры, обучение, СОУТ, риски.</summary>
public sealed class HomeDeadlinesService
{
    private readonly MedExamStore _medExams;
    private readonly WorkplaceStore _workplaces;
    private readonly RiskWorkplaceStore _riskWorkplaces;
    private readonly TrainingStandardsStore _trainingStandards;
    private readonly TrainingConductStore _trainingConduct;
    private readonly TrainingPersonStandardAssignmentStore _trainingAssignments;
    private readonly TrainingKindStore _trainingKinds;
    private readonly PpeEmployeesService _employees;
    private readonly OrganizationEmployeesService _orgEmployees;

    public HomeDeadlinesService(
        MedExamStore medExams,
        WorkplaceStore workplaces,
        RiskWorkplaceStore riskWorkplaces,
        TrainingStandardsStore trainingStandards,
        TrainingConductStore trainingConduct,
        TrainingPersonStandardAssignmentStore trainingAssignments,
        TrainingKindStore trainingKinds,
        PpeEmployeesService employees,
        OrganizationEmployeesService orgEmployees)
    {
        _medExams = medExams;
        _workplaces = workplaces;
        _riskWorkplaces = riskWorkplaces;
        _trainingStandards = trainingStandards;
        _trainingConduct = trainingConduct;
        _trainingAssignments = trainingAssignments;
        _trainingKinds = trainingKinds;
        _employees = employees;
        _orgEmployees = orgEmployees;
    }

    private readonly object _cacheSync = new();
    private string _cacheOrg = string.Empty;
    private DateTime _cacheDay = DateTime.MinValue;
    private DateTime _cacheUtc = DateTime.MinValue;
    private IReadOnlyList<HomeDeadlineItem>? _cacheItems;

    public IReadOnlyList<HomeDeadlineItem> Load(string organization, DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var org = (organization ?? string.Empty).Trim();
        lock (_cacheSync)
        {
            if (string.Equals(_cacheOrg, org, StringComparison.OrdinalIgnoreCase)
                && _cacheDay == today
                && DateTime.UtcNow - _cacheUtc < TimeSpan.FromSeconds(90)
                && _cacheItems is not null)
            {
                return _cacheItems;
            }
        }

        var trainingRows = BuildTrainingStatusRows(org, today);
        var items = new List<HomeDeadlineItem>();

        var staffNames = _orgEmployees.Load(org)
            .Select(e => (e.Name ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var examGroup in _medExams.LoadByOrganization(org)
                     .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeName))
                     .GroupBy(e =>
                     {
                         var name = e.EmployeeName.Trim();
                         var type = string.IsNullOrWhiteSpace(e.ExamType)
                             ? "Периодический"
                             : e.ExamType.Trim();
                         return name + "\u001e" + type;
                     }, StringComparer.OrdinalIgnoreCase))
        {
            // В хранилище медосмотров — строка на фактор; в сроках нужна одна задача на человека + вид.
            var sample = examGroup.First();
            var name = sample.EmployeeName.Trim();
            if (staffNames.Count > 0 && !staffNames.Contains(name))
            {
                continue;
            }

            var examType = string.IsNullOrWhiteSpace(sample.ExamType)
                ? "Периодический"
                : sample.ExamType.Trim();
            var due = examGroup.Min(e => MedDashboardBuilder.ResolveDueDate(e).Date);
            items.Add(Make(
                Section: "Медосмотры",
                SectionOrder: 1,
                Title: $"{name} — медосмотр",
                Subtitle: $"{examType} · срок {due:dd.MM.yyyy}",
                Due: due,
                Today: today,
                Href: "/Med/Schedule"));
        }

        foreach (var training in trainingRows.Where(NeedsTrainingDeadline))
        {
            var due = ResolveTrainingDue(training, today);
            var hasNext = !string.IsNullOrWhiteSpace(training.NextTrainingDate);
            var nextText = hasNext
                ? $"следующее {training.NextTrainingDate}"
                : (string.IsNullOrWhiteSpace(training.Status) ? "не проводилось" : training.Status);
            var item = Make(
                Section: "Обучение",
                SectionOrder: 2,
                Title: $"{training.Person} — обучение",
                Subtitle: $"{training.Program} · {nextText}",
                Due: due,
                Today: today,
                Href: "/Training/Planning");
            if (!hasNext && string.Equals(training.Traffic, "red", StringComparison.OrdinalIgnoreCase))
            {
                item = item with { Badge = "не проводилось", BadgeKind = "danger" };
            }

            items.Add(item);
        }

        foreach (var workplace in _workplaces.LoadByOrganization(org).Where(w => !w.IsGroup))
        {
            if (workplace.SoutAssessmentDate is not { } soutDate)
            {
                if (workplace.SoutAssessmentNotConducted || string.IsNullOrWhiteSpace(workplace.ConditionsClass))
                {
                    items.Add(Make(
                        Section: "СОУТ",
                        SectionOrder: 3,
                        Title: $"{DisplayWorkplace(workplace)} — спецоценка",
                        Subtitle: "не проведена",
                        Due: today.AddDays(-1),
                        Today: today,
                        Href: "/Sout/Status"));
                }

                continue;
            }

            var nextSout = soutDate.Date.AddYears(RiskAssessmentStatusBuilder.ValidityYears);
            items.Add(Make(
                Section: "СОУТ",
                SectionOrder: 3,
                Title: $"{DisplayWorkplace(workplace)} — спецоценка",
                Subtitle: $"проведена {soutDate:dd.MM.yyyy} · следующий срок {nextSout:dd.MM.yyyy}",
                Due: nextSout,
                Today: today,
                Href: "/Sout/Status"));
        }

        foreach (var need in RiskWorkplaceNeedsBuilder.Build(_riskWorkplaces.LoadByOrganization(org), org, today))
        {
            var due = need.ExpiresOn?.Date ?? today.AddDays(-1);
            var subtitle = need.NeedKind switch
            {
                RiskWorkplaceNeedsBuilder.NeedNotConducted => "оценка не проведена",
                RiskWorkplaceNeedsBuilder.NeedOverdue =>
                    need.ExpiresOn is { } e ? $"просрочена · срок был {e:dd.MM.yyyy}" : "просрочена",
                RiskWorkplaceNeedsBuilder.NeedExpiring =>
                    need.ExpiresOn is { } e ? $"истекает · срок {e:dd.MM.yyyy}" : "истекает",
                _ => need.Status
            };
            items.Add(Make(
                Section: "Риски",
                SectionOrder: 4,
                Title: $"{need.Profession} — оценка рисков",
                Subtitle: string.IsNullOrWhiteSpace(need.Department)
                    ? subtitle
                    : $"{need.Department} · {subtitle}",
                Due: due,
                Today: today,
                Href: string.IsNullOrWhiteSpace(need.Id)
                    ? "/Risks/AssessmentList"
                    : $"/Risks/Workplaces/Assessment?id={Uri.EscapeDataString(need.Id)}"));
        }

        var ordered = items
            .OrderBy(i => i.SectionOrder)
            .ThenBy(i => i.Due)
            .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        lock (_cacheSync)
        {
            _cacheOrg = org;
            _cacheDay = today;
            _cacheUtc = DateTime.UtcNow;
            _cacheItems = ordered;
        }

        return ordered;
    }

    public IReadOnlyList<HomeDeadlineSection> GroupBySection(IReadOnlyList<HomeDeadlineItem> items) =>
        items
            .GroupBy(i => i.Section)
            .OrderBy(g => g.First().SectionOrder)
            .Select(g =>
            {
                var sectionUrl = g.Key switch
                {
                    "Медосмотры" => "/Med/Schedule",
                    "Обучение" => "/Training/Planning",
                    "СОУТ" => "/Sout/Status",
                    "Риски" => "/Risks/AssessmentList",
                    _ => "/"
                };
                var ordered = g
                    .OrderBy(x => x.Due)
                    .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                return new HomeDeadlineSection(
                    g.Key,
                    sectionUrl,
                    ordered,
                    ordered.Count);
            })
            .ToList();

    /// <summary>Превью для главной: по разделам, не больше <paramref name="perSection"/> строк в каждом.</summary>
    public IReadOnlyList<HomeDeadlineSection> PreviewBySection(
        IReadOnlyList<HomeDeadlineItem> items,
        int perSection = 3) =>
        GroupBySection(items)
            .Select(s => s with
            {
                Items = s.Items.Take(Math.Max(1, perSection)).ToList()
            })
            .Where(s => s.Items.Count > 0)
            .ToList();

    private IReadOnlyList<TrainingStatusRow> BuildTrainingStatusRows(string organization, DateTime today)
    {
        var assignmentMap = _trainingAssignments.LoadAll()
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First().StandardId, StringComparer.OrdinalIgnoreCase);
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
            _trainingStandards.LoadAll(),
            organization,
            employeeList,
            today,
            _trainingConduct.LoadAll(),
            assignmentMap,
            _trainingKinds.Load());
    }

    private static bool NeedsTrainingDeadline(TrainingStatusRow row) =>
        !string.IsNullOrWhiteSpace(row.Program) &&
        (string.Equals(row.Traffic, "red", StringComparison.OrdinalIgnoreCase)
         || string.Equals(row.Traffic, "yellow", StringComparison.OrdinalIgnoreCase)
         || string.Equals(row.Traffic, "orange", StringComparison.OrdinalIgnoreCase));

    private static DateTime ResolveTrainingDue(TrainingStatusRow row, DateTime today)
    {
        if (!string.IsNullOrWhiteSpace(row.NextTrainingDate) &&
            DateTime.TryParseExact(
                row.NextTrainingDate.Trim(),
                "dd.MM.yyyy",
                System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
                System.Globalization.DateTimeStyles.None,
                out var next))
        {
            return next.Date;
        }

        return today.AddDays(-1);
    }

    private static string DisplayWorkplace(WorkplaceRecord w) =>
        string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession;

    private static HomeDeadlineItem Make(
        string Section,
        int SectionOrder,
        string Title,
        string Subtitle,
        DateTime Due,
        DateTime Today,
        string Href)
    {
        var days = (Due.Date - Today).Days;
        var (badge, kind) = days switch
        {
            < 0 => ($"просрочено {Math.Abs(days)} дн.", "danger"),
            0 => ("сегодня", "warn"),
            <= 30 => ($"через {days} дн.", "warn"),
            _ => ($"через {days} дн.", "soon")
        };
        return new HomeDeadlineItem(Section, SectionOrder, Title, Subtitle, badge, kind, Due.Date, Href);
    }
}

public sealed record HomeDeadlineItem(
    string Section,
    int SectionOrder,
    string Title,
    string Subtitle,
    string Badge,
    string BadgeKind,
    DateTime Due,
    string Href);

public sealed record HomeDeadlineSection(
    string Title,
    string SectionUrl,
    IReadOnlyList<HomeDeadlineItem> Items,
    int TotalCount = 0);

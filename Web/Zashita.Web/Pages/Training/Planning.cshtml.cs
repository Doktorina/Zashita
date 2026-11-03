using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Zashita.Core.Electrical;
using Zashita.Core.Employees;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class PlanningModel : PageModel
{
    private readonly TrainingPlanningStore _store;
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingConductStore _conduct;
    private readonly TrainingPersonStandardAssignmentStore _assignments;
    private readonly TrainingKindStore _kinds;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly TrainingApplicationEditor _applicationEditor;
    private readonly ElectricalKnowledgeCheckStore _electricalKnowledge;

    public PlanningModel(
        TrainingPlanningStore store,
        TrainingStandardsStore standards,
        TrainingConductStore conduct,
        TrainingPersonStandardAssignmentStore assignments,
        TrainingKindStore kinds,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        TrainingApplicationEditor applicationEditor,
        ElectricalKnowledgeCheckStore electricalKnowledge)
    {
        _store = store;
        _standards = standards;
        _conduct = conduct;
        _assignments = assignments;
        _kinds = kinds;
        _orgs = orgs;
        _employees = employees;
        _applicationEditor = applicationEditor;
        _electricalKnowledge = electricalKnowledge;
    }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Section { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TrainingKind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string[]? TrainingKinds { get; set; }

    /// <summary>overdue | notconducted | expiring | all (пусто = all).</summary>
    [BindProperty(SupportsGet = true)]
    public string? NeedFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Create { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DueMonth { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? PlanYear { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<TrainingStatusRow> NeedsRows { get; private set; } = [];
    public IReadOnlyList<PlanningNeedPersonGroup> NeedPersonGroups { get; private set; } = [];
    public int NeedsOverdueCount { get; private set; }
    public int NeedsNotConductedCount { get; private set; }
    public int NeedsExpiringCount { get; private set; }
    public int NeedsTrainedCount { get; private set; }
    public int NeedsPlannedCount { get; private set; }
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<string> Sections { get; private set; } = [];
    public IReadOnlyList<string> TrainingKindOptions { get; private set; } = [];
    public IReadOnlyList<TrainingNeedsCalendarKindGroup> TrainingKindGroups { get; private set; } = [];
    public IReadOnlyList<string> SelectedTrainingKinds { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        if (!string.IsNullOrWhiteSpace(EditId) || Create || !string.IsNullOrWhiteSpace(PrintId))
        {
            return RedirectToPage("/Training/Applications", new
            {
                editId = EditId,
                create = Create ? true : (bool?)null,
                printId = PrintId,
                q = Q
            });
        }

        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        Message = TempData["Message"] as string;
        LoadRows();
        LoadNeedsFromReport();
        return Page();
    }

    public IActionResult OnGetFromNeed(string? person, string? program, string? department, string? profession) =>
        RedirectToPage("/Training/Applications", new { handler = "FromNeed", person, program, department, profession });

    public IActionResult OnPostCreateFromNeeds(string[]? selectedNeeds)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ResolveSelectedTrainingKinds();
        LoadRows();
        LoadNeedsFromReport();

        if (selectedNeeds is null || selectedNeeds.Length == 0)
        {
            IsError = true;
            Message = "Выберите хотя бы одного сотрудника.";
            return Page();
        }

        var selectedGroups = NeedPersonGroups
            .Where(group => selectedNeeds.Contains(group.Key, StringComparer.Ordinal))
            .ToList();
        if (selectedGroups.Count == 0)
        {
            IsError = true;
            Message = "Не удалось определить выбранных сотрудников.";
            return Page();
        }

        var employees = _employees.LoadForTrainingReport(Organization);
        var selectedKind = SelectedTrainingKinds.Count == 1 ? SelectedTrainingKinds[0] : string.Empty;
        var mappedProgram = _applicationEditor.ResolveProgramForKind(selectedKind);
        var participants = selectedGroups
            .Select(group =>
            {
                var employee = TrainingApplicationEditor.FindEmployee(employees, group.Person, group.Department, group.Profession);
                var programs = group.Items
                    .Select(item => item.Program)
                    .Where(program => !string.IsNullOrWhiteSpace(program))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var programLabel = !string.IsNullOrWhiteSpace(mappedProgram)
                    ? mappedProgram
                    : programs.Count switch
                    {
                        0 => string.Empty,
                        1 => programs[0],
                        _ => string.Join(", ", programs)
                    };
                return TrainingApplicationEditor.BuildParticipantFromEmployee(
                    employee, group.Person, group.Profession, programLabel);
            })
            .ToList();

        var departments = selectedGroups
            .Select(group => group.Department)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allPrograms = selectedGroups
            .SelectMany(group => group.Items.Select(item => item.Program))
            .Where(program => !string.IsNullOrWhiteSpace(program))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var record = TrainingApplicationEditor.CreateBlankRecord(Organization);
        record.Participants = participants;
        record.Department = departments.Count == 1 ? departments[0] : string.Empty;
        record.TrainingKind = !string.IsNullOrWhiteSpace(selectedKind)
            ? selectedKind
            : allPrograms.Count == 1 ? allPrograms[0] : string.Empty;
        record.Content = !string.IsNullOrWhiteSpace(mappedProgram)
            ? $"Планирование: {mappedProgram}"
            : allPrograms.Count > 0
                ? $"Планирование: {string.Join(", ", allPrograms)}"
                : "Планирование обучения";

        _applicationEditor.Save(record);
        TempData["Message"] = "Заявка сформирована из выбранных сотрудников.";
        return RedirectToPage("/Training/Applications", new { editId = record.Id });
    }

    public IActionResult OnPostMarkConducted(
        string[]? selectedNeeds,
        string[]? conductId,
        string[]? organization,
        string[]? department,
        string[]? profession,
        string[]? person,
        string[]? program,
        string[]? protocolMovement,
        string[]? updStatus)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ResolveSelectedTrainingKinds();
        LoadRows();
        LoadNeedsFromReport();

        if (selectedNeeds is null || selectedNeeds.Length == 0)
        {
            IsError = true;
            Message = "Выберите хотя бы одного сотрудника.";
            return Page();
        }

        var selectedKeys = selectedNeeds.ToHashSet(StringComparer.Ordinal);
        var drafts = new List<TrainingConductRecord>();
        var count = person?.Length ?? 0;
        for (var i = 0; i < count; i++)
        {
            var personName = ValueAt(person, i);
            var programName = ValueAt(program, i);
            if (string.IsNullOrWhiteSpace(personName) || string.IsNullOrWhiteSpace(programName))
            {
                continue;
            }

            var rowKey = BuildNeedKey(
                personName,
                ValueAt(department, i),
                ValueAt(profession, i));
            if (!selectedKeys.Contains(rowKey))
            {
                continue;
            }

            var org = ValueAt(organization, i);
            if (string.IsNullOrWhiteSpace(org))
            {
                org = Organization;
            }

            var draft = ConductModel.BuildDraft(
                _conduct,
                org,
                personName,
                programName,
                ValueAt(department, i),
                ValueAt(profession, i),
                ValueAt(conductId, i),
                trainingDateText: null,
                nextTrainingDateText: null,
                certificateNumber: null,
                comment: null,
                protocolMovement: ValueAt(protocolMovement, i),
                updStatus: ValueAt(updStatus, i));

            if (drafts.Any(d => string.Equals(d.Id, draft.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            drafts.Add(draft);
        }

        if (drafts.Count == 0)
        {
            IsError = true;
            Message = "Не удалось открыть форму проведения — проверьте выбранных сотрудников.";
            return Page();
        }

        ConductModel.StoreBatch(TempData, drafts);
        return RedirectToPage("/Training/Conduct");
    }

    public RouteValueDictionary ListRoute(string? needFilter = null)
    {
        return new RouteValueDictionary
        {
            ["department"] = Department,
            ["section"] = Section,
            ["q"] = Q,
            ["needFilter"] = needFilter ?? NeedFilter,
            ["dueMonth"] = DueMonth,
            ["planYear"] = PlanYear
        };
    }

    public IActionResult OnPostApplyKinds(
        string? department,
        string? section,
        string? q,
        string? needFilter,
        int? dueMonth,
        int? planYear,
        List<string>? trainingKinds)
    {
        TrainingFilterSession.SavePlanningKinds(HttpContext, trainingKinds);
        return RedirectToPage(new
        {
            department,
            section,
            q,
            needFilter,
            dueMonth,
            planYear
        });
    }

    private void ResolveSelectedTrainingKinds()
    {
        var selected = TrainingKinds?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (selected.Count == 0 && !string.IsNullOrWhiteSpace(TrainingKind))
        {
            selected.Add(TrainingKind.Trim());
        }

        if (selected.Count > 0)
        {
            TrainingFilterSession.SavePlanningKinds(HttpContext, selected);
            SelectedTrainingKinds = selected;
            return;
        }

        SelectedTrainingKinds = TrainingFilterSession.LoadPlanningKinds(HttpContext);
    }

    private void LoadRows()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ResolveSelectedTrainingKinds();
        var all = _store.LoadAll()
            .Where(record => string.Equals(record.Organization, Organization, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(record.Organization))
            .ToList();

        var employees = _employees.LoadForTrainingReport(Organization);
        var kindNodes = _kinds.Load().Where(k => !k.IsGroup && !k.MarkedForDeletion).ToList();

        Departments = employees.Select(e => e.Department)
            .Concat(all.Select(r => r.Department))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Sections = TrainingKindCatalogDefaults.CollectUsedSections(kindNodes)
            .ToList();

        // Только справочник «Виды обучения» (+ ЭБ в группах фильтра ниже).
        TrainingKindOptions = kindNodes.Select(k => k.Name)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void LoadNeedsFromReport()
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

        var all = TrainingReportBuilder.BuildManualStatusReport(
            standards,
            Organization,
            employeeList,
            DateTime.Today,
            _conduct.LoadForReports(),
            assignmentMap,
            kinds).ToList();
        all.AddRange(ElectricalTrainingReportBridge.ToTrainingRows(
            _electricalKnowledge.Load(Organization),
            DateTime.Today,
            employeeList));
        all = TrainingReportBuilder.ApplyStaffDepartments(
            TrainingReportBuilder.UnifyPersonDepartments(all),
            employeeList);

        var asOf = DateTime.Today;
        var withProgram = all.Where(r => !string.IsNullOrWhiteSpace(r.Program)).ToList();
        NeedsOverdueCount = TrainingNeedsCalendarBuilder.CountPeople(
            withProgram, asOf, TrainingNeedsCalendarBuilder.FilterOverdue);
        NeedsNotConductedCount = TrainingNeedsCalendarBuilder.CountPeople(
            withProgram, asOf, TrainingNeedsCalendarBuilder.FilterNotConducted);
        NeedsExpiringCount = TrainingNeedsCalendarBuilder.CountPeople(
            withProgram, asOf, TrainingNeedsCalendarBuilder.FilterExpiring);
        NeedsTrainedCount = TrainingNeedsCalendarBuilder.CountPeople(
            withProgram, asOf, TrainingNeedsCalendarBuilder.FilterTrained);

        var plannedRecords = _store.LoadAll()
            .Where(record => string.Equals(record.Organization, Organization, StringComparison.OrdinalIgnoreCase)
                             || string.IsNullOrWhiteSpace(record.Organization))
            .ToList();
        NeedsPlannedCount = plannedRecords
            .SelectMany(record => record.Participants is { Count: > 0 }
                ? record.Participants
                : [new TrainingPlanningParticipant()])
            .Select(participant => participant.FullName?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0 && !string.Equals(name, "—", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var filter = string.IsNullOrWhiteSpace(NeedFilter)
            ? TrainingNeedsCalendarBuilder.FilterAll
            : NeedFilter.Trim();
        var planYear = PlanYear is >= 2000 and <= 2100 ? PlanYear.Value : DateTime.Today.Year;

        IEnumerable<TrainingStatusRow> filtered;
        if (string.Equals(filter, TrainingNeedsCalendarBuilder.FilterPlanned, StringComparison.OrdinalIgnoreCase))
        {
            filtered = BuildPlannedNeedRows(plannedRecords);
        }
        else
        {
            filtered = TrainingNeedsCalendarBuilder.FilterNeeds(withProgram, filter, asOf);
        }

        NeedsRows = filtered
            .Where(r => string.IsNullOrWhiteSpace(Department) ||
                        string.Equals(r.Department, Department, StringComparison.OrdinalIgnoreCase))
            .Where(r => SelectedTrainingKinds.Count == 0 ||
                        SelectedTrainingKinds.Any(kind =>
                            ElectricalKnowledgeCalculator.MatchesTrainingKindFilter(r.Program, kind)))
            .Where(r => DueMonth is not >= 1 or <= 12
                || TrainingNeedsCalendarBuilder.ResolveDueMonth(r, planYear, asOf) == DueMonth.Value)
            .Where(r => string.IsNullOrWhiteSpace(Q) ||
                        r.Person.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                        r.Program.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                        r.Department.Contains(Q, StringComparison.OrdinalIgnoreCase))
            .Take(300)
            .ToList();

        Departments = Departments
            .Concat(NeedsRows.Select(r => r.Department))
            .Where(v => !string.IsNullOrWhiteSpace(v) && v != "—")
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        TrainingKindGroups = TrainingNeedsCalendarBuilder.BuildFilterGroupsFromKinds(_kinds.Load());

        NeedPersonGroups = BuildNeedPersonGroups();
    }

    private static List<TrainingStatusRow> BuildPlannedNeedRows(IEnumerable<TrainingPlanningRecord> records)
    {
        var rows = new List<TrainingStatusRow>();
        foreach (var record in records)
        {
            var department = string.IsNullOrWhiteSpace(record.Department) ? "—" : record.Department.Trim();
            var program = !string.IsNullOrWhiteSpace(record.TrainingKind)
                ? record.TrainingKind.Trim()
                : (string.IsNullOrWhiteSpace(record.Content) ? "Заявка на обучение" : record.Content.Trim());
            var participants = record.Participants is { Count: > 0 }
                ? record.Participants
                : [new TrainingPlanningParticipant { FullName = "—", Position = "—", ProgramName = program }];

            foreach (var participant in participants)
            {
                var person = string.IsNullOrWhiteSpace(participant.FullName) ? "—" : participant.FullName.Trim();
                var profession = string.IsNullOrWhiteSpace(participant.Position) ? "—" : participant.Position.Trim();
                var itemProgram = string.IsNullOrWhiteSpace(participant.ProgramName)
                    ? program
                    : participant.ProgramName.Trim();
                rows.Add(new TrainingStatusRow(
                    record.Organization,
                    department,
                    profession,
                    person,
                    itemProgram,
                    "Запланировано",
                    "Запланировано",
                    "—",
                    record.DocumentDate.ToString("dd.MM.yyyy"),
                    record.ExecutionDate?.ToString("dd.MM.yyyy"),
                    record.Number ?? string.Empty,
                    "blue",
                    record.Id,
                    null,
                    null,
                    record.Content ?? string.Empty));
            }
        }

        return rows;
    }

    private List<PlanningNeedPersonGroup> BuildNeedPersonGroups() =>
        NeedsRows
            .GroupBy(row => new { row.Person, row.Department, row.Profession })
            .Select(group => new PlanningNeedPersonGroup(
                BuildNeedKey(group.Key.Person, group.Key.Department, group.Key.Profession),
                group.Key.Person,
                group.Key.Department,
                group.Key.Profession,
                group.ToList()))
            .OrderBy(group => group.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => group.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => group.Person, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string BuildNeedKey(string person, string department, string profession) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            string.Join('\u001e', person.Trim(), department.Trim(), profession.Trim())));

    private static string ValueAt(string[]? values, int index) =>
        values is not null && index < values.Length ? values[index]?.Trim() ?? string.Empty : string.Empty;

    private static DateTime? ResolveNextTrainingDate(
        DateTime trainingDate,
        string? periodicity,
        DateTime? storedNextDate)
    {
        var p = (periodicity ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (p.Length == 0)
        {
            return storedNextDate;
        }

        if (p is "1 раз" or "однократно"
            || p.Contains("единораз", StringComparison.Ordinal)
            || p.Contains("при приеме", StringComparison.Ordinal)
            || p.Contains("при приёме", StringComparison.Ordinal)
            || p.Contains("при поступлен", StringComparison.Ordinal)
            || p.Contains("по необходимости", StringComparison.Ordinal))
        {
            return null;
        }

        var months = TrainingProgramCatalog.PeriodMonthsFromPeriodicity(periodicity);
        return months > 0 ? trainingDate.Date.AddMonths(months) : storedNextDate;
    }
}

public sealed record PlanningNeedPersonGroup(
    string Key,
    string Person,
    string Department,
    string Profession,
    IReadOnlyList<TrainingStatusRow> Items);

public sealed class TrainingKindSectionFilterModel
{
    public string CheckboxName { get; init; } = "trainingKinds";
    public IReadOnlyList<TrainingNeedsCalendarKindGroup> Groups { get; init; } = [];
    public IReadOnlyCollection<string> Selected { get; init; } = [];
    public bool TreatEmptyAsAll { get; init; }

    public bool IsSelected(string program)
    {
        if (TreatEmptyAsAll && Selected.Count == 0)
        {
            return true;
        }

        return Selected.Contains(program, StringComparer.OrdinalIgnoreCase);
    }
}

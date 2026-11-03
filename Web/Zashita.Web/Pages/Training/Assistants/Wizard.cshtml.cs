using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training.Assistants;

[RequireLogin]
public class WizardModel : PageModel
{
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingConductStore _conduct;
    private readonly TrainingPlanningStore _planning;
    private readonly TrainingKindStore _kinds;
    private readonly OrganizationStore _orgs;
    private readonly PpeEmployeesService _employees;

    public WizardModel(
        TrainingStandardsStore standards,
        TrainingConductStore conduct,
        TrainingPlanningStore planning,
        TrainingKindStore kinds,
        OrganizationStore orgs,
        PpeEmployeesService employees)
    {
        _standards = standards;
        _conduct = conduct;
        _planning = planning;
        _kinds = kinds;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "plan";

    [BindProperty(SupportsGet = true, Name = "step")]
    public int StepNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public DateTime? AnalyticsDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AllDepartments { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public int SelectedIndex { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TrainingKindFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SavedPlanningId { get; set; }

    public TrainingAssistantWizardKind WizardKind { get; private set; } = TrainingAssistantWizardKind.Plan;
    public string Organization { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Hint { get; private set; } = string.Empty;
    public List<TrainingAssistantSummaryRow> SummaryRows { get; private set; } = [];
    public IReadOnlyList<string> TrainingKindOptions { get; private set; } = [];
    public List<TrainingAssistantEmployeeRow> EmployeeRows { get; private set; } = [];
    public List<TrainingAssistantResultDocument> ResultDocuments { get; private set; } = [];
    public TrainingAssistantSummaryRow? SelectedSummary { get; private set; }
    public DateTime EffectiveAnalyticsDate { get; private set; }

    public IActionResult OnGet()
    {
        LoadCommon();
        if (StepNumber is < 1 or > 3)
        {
            StepNumber = 1;
        }

        if (StepNumber >= 2 && SummaryRows.Count == 0)
        {
            return RedirectToPage(new { Kind, step = 1 });
        }

        if (StepNumber >= 2)
        {
            SelectedSummary = SummaryRows.ElementAtOrDefault(SelectedIndex) ?? SummaryRows.First();
            EmployeeRows = TrainingInstructionAssistantBuilder.BuildEmployeeRows(
                _standards.LoadAll(),
                SelectedSummary.Organization,
                SelectedSummary.TrainingKind,
                _employees.GetByProfession(SelectedSummary.Organization),
                EffectiveAnalyticsDate,
                WizardKind);

            if (!string.IsNullOrWhiteSpace(Q))
            {
                var query = Q.Trim();
                EmployeeRows = EmployeeRows
                    .Where(row =>
                        row.Person.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        row.Profession.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        row.Department.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        if (StepNumber == 3 && SelectedSummary is not null)
        {
            var employees = TrainingInstructionAssistantBuilder.BuildEmployeeRows(
                _standards.LoadAll(),
                SelectedSummary.Organization,
                SelectedSummary.TrainingKind,
                _employees.GetByProfession(SelectedSummary.Organization),
                EffectiveAnalyticsDate,
                WizardKind);

            ResultDocuments = TrainingInstructionAssistantBuilder.BuildResultDocuments(
                WizardKind,
                SelectedSummary.Organization,
                SelectedSummary.TrainingKind,
                employees,
                Organization,
                EffectiveAnalyticsDate);
        }

        Hint = StepNumber switch
        {
            2 when SelectedSummary is not null => TrainingAssistantWizardLabels.Step2Hint(WizardKind, SelectedSummary.Organization),
            3 => TrainingAssistantWizardLabels.Step3Hint(WizardKind),
            _ => TrainingAssistantWizardLabels.Step1Hint(WizardKind)
        };

        return Page();
    }

    public IActionResult OnPostNext(int selectedIndex, DateTime? analyticsDate, bool allDepartments = true)
    {
        Kind = Request.Form["kind"].ToString();
        if (string.IsNullOrWhiteSpace(Kind))
        {
            Kind = "plan";
        }

        TrainingKindFilter = Request.Form["trainingKindFilter"].ToString();

        return RedirectToPage(new
        {
            Kind,
            step = 2,
            selectedIndex,
            analyticsDate = (analyticsDate ?? DateTime.Today).ToString("yyyy-MM-dd"),
            allDepartments,
            trainingKindFilter = TrainingKindFilter
        });
    }

    public IActionResult OnPostFinish(int selectedIndex, DateTime? analyticsDate, bool allDepartments = true)
    {
        Kind = Request.Form["kind"].ToString();
        if (string.IsNullOrWhiteSpace(Kind))
        {
            Kind = "plan";
        }

        TrainingKindFilter = Request.Form["trainingKindFilter"].ToString();
        LoadCommon();
        SelectedIndex = selectedIndex;
        AnalyticsDate = analyticsDate;
        AllDepartments = allDepartments;
        SelectedSummary = SummaryRows.ElementAtOrDefault(selectedIndex) ?? SummaryRows.FirstOrDefault();
        if (SelectedSummary is null)
        {
            return RedirectToPage(new { Kind, step = 1, trainingKindFilter = TrainingKindFilter });
        }

        var employees = TrainingInstructionAssistantBuilder.BuildEmployeeRows(
            _standards.LoadAll(),
            SelectedSummary.Organization,
            SelectedSummary.TrainingKind,
            _employees.GetByProfession(SelectedSummary.Organization),
            EffectiveAnalyticsDate,
            WizardKind);

        PersistDocuments(SelectedSummary, employees);

        return RedirectToPage(new
        {
            Kind,
            step = 3,
            selectedIndex,
            analyticsDate = EffectiveAnalyticsDate.ToString("yyyy-MM-dd"),
            allDepartments,
            trainingKindFilter = TrainingKindFilter,
            savedPlanningId = SavedPlanningId
        });
    }

    private void LoadCommon()
    {
        WizardKind = TrainingAssistantWizardLabels.ParseKind(Kind);
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        EffectiveAnalyticsDate = AnalyticsDate?.Date ?? DateTime.Today;
        Title = TrainingAssistantWizardLabels.Title(WizardKind);
        var kinds = _kinds.Load();
        SummaryRows = TrainingInstructionAssistantBuilder.BuildSummaryRows(
            _standards.LoadAll(),
            Organization,
            _employees.GetByProfession(Organization),
            WizardKind,
            kinds,
            TrainingKindFilter);

        // Опции вида — по каталогу + фактические строки (без фильтра вида, чтобы список выбора был полным).
        var allForOptions = TrainingInstructionAssistantBuilder.BuildSummaryRows(
            _standards.LoadAll(),
            Organization,
            _employees.GetByProfession(Organization),
            WizardKind,
            kinds,
            trainingKindFilter: null);
        TrainingKindOptions = TrainingInstructionAssistantBuilder.ResolveKindOptions(WizardKind, kinds, allForOptions);

        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = Title;
        ViewData["Subtitle"] = "Помощники по обучению";
    }

    private void PersistDocuments(TrainingAssistantSummaryRow summary, IReadOnlyList<TrainingAssistantEmployeeRow> employees)
    {
        if (WizardKind is TrainingAssistantWizardKind.Plan or TrainingAssistantWizardKind.TrainPlan)
        {
            var isTraining = WizardKind == TrainingAssistantWizardKind.TrainPlan;
            var assistantName = TrainingAssistantWizardLabels.Title(WizardKind);
            var record = new TrainingPlanningRecord
            {
                DocumentDate = EffectiveAnalyticsDate,
                Number = _planning.AllocateNextNumber("ПЛ-"),
                Organization = summary.Organization,
                Department = employees.FirstOrDefault()?.Department ?? string.Empty,
                Section = "Охрана труда",
                TrainingKind = summary.TrainingKind,
                Content = isTraining
                    ? $"Планирование обучения ({summary.HeadCount} чел.) #Создан помощником «{assistantName}»"
                    : $"Планирование инструктажей ({summary.HeadCount} чел.) #Создан помощником «{assistantName}»",
                ExecutionDate = employees.FirstOrDefault()?.PlanningDate,
                ContactPersonName = Organization,
                Participants = employees.Select(employee => new TrainingPlanningParticipant
                {
                    FullName = employee.Person,
                    Position = employee.Profession,
                    ProgramName = summary.TrainingKind,
                    TrainingDirection = "Охрана труда"
                }).ToList()
            };
            _planning.Upsert(record);
            SavedPlanningId = record.Id;
            return;
        }

        foreach (var employee in employees)
        {
            _conduct.Upsert(new TrainingConductRecord
            {
                Organization = summary.Organization,
                Department = employee.Department,
                Profession = employee.Profession,
                Person = employee.Person,
                Program = summary.TrainingKind,
                TrainingType = "Инструктаж",
                Result = "Сдал",
                TrainingDate = employee.ConductDate ?? EffectiveAnalyticsDate,
                NextTrainingDate = employee.NextTrainingDate,
                Comment = WizardKind == TrainingAssistantWizardKind.Direct
                    ? "Создано: проведение без планирования"
                    : "Создано: помощник проведения инструктажа"
            });
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Core.Training.Journals;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class JournalModel : PageModel
{
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingJournalStore _journalStore;
    private readonly TrainingJournalHistoryStore _history;
    private readonly TrainingPrintHistoryStore _printHistory;
    private readonly TrainingJournalEntryBuilder _entryBuilder;
    private readonly TrainingJournalExportService _export;
    private readonly TrainingConductStore _conduct;
    private readonly OrganizationStore _orgs;
    private readonly PpeEmployeesService _employees;
    private readonly OrganizationEmployeesStore _orgEmployees;

    public JournalModel(
        TrainingStandardsStore standards,
        TrainingJournalStore journalStore,
        TrainingJournalHistoryStore history,
        TrainingPrintHistoryStore printHistory,
        TrainingJournalEntryBuilder entryBuilder,
        TrainingJournalExportService export,
        TrainingConductStore conduct,
        OrganizationStore orgs,
        PpeEmployeesService employees,
        OrganizationEmployeesStore orgEmployees)
    {
        _standards = standards;
        _journalStore = journalStore;
        _history = history;
        _printHistory = printHistory;
        _entryBuilder = entryBuilder;
        _export = export;
        _conduct = conduct;
        _orgs = orgs;
        _employees = employees;
        _orgEmployees = orgEmployees;
    }

    [BindProperty(SupportsGet = true)]
    public string? Title { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime DateFrom { get; set; } = DateTime.Today.AddMonths(-1);

    [BindProperty(SupportsGet = true)]
    public DateTime DateTo { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public DateTime? TitleStarted { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? TitleEnded { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Organization { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Workplace { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool StrictDepartment { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool ShowTitlePage { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public bool PadEmptyRowsToPageEnd { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? BriefingType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DefaultInstructor { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? LocalActName { get; set; }

    [BindProperty(SupportsGet = true)]
    public int StartRowNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int EmptyRowCount { get; set; }

    [BindProperty]
    public List<TrainingJournalEntry> Entries { get; set; } = [];

    public TrainingJournalDefinition? Definition { get; private set; }
    public IReadOnlyList<TrainingJournalColumn> Columns { get; private set; } = [];
    public IReadOnlyList<string> BriefingTypeOptions { get; private set; } = TrainingBriefingTypes.Options;
    public IReadOnlyList<string> InstructorSuggestions { get; private set; } = [];
    public IReadOnlyList<string> LocalActSuggestions { get; private set; } = [];
    public IReadOnlyList<string> DepartmentOptions { get; private set; } = [];
    public IReadOnlyList<TrainingJournalHistoryEntry> VersionHistory { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public string SessionOrganization { get; private set; } = string.Empty;
    public bool ReasonRequired => TrainingBriefingTypes.RequiresReason(BriefingType);

    public IActionResult OnGet()
    {
        SessionOrganization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;

        if (TempData["Message"] is string tempMessage)
        {
            Message = tempMessage;
            IsError = TempData["IsError"] is true;
        }

        if (string.IsNullOrWhiteSpace(Title) ||
            !TrainingJournalRegistry.TryGetByTitle(Title, out var definition) ||
            definition is null)
        {
            ViewData["Title"] = "Журнал";
            ViewData["Subtitle"] = "Не удалось определить тип журнала.";
            Message = "Укажите параметр title с названием журнала из каталога отчётов.";
            IsError = true;
            return Page();
        }

        Definition = definition;
        Columns = TrainingJournalColumns.GetColumns(definition.Kind);
        BriefingTypeOptions = TrainingBriefingTypes.OptionsFor(definition.Kind);
        ViewData["Title"] = definition.Title;
        ViewData["Subtitle"] = definition.Description;

        var saved = _journalStore.Load(definition.Kind);
        if (saved.Params is not null)
        {
            DateFrom = saved.Params.DateFrom;
            DateTo = saved.Params.DateTo;
            TitleStarted = saved.Params.TitleStarted ?? saved.Params.DateFrom;
            TitleEnded = saved.Params.TitleEnded ?? saved.Params.DateTo;
            Department = saved.Params.Department;
            Workplace = saved.Params.Workplace;
            StrictDepartment = saved.Params.StrictDepartmentFilter;
            ShowTitlePage = saved.Params.ShowTitlePage;
            PadEmptyRowsToPageEnd = saved.Params.PadEmptyRowsToPageEnd;
            BriefingType = TrainingBriefingTypes.Normalize(saved.Params.BriefingType);
            DefaultInstructor = saved.Params.DefaultInstructor;
            LocalActName = saved.Params.LocalActName;
            StartRowNumber = Math.Max(1, saved.Params.StartRowNumber);
            EmptyRowCount = Math.Max(0, saved.Params.EmptyRowCount);
        }
        else
        {
            TitleStarted ??= DateFrom;
            TitleEnded ??= DateTo;
        }

        if (Entries.Count == 0 && saved.Entries.Count > 0)
        {
            Entries = saved.Entries;
        }

        Organization = SessionOrganization;
        LoadSuggestions(saved.Entries);
        VersionHistory = _history.Get(definition.Kind);
        return Page();
    }

    public IActionResult OnPostGenerate()
    {
        if (!TryResolveDefinition(out var definition))
        {
            return RedirectToPage(new { title = Title });
        }

        BindFlagsFromForm();
        SessionOrganization = _orgs.GetSelectedOrganization(HttpContext);
        Organization = SessionOrganization;
        BriefingType = TrainingBriefingTypes.Normalize(BriefingType);
        StartRowNumber = Math.Max(1, StartRowNumber);
        EmptyRowCount = Math.Clamp(EmptyRowCount, 0, 500);
        NormalizeTitleDates();

        var standards = _standards.LoadAll();
        var conduct = _conduct.LoadAll();
        var parameters = BuildParams();
        var existing = _journalStore.Load(definition.Kind).Entries;
        Entries = _entryBuilder.Build(
            standards, parameters, definition, SessionOrganization, conduct, existing);
        var savedEmpty = EmptyRowCount;
        if (savedEmpty > 0)
        {
            EmptyRowCount = 0;
            parameters = BuildParams();
        }

        _journalStore.Save(definition.Kind, parameters, Entries);
        _history.Append(
            definition.Kind,
            parameters,
            Entries,
            savedEmpty > 0
                ? $"Дописано пустых строк: {savedEmpty}"
                : PadEmptyRowsToPageEnd
                    ? "Сформирован (с пустыми строками до конца страницы)"
                    : "Сформирован");
        RecordJournalPrint(definition, parameters, Entries, "Сформирован");
        TempData["Message"] = $"Сформировано строк: {Entries.Count}.";
        return RedirectToPage(new { title = definition.Title });
    }

    public IActionResult OnPostSave()
    {
        if (!TryResolveDefinition(out var definition))
        {
            return RedirectToPage(new { title = Title });
        }

        PersistCurrent(definition, "Сохранён");
        TempData["Message"] = "Записи сохранены.";
        return RedirectToPage(new { title = definition.Title });
    }

    public IActionResult OnPostSaveAndPrint()
    {
        if (!TryResolveDefinition(out var definition))
        {
            return RedirectToPage(new { title = Title });
        }

        PersistCurrent(definition, "Сохранён перед предпросмотром");
        return RedirectToPage(
            "/Training/Journal/Preview",
            new { kind = definition.Kind.ToString(), showTitlePage = ShowTitlePage, autoPrint = false });
    }

    public IActionResult OnPostSaveAndExport()
    {
        if (!TryResolveDefinition(out var definition))
        {
            return RedirectToPage(new { title = Title });
        }

        PersistCurrent(definition, "Сохранён перед экспортом");
        return OnGetExport(definition.Title);
    }

    public IActionResult OnPostRestore(int version)
    {
        if (!TryResolveDefinition(out var definition))
        {
            return RedirectToPage(new { title = Title });
        }

        SessionOrganization = _orgs.GetSelectedOrganization(HttpContext);
        if (!_history.TryRestore(definition.Kind, version, out var snapshot) || snapshot is null)
        {
            TempData["Message"] = $"Версия {version} не найдена.";
            TempData["IsError"] = true;
            return RedirectToPage(new { title = definition.Title });
        }

        _journalStore.Save(definition.Kind, snapshot.Params, snapshot.Entries, snapshot.PrintOverrides);
        TempData["Message"] = $"Открыта версия {version}.";
        return RedirectToPage(new { title = definition.Title });
    }

    public IActionResult OnGetExport(string? title)
    {
        Title = title ?? Title;
        if (!TryResolveDefinition(out var definition))
        {
            return RedirectToPage(new { title = Title });
        }

        SessionOrganization = _orgs.GetSelectedOrganization(HttpContext);
        Organization = SessionOrganization;
        var saved = _journalStore.Load(definition.Kind);
        var entries = saved.Entries;
        if (entries.Count == 0)
        {
            TempData["Message"] = "Нет записей для экспорта. Сначала сформируйте журнал.";
            TempData["IsError"] = true;
            return RedirectToPage(new { title = definition.Title });
        }

        var parameters = saved.Params ?? BuildParams();
        try
        {
            var (content, fileName, contentType) = _export.Export(definition, entries, parameters);
            var contentCount = entries.Count(e => !string.IsNullOrWhiteSpace(e.EmployeeName));
            _printHistory.Append(new TrainingPrintHistoryRecord
            {
                PrintedAt = DateTime.Now,
                Kind = definition.Kind,
                Title = definition.Title,
                Organization = parameters.Organization ?? SessionOrganization,
                BriefingType = parameters.BriefingType,
                DateFrom = parameters.DateFrom,
                DateTo = parameters.DateTo,
                EntryCount = contentCount,
                Summary = $"Экспорт · {fileName}"
            });

            return File(content, contentType, fileName);
        }
        catch (Exception ex)
        {
            TempData["Message"] = ex.Message;
            TempData["IsError"] = true;
            return RedirectToPage(new { title = definition.Title });
        }
    }

    private void PersistCurrent(TrainingJournalDefinition definition, string action)
    {
        BindFlagsFromForm();
        SessionOrganization = _orgs.GetSelectedOrganization(HttpContext);
        Organization = SessionOrganization;
        BriefingType = TrainingBriefingTypes.Normalize(BriefingType);
        StartRowNumber = Math.Max(1, StartRowNumber);
        EmptyRowCount = Math.Clamp(EmptyRowCount, 0, 500);
        NormalizeTitleDates();

        var parameters = BuildParams();
        _journalStore.Save(definition.Kind, parameters, Entries);
        _history.Append(definition.Kind, parameters, Entries, action);
        RecordJournalPrint(definition, parameters, Entries, action);
    }

    private void BindFlagsFromForm()
    {
        StrictDepartment = Request.Form.ContainsKey(nameof(StrictDepartment));
        ShowTitlePage = Request.Form.ContainsKey(nameof(ShowTitlePage));
        PadEmptyRowsToPageEnd = Request.Form.ContainsKey(nameof(PadEmptyRowsToPageEnd));
    }

    private void NormalizeTitleDates()
    {
        TitleStarted ??= DateFrom.Date;
        TitleEnded ??= DateTo.Date;
        if (TitleEnded < TitleStarted)
        {
            (TitleStarted, TitleEnded) = (TitleEnded, TitleStarted);
        }
    }

    private void RecordJournalPrint(
        TrainingJournalDefinition definition,
        TrainingJournalParams parameters,
        IReadOnlyList<TrainingJournalEntry> entries,
        string action)
    {
        var contentCount = entries.Count(e => !string.IsNullOrWhiteSpace(e.EmployeeName));
        _printHistory.Append(new TrainingPrintHistoryRecord
        {
            PrintedAt = DateTime.Now,
            Kind = definition.Kind,
            Title = definition.Title,
            Organization = parameters.Organization ?? SessionOrganization,
            BriefingType = parameters.BriefingType,
            DateFrom = parameters.DateFrom,
            DateTo = parameters.DateTo,
            EntryCount = contentCount,
            Summary = $"{action} · строк: {contentCount}"
        });
    }

    private bool TryResolveDefinition(out TrainingJournalDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(Title) &&
            TrainingJournalRegistry.TryGetByTitle(Title, out var resolved) &&
            resolved is not null)
        {
            definition = resolved;
            return true;
        }

        definition = null!;
        Message = "Журнал не найден.";
        IsError = true;
        return false;
    }

    private TrainingJournalParams BuildParams() =>
        new(
            DateFrom.Date,
            DateTo.Date,
            string.IsNullOrWhiteSpace(Organization) ? SessionOrganization : Organization.Trim(),
            string.IsNullOrWhiteSpace(Department) ? null : Department.Trim(),
            string.IsNullOrWhiteSpace(Workplace) ? null : Workplace.Trim(),
            StrictDepartment,
            ShowTitlePage,
            string.IsNullOrWhiteSpace(BriefingType) ? null : TrainingBriefingTypes.Normalize(BriefingType),
            string.IsNullOrWhiteSpace(DefaultInstructor) ? null : DefaultInstructor.Trim(),
            string.IsNullOrWhiteSpace(LocalActName) ? null : LocalActName.Trim(),
            Math.Max(1, StartRowNumber),
            Math.Clamp(EmptyRowCount, 0, 500),
            TitleStarted?.Date ?? DateFrom.Date,
            TitleEnded?.Date ?? DateTo.Date,
            PadEmptyRowsToPageEnd);

    private void LoadSuggestions(IReadOnlyList<TrainingJournalEntry> entries)
    {
        var fromEmployees = _employees.GetByProfession(SessionOrganization)
            .SelectMany(p => p.Value)
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => x.Length > 0);

        var fromOrgEmployees = _orgEmployees.Get(SessionOrganization)
            .Select(e => e.Name?.Trim() ?? string.Empty)
            .Where(x => x.Length > 0);

        InstructorSuggestions = entries
            .Select(e => e.InstructorName?.Trim() ?? string.Empty)
            .Where(x => x.Length > 0)
            .Concat(fromEmployees)
            .Concat(fromOrgEmployees)
            .Concat(string.IsNullOrWhiteSpace(DefaultInstructor) ? [] : [DefaultInstructor.Trim()])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        LocalActSuggestions = entries
            .Select(e => string.IsNullOrWhiteSpace(e.LocalActName) ? e.Workplace : e.LocalActName)
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => x.Length > 0)
            .Concat(
            [
                "Инструкция по охране труда",
                "Инструкция по ОТ на рабочем месте",
                "Программа первичного инструктажа"
            ])
            .Concat(string.IsNullOrWhiteSpace(LocalActName) ? [] : [LocalActName.Trim()])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        DepartmentOptions = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_orgEmployees, SessionOrganization),
            entries.Select(e => e.Department),
            string.IsNullOrWhiteSpace(Department) ? null : [Department]);
    }
}

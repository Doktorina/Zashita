using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Electrical;
using Zashita.Core.Employees;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class StatusModel : PageModel
{
    private readonly TrainingStandardsStore _standards;
    private readonly TrainingConductStore _conduct;
    private readonly TrainingPersonStandardAssignmentStore _assignments;
    private readonly TrainingKindStore _kinds;
    private readonly TrainingPlanningStore _planning;
    private readonly TrainingMappingStore _mapping;
    private readonly TrainingScheduleImportService _scheduleImport;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly ElectricalKnowledgeCheckStore _electricalKnowledge;

    public StatusModel(
        TrainingStandardsStore standards,
        TrainingConductStore conduct,
        TrainingPersonStandardAssignmentStore assignments,
        TrainingKindStore kinds,
        TrainingPlanningStore planning,
        TrainingMappingStore mapping,
        TrainingScheduleImportService scheduleImport,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        ElectricalKnowledgeCheckStore electricalKnowledge)
    {
        _standards = standards;
        _conduct = conduct;
        _assignments = assignments;
        _kinds = kinds;
        _planning = planning;
        _mapping = mapping;
        _scheduleImport = scheduleImport;
        _orgs = orgs;
        _employees = employees;
        _electricalKnowledge = electricalKnowledge;
    }

    private static readonly object RowsCacheGate = new();
    private static string _rowsCacheKey = string.Empty;
    private static DateTime _rowsCacheUtc = DateTime.MinValue;
    private static List<TrainingStatusRow>? _rowsCache;
    private static DateTime _applicationCommentSyncUtc = DateTime.MinValue;
    private static DateTime _repairOrgsUtc = DateTime.MinValue;
    private static readonly TimeSpan HeavyMaintenanceInterval = TimeSpan.FromMinutes(30);

    private static void InvalidateRowsCache()
    {
        lock (RowsCacheGate)
        {
            _rowsCache = null;
            _rowsCacheKey = string.Empty;
            _rowsCacheUtc = DateTime.MinValue;
        }
    }

    /// <summary>Сброс кэша отчёта после изменения стандартов / импорта.</summary>
    public static void InvalidateReportCache() => InvalidateRowsCache();

    /// <summary>
    /// Заявки на обучение → колонка «заметки» в отчёте («Заявка N от dd.MM.yyyy»).
    /// Не чаще раза в 30 мин — иначе каждый заход в отчёт переписывает весь conduct.json.
    /// </summary>
    private void SyncApplicationCommentsIntoConduct(bool force = false)
    {
        if (!force && DateTime.UtcNow - _applicationCommentSyncUtc < HeavyMaintenanceInterval)
        {
            return;
        }

        _applicationCommentSyncUtc = DateTime.UtcNow;
        var apps = _planning.LoadAll();
        if (apps.Count == 0)
        {
            return;
        }

        string ResolveProgram(string? kind) =>
            TrainingKindProgramMapping.ResolveProgram(_mapping.Load(TrainingMappingKind.WorkTypes), kind)
            ?? string.Empty;

        var list = _conduct.LoadAll();
        var changed = TrainingApplicationCommentSync.EnsureFromApplications(apps, list, ResolveProgram);
        if (changed > 0)
        {
            _conduct.SaveAll(list);
            InvalidateRowsCache();
        }
    }

    [BindProperty]
    public DateTime ReportDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TrainingType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TrainingKind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Profession { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PersonQ { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ProtocolMovement { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UpdStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedOrganization { get; set; }

    /// <summary>В HTML сразу все строки программ (тяжёлая страница). По умолчанию — ленивая подгрузка.</summary>
    [BindProperty(SupportsGet = true)]
    public bool ExpandPrograms { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> OrganizationOptions { get; private set; } = [];
    public IReadOnlyList<TrainingStatusRow> Rows { get; private set; } = [];
    public IReadOnlyList<TrainingStatusTreeLine> TreeLines { get; private set; } = [];
    /// <summary>Рендерить строки программ в первой отдаче (поиск / фильтр / expandPrograms).</summary>
    public bool IncludeProgramRows { get; private set; }
    public int PeopleCount { get; private set; }
    public IReadOnlyList<string> DepartmentOptions { get; private set; } = [];
    public IReadOnlyList<string> TrainingTypeOptions { get; private set; } = [];
    public IReadOnlyList<string> TrainingKindOptions { get; private set; } = [];
    public IReadOnlyList<string> ProfessionOptions { get; private set; } = [];
    public IReadOnlyList<string> ProtocolMovementOptions { get; } = ["готов", "на подписи", "—"];
    public IReadOnlyList<string> UpdStatusOptions { get; } = ["УПД есть", "УПД нет", "—"];
    public IReadOnlyList<string> PeriodicityOptions { get; } = TrainingProgramCatalog.PeriodicityOptions;
    public IReadOnlyList<TrainingStandard> StandardOptions { get; private set; } = [];
    /// <summary>Стандарты по юрлицу — один раз на страницу, без StandardsForOrganization в каждой строке дерева.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TrainingStandard>> StandardsByOrganization { get; private set; } =
        new Dictionary<string, IReadOnlyList<TrainingStandard>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<TrainingStandard> StandardsForOrg(string? organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return StandardOptions;
        }

        if (StandardsByOrganization.TryGetValue(org, out var exact))
        {
            return exact;
        }

        foreach (var (key, list) in StandardsByOrganization)
        {
            if (OrganizationMatcher.Matches(org, key))
            {
                return list;
            }
        }

        return TrainingReportBuilder.StandardsForOrganization(StandardOptions, org);
    }
    public Dictionary<string, string> HrStatusByName { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime?> DismissalDateByEmployee { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> TransferArchiveKeys { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string? Message { get; private set; }
    public bool IsArchive { get; private set; }

    protected virtual bool ArchiveOnly => false;

    public void OnGet()
    {
        IsArchive = ArchiveOnly;
        LoadReport();
    }

    /// <summary>Фрагмент строк программ одного сотрудника (ленивая подгрузка дерева).</summary>
    public IActionResult OnGetPersonPrograms(string? personId, int startIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(personId))
        {
            return Content(string.Empty, "text/html; charset=utf-8");
        }

        IsArchive = ArchiveOnly || IsArchiveRequest();
        // Не ставим ExpandPrograms — иначе пересоберётся весь отчёт со всеми программами и клик «зависнет».
        ExpandPrograms = false;
        LoadReport();

        var id = personId.Trim();
        var personLine = TreeLines.FirstOrDefault(l =>
            l.IsPerson && string.Equals(l.Id, id, StringComparison.Ordinal));
        if (personLine is null)
        {
            return Content(string.Empty, "text/html; charset=utf-8");
        }

        var personRows = Rows
            .Where(r =>
                string.Equals(r.Organization, personLine.Organization, StringComparison.OrdinalIgnoreCase)
                && string.Equals((r.Person ?? string.Empty).Trim(), personLine.Label, StringComparison.OrdinalIgnoreCase)
                && (IsArchive
                    ? string.Equals(
                        ArchiveProfessionKey(r.Profession),
                        ArchiveProfessionKey(personLine.Profession),
                        StringComparison.OrdinalIgnoreCase)
                    : string.Equals(
                        (r.Profession ?? string.Empty).Trim(),
                        (personLine.Profession ?? string.Empty).Trim(),
                        StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var lines = new List<TrainingStatusTreeLine>();
        var dataLevel = personLine.Level + 1;
        foreach (var row in personRows
                     .Where(r => !string.IsNullOrWhiteSpace(r.Program))
                     .OrderBy(r => TrainingProgramDisplayOrder.GetRank(r.Program))
                     .ThenBy(r => r.Program, StringComparer.CurrentCultureIgnoreCase))
        {
            lines.Add(TrainingStatusTreeLine.Data(
                row,
                dataLevel,
                row.ConductId ?? NodeId("prg", row.Organization, row.Person, row.Program),
                id));
        }

        if (lines.Count == 0)
        {
            var stub = personRows.FirstOrDefault() ?? new TrainingStatusRow(
                personLine.Organization,
                string.Empty,
                personLine.Profession ?? string.Empty,
                personLine.Label,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                null,
                string.Empty,
                string.Empty);
            lines.Add(TrainingStatusTreeLine.Data(
                stub,
                dataLevel,
                NodeId("empty", personLine.Organization, personLine.Label),
                id));
        }

        return Partial("_TrainingStatusProgramRows", new TrainingProgramRowsPartialModel
        {
            Lines = lines,
            ProtocolMovementOptions = ProtocolMovementOptions,
            UpdStatusOptions = UpdStatusOptions,
            SaveIndex = new TrainingSaveIndexCounter { Value = Math.Max(0, startIndex) }
        });
    }

    public IActionResult OnGetExport()
    {
        IsArchive = ArchiveOnly || IsArchiveRequest();
        LoadReport();

        var bytes = TrainingStatusExcelExporter.Export(
            Rows,
            Organization,
            forArchive: IsArchive,
            dismissalByEmployee: DismissalDateByEmployee,
            transferArchiveKeys: TransferArchiveKeys);

        var fileName = TrainingStatusExcelExporter.BuildFileName(Organization, IsArchive, ReportDate);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    public IActionResult OnPostSaveStandard(
        string? organization,
        string? person,
        string? profession,
        string? standardId,
        string? reportDate,
        string? selectedOrganization,
        string? department,
        string? trainingType,
        string? trainingKind,
        string? professionFilter,
        string? personQ,
        string? protocolMovement,
        string? updStatus)
    {
        IsArchive = ArchiveOnly || IsArchiveRequest();
        if (!string.IsNullOrWhiteSpace(selectedOrganization) || selectedOrganization == "")
        {
            SelectedOrganization = selectedOrganization;
        }

        // Сохранить состояние «развернуть программы» при смене стандарта.
        ExpandPrograms = ExpandPrograms
            || string.Equals(Request.Form["expandPrograms"], "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Request.Query["expandPrograms"], "true", StringComparison.OrdinalIgnoreCase);

        object Route() => new
        {
            reportDate,
            selectedOrganization = SelectedOrganization,
            department,
            trainingType,
            trainingKind,
            profession = professionFilter,
            personQ,
            protocolMovement,
            updStatus,
            expandPrograms = ExpandPrograms ? (bool?)true : null
        };

        if (string.IsNullOrWhiteSpace(person))
        {
            TempData["Message"] = "Не указан сотрудник.";
            return RedirectToPage(PageRoute(), Route());
        }

        var org = string.IsNullOrWhiteSpace(organization)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : organization.Trim();
        var actualProfession = ResolveEmployeeProfession(org, person.Trim(), profession?.Trim() ?? "");

        if (string.IsNullOrWhiteSpace(standardId))
        {
            _assignments.SetAssignment(org, person.Trim(), actualProfession, null);
            // Дублируем по профессии из формы — ключ в отчёте может отличаться от штата.
            var postedProfession = (profession ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(postedProfession)
                && !string.Equals(postedProfession, actualProfession, StringComparison.OrdinalIgnoreCase))
            {
                _assignments.SetAssignment(org, person.Trim(), postedProfession, null);
            }

            InvalidateRowsCache();
            TempData["Message"] = $"Стандарт обучения для «{person}» снят.";
            return RedirectToPage(PageRoute(), Route());
        }

        var standard = _standards.LoadAll()
            .FirstOrDefault(s => string.Equals(s.Id, standardId.Trim(), StringComparison.Ordinal));
        if (standard is null)
        {
            TempData["Message"] = "Стандарт обучения не найден.";
            return RedirectToPage(PageRoute(), Route());
        }

        var sameOrg = string.Equals(standard.Organization.Trim(), org, StringComparison.OrdinalIgnoreCase)
                      || OrganizationMatcher.Matches(org, standard.Organization);
        if (!sameOrg)
        {
            TempData["Message"] =
                $"Стандарт «{standard.Name}» относится к «{standard.Organization}», а не к «{org}». Выберите стандарт своей компании.";
            return RedirectToPage(PageRoute(), Route());
        }

        _assignments.SetAssignment(org, person.Trim(), actualProfession, standardId.Trim());
        InvalidateRowsCache();
        TempData["Message"] = $"Стандарт обучения для «{person}» обновлён.";
        return RedirectToPage(PageRoute(), Route());
    }

    public IActionResult OnPostSave(
        string[]? conductId,
        string[]? organization,
        string[]? department,
        string[]? profession,
        string[]? person,
        string[]? program,
        string[]? trainingDate,
        string[]? certificateNumber,
        string[]? protocolMovement,
        string[]? updStatus,
        string[]? comment,
        string[]? cellStyles,
        string[]? notTraining,
        string[]? periodicity,
        string[]? personMetaOrg,
        string[]? personMetaPerson,
        string[]? personMetaProfession,
        string[]? personObjectNote,
        string[]? personJournal,
        string[]? personPractice,
        string? filterSelectedOrganization = null,
        string? filterDepartment = null,
        string? filterTrainingType = null,
        string? filterTrainingKind = null,
        string? filterProfession = null,
        string? filterPersonQ = null,
        string? filterProtocolMovement = null,
        string? filterUpdStatus = null,
        bool expandPrograms = false)
    {
        IsArchive = ArchiveOnly || IsArchiveRequest();
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        // Фильтры из отдельных полей (не смешивать с массивами строк таблицы).
        if (!string.IsNullOrWhiteSpace(filterSelectedOrganization))
        {
            SelectedOrganization = filterSelectedOrganization;
        }

        Department = filterDepartment;
        TrainingType = filterTrainingType;
        TrainingKind = filterTrainingKind;
        Profession = filterProfession;
        PersonQ = filterPersonQ;
        ProtocolMovement = filterProtocolMovement;
        UpdStatus = filterUpdStatus;
        // После «Сохранить» / импорта — программы остаются развёрнутыми, если так было.
        ExpandPrograms = expandPrograms
            || string.Equals(Request.Form["expandPrograms"], "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Request.Query["expandPrograms"], "true", StringComparison.OrdinalIgnoreCase);

        // Один проход по Request.Form вместо FormArray×20 (на 300+ строк это заметно).
        var formFields = ParseIndexedFormFields();
        conductId = FormArrayFromMap(formFields, "conductId", conductId);
        organization = FormArrayFromMap(formFields, "organization", organization);
        department = FormArrayFromMap(formFields, "department", department);
        profession = FormArrayFromMap(formFields, "profession", profession);
        person = FormArrayFromMap(formFields, "person", person);
        program = FormArrayFromMap(formFields, "program", program);
        trainingDate = FormArrayFromMap(formFields, "trainingDate", trainingDate);
        certificateNumber = FormArrayFromMap(formFields, "certificateNumber", certificateNumber);
        protocolMovement = FormArrayFromMap(formFields, "protocolMovement", protocolMovement);
        updStatus = FormArrayFromMap(formFields, "updStatus", updStatus);
        comment = FormArrayFromMap(formFields, "comment", comment);
        cellStyles = FormArrayFromMap(formFields, "cellStyles", cellStyles);
        notTraining = FormArrayFromMap(formFields, "notTraining", notTraining);
        periodicity = FormArrayFromMap(formFields, "periodicity", periodicity);
        personMetaOrg = FormArrayFromMap(formFields, "personMetaOrg", personMetaOrg);
        personMetaPerson = FormArrayFromMap(formFields, "personMetaPerson", personMetaPerson);
        personMetaProfession = FormArrayFromMap(formFields, "personMetaProfession", personMetaProfession);
        personObjectNote = FormArrayFromMap(formFields, "personObjectNote", personObjectNote);
        personJournal = FormArrayFromMap(formFields, "personJournal", personJournal);
        personPractice = FormArrayFromMap(formFields, "personPractice", personPractice);

        var list = _conduct.LoadAll();
        var employeeCache = new EmployeeLookupCache(_employees);
        SavePersonRowExtras(
            list,
            employeeCache,
            personMetaOrg,
            personMetaPerson,
            personMetaProfession,
            personObjectNote,
            personJournal,
            personPractice);

        var count = MaxLen(
            person, program, conductId, organization, department, profession,
            trainingDate, certificateNumber, protocolMovement, updStatus, comment, cellStyles, notTraining, periodicity);
        if (count == 0 && (personMetaPerson?.Length ?? 0) == 0)
        {
            TempData["Message"] = "Нет строк для сохранения.";
            return RedirectToPage(PageRoute(), FilterRoute());
        }

        var standardsCache = _standards.LoadAll();
        var assignmentMap = _assignments.LoadAll()
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First().StandardId, StringComparer.OrdinalIgnoreCase);
        var kindsCache = _kinds.Load();
        var wantSnapshot = IsArchive;
        var savedRows = 0;

        // Индексы вместо FirstOrDefault/FindIndex по всему conduct на каждую строку.
        var byId = new Dictionary<string, TrainingConductRecord>(StringComparer.Ordinal);
        var byPersonProgram = new Dictionary<string, TrainingConductRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in list)
        {
            if (!string.IsNullOrWhiteSpace(row.Id))
            {
                byId[row.Id] = row;
            }

            if (row.IsArchivedSnapshot != wantSnapshot
                || string.IsNullOrWhiteSpace(row.Person)
                || string.IsNullOrWhiteSpace(row.Program))
            {
                continue;
            }

            var key = ConductLookupKey(row.Organization, row.Person, row.Program);
            if (!byPersonProgram.ContainsKey(key))
            {
                byPersonProgram[key] = row;
            }
        }

        for (var i = 0; i < count; i++)
        {
            var personName = ValueAt(person, i);
            var programName = ValueAt(program, i);
            if (string.IsNullOrWhiteSpace(personName) || string.IsNullOrWhiteSpace(programName))
            {
                continue;
            }

            var org = ValueAt(organization, i);
            if (string.IsNullOrWhiteSpace(org))
            {
                org = Organization;
            }
            // Не сохраняем подразделение («Строительство») как юрлицо — иначе вторая ветка в дереве.
            else if (!OrganizationMatcher.LooksLikeOrganization(org)
                     && !string.IsNullOrWhiteSpace(Organization)
                     && OrganizationMatcher.LooksLikeOrganization(Organization))
            {
                org = Organization;
            }

            var dept = ValueAt(department, i);
            var postedProf = ValueAt(profession, i);
            var dateText = ValueAt(trainingDate, i);
            var cert = ValueAt(certificateNumber, i);
            var movement = ValueAt(protocolMovement, i);
            var upd = ValueAt(updStatus, i);
            var note = ValueAt(comment, i);
            var stylesJson = ValueAt(cellStyles, i);
            var postedPeriodicity = ValueAt(periodicity, i);
            var parsedStyles = ParseCellStyles(stylesJson);
            DateTime? parsedDate = ParseDate(dateText);
            var markNotTraining = IsNotTrainingFlag(ValueAt(notTraining, i));
            var manualPeriod = TrainingProgramCatalog.AllowsManualPeriodicityInReport(programName);
            var periodOverride = manualPeriod && !TrainingProgramCatalog.IsBlankPeriodicityMarker(postedPeriodicity)
                ? postedPeriodicity.Trim()
                : string.Empty;
            // Для профобучения пустое значение в форме — явное «сбросить» переопределение.
            var periodPosted = manualPeriod;

            var movementEmpty = string.IsNullOrWhiteSpace(movement) || TrainingConductValues.IsBlankMarker(movement);
            var updEmpty = string.IsNullOrWhiteSpace(upd) || TrainingConductValues.IsBlankMarker(upd);
            var id = ValueAt(conductId, i);
            var hasPayload = markNotTraining
                || parsedDate.HasValue
                || !string.IsNullOrWhiteSpace(cert)
                || !movementEmpty
                || !updEmpty
                || !string.IsNullOrWhiteSpace(note)
                || parsedStyles.Count > 0
                || (periodPosted && !string.IsNullOrWhiteSpace(periodOverride));

            // Пустая новая строка без id — сразу дальше (без поиска по conduct и штату).
            if (string.IsNullOrWhiteSpace(id) && !hasPayload && !periodPosted)
            {
                continue;
            }

            TrainingConductRecord? existing = null;
            if (!string.IsNullOrWhiteSpace(id) && byId.TryGetValue(id, out var byIdHit))
            {
                existing = byIdHit;
            }

            // ID от чужой программы (старое мягкое «Арматурщик»↔«Арматурщик 4 разряд») — не перезаписываем.
            if (existing is not null
                && !string.Equals(existing.Program, programName, StringComparison.OrdinalIgnoreCase))
            {
                existing = null;
            }

            // Не пишем активный отчёт в снимок архива и наоборот.
            // Иначе дата/заметка «исчезают»: запись есть, но FindConduct её не показывает.
            if (existing is not null && existing.IsArchivedSnapshot != wantSnapshot)
            {
                existing = null;
            }

            if (existing is null
                && byPersonProgram.TryGetValue(ConductLookupKey(org, personName, programName), out var byKeyHit)
                && byKeyHit.IsArchivedSnapshot == wantSnapshot)
            {
                existing = byKeyHit;
            }

            if (existing is null && (hasPayload || periodPosted || !string.IsNullOrWhiteSpace(id)))
            {
                // Fallback: организация в записи пустая / другое написание.
                foreach (var candidate in list)
                {
                    if (candidate.IsArchivedSnapshot != wantSnapshot)
                    {
                        continue;
                    }

                    if (!string.Equals(candidate.Person, personName, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(candidate.Program, programName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(candidate.Organization)
                        || string.Equals(candidate.Organization, org, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcher.Matches(candidate.Organization, org))
                    {
                        existing = candidate;
                        break;
                    }
                }
            }

            var periodChanged = periodPosted
                && !string.Equals(
                    existing?.Periodicity?.Trim() ?? string.Empty,
                    periodOverride,
                    StringComparison.OrdinalIgnoreCase);
            if (existing is null && !hasPayload && !periodChanged)
            {
                continue;
            }

            // Штат — только для строк, которые реально сохраняем.
            var emp = employeeCache.Find(org, personName);
            var prof = !string.IsNullOrWhiteSpace(emp?.Profession)
                ? emp!.Profession.Trim()
                : postedProf;

            var isNew = existing is null;
            existing ??= new TrainingConductRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                IsArchivedSnapshot = wantSnapshot
            };
            existing.IsArchivedSnapshot = wantSnapshot;
            existing.Organization = org;
            existing.Department = !string.IsNullOrWhiteSpace(emp?.Department)
                ? emp!.Department.Trim()
                : dept;
            existing.Profession = prof;
            existing.Person = personName;
            existing.Program = programName;
            existing.TrainingDate = parsedDate;
            existing.ProtocolNumber = cert.Trim();
            existing.CertificateNumber = cert.Trim();
            existing.ProtocolMovement = NormalizeProtocolMovement(movement);
            existing.UpdStatus = NormalizeUpdStatus(upd);
            existing.Comment = note.Trim();
            existing.CellStyles = parsedStyles;
            if (periodPosted)
            {
                existing.Periodicity = periodOverride;
            }

            savedRows++;

            var standard = TrainingReportBuilder.ResolveStandard(
                standardsCache,
                assignmentMap,
                org,
                personName,
                prof);
            var catalogPeriodicity = TrainingProgramCatalog.ResolvePeriodicityForKind(
                programName,
                kindsCache,
                standard?.PersonnelCategory,
                standard?.ProgramPeriodicities);
            var effectivePeriodicity = !TrainingProgramCatalog.IsBlankPeriodicityMarker(existing.Periodicity)
                ? existing.Periodicity.Trim()
                : catalogPeriodicity;

            if (markNotTraining)
            {
                existing.Result = TrainingConductValues.NotTrainingResult;
                if (parsedDate.HasValue)
                {
                    existing.NextTrainingDate = TrainingConductValues.IsOneTimePeriodicity(effectivePeriodicity)
                        ? null
                        : parsedDate.Value.AddMonths(
                            TrainingProgramCatalog.PeriodMonthsFromPeriodicity(effectivePeriodicity));
                }
                else
                {
                    existing.NextTrainingDate = null;
                }
            }
            else if (parsedDate.HasValue)
            {
                existing.Result = string.IsNullOrWhiteSpace(existing.Result)
                                  || existing.Result.Contains("не проводилось", StringComparison.OrdinalIgnoreCase)
                                  || TrainingConductValues.IsNotTraining(existing.Result)
                    ? (programName.Contains("проверк", StringComparison.OrdinalIgnoreCase) ? "Сдал" : "Пройден")
                    : existing.Result;

                existing.NextTrainingDate = TrainingConductValues.IsOneTimePeriodicity(effectivePeriodicity)
                    ? null
                    : parsedDate.Value.AddMonths(
                        TrainingProgramCatalog.PeriodMonthsFromPeriodicity(effectivePeriodicity));
            }
            else
            {
                existing.NextTrainingDate = null;
                // Без даты прохождения нельзя оставлять «Сдал/Пройден» (в т.ч. дефолт модели).
                existing.Result = "Не проводилось";
            }

            if (isNew)
            {
                list.Add(existing);
            }

            byId[existing.Id] = existing;
            byPersonProgram[ConductLookupKey(existing.Organization, existing.Person, existing.Program)] = existing;
        }

        _conduct.SaveAll(list);
        InvalidateRowsCache();
        TempData["Message"] = savedRows > 0
            ? $"Сохранено строк: {savedRows}."
            : "Нет изменений для сохранения (проверьте дату или заметку).";
        return RedirectToPage(PageRoute(), FilterRoute());
    }

    private void SavePersonRowExtras(
        List<TrainingConductRecord> conduct,
        EmployeeLookupCache employeeCache,
        string[]? personMetaOrg,
        string[]? personMetaPerson,
        string[]? personMetaProfession,
        string[]? personObjectNote,
        string[]? personJournal,
        string[]? personPractice)
    {
        var n = personMetaPerson?.Length ?? 0;
        if (n == 0)
        {
            return;
        }

        var updates = new List<TrainingPersonStandardAssignmentStore.PersonExtrasUpdate>(n);
        var notesByPerson = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < n; i++)
        {
            var personName = ValueAt(personMetaPerson, i);
            if (string.IsNullOrWhiteSpace(personName))
            {
                continue;
            }

            var org = ValueAt(personMetaOrg, i);
            if (string.IsNullOrWhiteSpace(org))
            {
                org = Organization;
            }

            var emp = employeeCache.Find(org, personName);
            var postedProf = ValueAt(personMetaProfession, i);
            var prof = !string.IsNullOrWhiteSpace(emp?.Profession)
                ? emp!.Profession.Trim()
                : postedProf;
            var objectNote = ValueAt(personObjectNote, i).Trim();
            var journal = IsNotTrainingFlag(ValueAt(personJournal, i));
            var practice = IsNotTrainingFlag(ValueAt(personPractice, i));
            updates.Add(new TrainingPersonStandardAssignmentStore.PersonExtrasUpdate(
                org, personName, prof, objectNote, journal, practice));
            notesByPerson[org + "\n" + personName] = objectNote;
        }

        _assignments.SetPersonExtrasBatch(updates);

        if (notesByPerson.Count == 0)
        {
            return;
        }

        // Индекс по ФИО → (орг, заметка), один проход по conduct.
        var notesByName = new Dictionary<string, List<(string Org, string Note)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, note) in notesByPerson)
        {
            var sep = key.IndexOf('\n');
            if (sep <= 0)
            {
                continue;
            }

            var org = key[..sep];
            var personName = key[(sep + 1)..];
            if (!notesByName.TryGetValue(personName, out var bucket))
            {
                bucket = [];
                notesByName[personName] = bucket;
            }

            bucket.Add((org, note));
        }

        foreach (var row in conduct)
        {
            if (string.IsNullOrWhiteSpace(row.Person)
                || !notesByName.TryGetValue(row.Person, out var bucket))
            {
                continue;
            }

            foreach (var (org, note) in bucket)
            {
                if (string.IsNullOrWhiteSpace(row.Organization)
                    || string.Equals(row.Organization, org, StringComparison.OrdinalIgnoreCase)
                    || OrganizationMatcher.Matches(row.Organization, org))
                {
                    row.ObjectNote = note;
                    break;
                }
            }
        }
    }

    public async Task<IActionResult> OnPostImportScheduleAsync(
        IFormFile? scheduleFile,
        bool overwriteExisting = false)
    {
        IsArchive = ArchiveOnly || IsArchiveRequest();
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SelectedOrganization = Organization;
        OrganizationOptions = _orgs.GetVisibleOrganizations(HttpContext);

        if (scheduleFile is null || scheduleFile.Length == 0)
        {
            TempData["Message"] = "Выберите файл Excel (.xlsx) — выгрузку отчёта/архива или график обучения.";
            return RedirectToPage(PageRoute(), FilterRoute());
        }

        var name = scheduleFile.FileName ?? string.Empty;
        if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "Нужен файл Excel (.xlsx / .xls).";
            return RedirectToPage(PageRoute(), FilterRoute());
        }

        // NPOI при Dispose закрывает Stream — читаем байты один раз и открываем копии.
        await using var upload = scheduleFile.OpenReadStream();
        using var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer);
        var fileBytes = buffer.ToArray();
        if (fileBytes.Length == 0)
        {
            TempData["Message"] = "Файл пустой.";
            return RedirectToPage(PageRoute(), FilterRoute());
        }

        if (string.IsNullOrWhiteSpace(Organization))
        {
            TempData["Message"] = "Выберите организацию в шапке — импорт идёт только для открытого юрлица.";
            return RedirectToPage(PageRoute(), FilterRoute());
        }

        var forArchive = IsArchive;

        try
        {
            // 1) Выгрузка «Скачать Excel» (отчёт / архив) — тот же формат обратно.
            using (var detectStream = new MemoryStream(fileBytes, writable: false))
            {
                if (TrainingStatusExcelImporter.LooksLikeStatusExport(detectStream, name))
                {
                    var reportRows = BuildImportReportRows(Organization, forArchive);
                    var employees = forArchive
                        ? _employees.LoadIncludingInactive(Organization)
                        : _employees.LoadForTrainingReport(Organization);
                    using var importStream = new MemoryStream(fileBytes, writable: false);
                    var statusResult = TrainingStatusExcelImporter.Import(
                        importStream,
                        name,
                        Organization,
                        reportRows,
                        employees,
                        _conduct,
                        forArchive,
                        overwriteExisting);
                    InvalidateRowsCache();
                    TempData["Message"] = statusResult.FormatMessage();
                    // «Уже заполненные не трогали» — не ошибка; ошибка только когда совсем ничего не подошло.
                    if (statusResult.UpdatedRows == 0
                        && statusResult.CreatedRows == 0
                        && statusResult.PreservedFilledRows == 0)
                    {
                        TempData["IsError"] = "1";
                    }

                    return RedirectToPage(PageRoute(), FilterRoute());
                }
            }

            // 2) График обучения (широкая таблица) — как в отчёте.
            using var parseStream = new MemoryStream(fileBytes, writable: false);
            var parsed = TrainingScheduleExcelParser.Parse(parseStream, name);

            // Матчинг листа Excel → только текущая организация (и её алиасы из каталога).
            var catalog = _orgs.AllOrganizations
                .Concat(OrganizationOptions)
                .Select(o => (o ?? string.Empty).Trim())
                .Where(o => o.Length > 0)
                .Where(o => string.Equals(o, Organization, StringComparison.OrdinalIgnoreCase)
                            || OrganizationMatcher.Matches(o, Organization)
                            || OrganizationMatcher.Matches(Organization, o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!catalog.Contains(Organization, StringComparer.OrdinalIgnoreCase))
            {
                catalog.Insert(0, Organization);
            }

            var result = _scheduleImport.Import(
                parsed,
                catalog,
                org => forArchive
                    ? _employees.LoadIncludingInactive(org)
                    : _employees.LoadForTrainingReport(org),
                org => BuildImportReportRows(org, forArchive),
                forArchive,
                targetOrganization: Organization,
                overwriteExisting: overwriteExisting);

            InvalidateRowsCache();
            TempData["Message"] = result.FormatMessage();
            if (result.UpdatedCells == 0 && result.CreatedStandards == 0 && result.PersonExtrasUpdated == 0)
            {
                TempData["IsError"] = "1";
            }

            return RedirectToPage(PageRoute(), FilterRoute());
        }
        catch (Exception ex)
        {
            TempData["Message"] = "Не удалось импортировать Excel: " + ex.Message;
            TempData["IsError"] = "1";
            return RedirectToPage(PageRoute(), FilterRoute());
        }
    }

    private IReadOnlyList<TrainingStatusRow> BuildImportReportRows(string organization, bool forArchive = false)
    {
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return [];
        }

        List<(string Organization, string Name, string Profession, string Department)> employeeList;
        IReadOnlyList<TrainingConductRecord> conduct;
        if (forArchive)
        {
            var employedNames = _employees.LoadForTrainingReport(org)
                .Select(e => e.Name.Trim())
                .Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _employees.LoadIncludingInactive(org))
            {
                if (OrganizationMatcher.IsIndividualEntrepreneurPerson(org, e.Name, e.Profession))
                {
                    employedNames.Add(e.Name.Trim());
                }
            }

            employeeList = _employees.LoadIncludingInactive(org)
                .Where(e => !EmployeeHrStatus.IsEmployed(e))
                .Where(e => !OrganizationMatcher.IsIndividualEntrepreneurPerson(org, e.Name, e.Profession))
                .Select(e => (org, e.Name, e.Profession, e.Department))
                .ToList();

            var allConduct = _conduct.LoadAll();
            foreach (var group in allConduct
                         .Where(c => c.IsArchivedSnapshot)
                         .Where(c => OrganizationMatchesOrg(c.Organization, org))
                         .Where(c => !string.IsNullOrWhiteSpace(c.Person))
                         .Where(c => employedNames.Contains(c.Person.Trim()))
                         .Where(c => !OrganizationMatcher.IsIndividualEntrepreneurPerson(
                             org, c.Person, c.Profession))
                         .GroupBy(c => (
                             Person: c.Person.Trim(),
                             Profession: string.IsNullOrWhiteSpace(c.Profession) ? "—" : c.Profession.Trim(),
                             Department: string.IsNullOrWhiteSpace(c.Department) ? "—" : c.Department.Trim())))
            {
                employeeList.Add((org, group.Key.Person, group.Key.Profession, group.Key.Department));
            }

            foreach (var group in allConduct
                         .Where(c => !c.IsArchivedSnapshot)
                         .Where(c => OrganizationMatchesOrg(c.Organization, org))
                         .Where(c => !string.IsNullOrWhiteSpace(c.Person))
                         .Where(c => !employedNames.Contains(c.Person.Trim()))
                         .Where(c => !OrganizationMatcher.IsIndividualEntrepreneurPerson(
                             org, c.Person, c.Profession))
                         .GroupBy(c => c.Person.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                if (employeeList.Any(e =>
                        string.Equals(e.Name, group.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var latest = group
                    .OrderByDescending(c => c.TrainingDate ?? DateTime.MinValue)
                    .First();
                employeeList.Add((
                    org,
                    group.Key,
                    string.IsNullOrWhiteSpace(latest.Profession) ? "—" : latest.Profession.Trim(),
                    string.IsNullOrWhiteSpace(latest.Department) ? "—" : latest.Department.Trim()));
            }

            employeeList = employeeList
                .GroupBy(
                    e => ArchiveEmployeeKey(e.Organization, e.Name, e.Profession),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            conduct = allConduct
                .Where(c =>
                    c.IsArchivedSnapshot
                    || (!employedNames.Contains((c.Person ?? string.Empty).Trim())
                        && !OrganizationMatcher.IsIndividualEntrepreneurPerson(
                            org, c.Person, c.Profession)))
                .ToList();
        }
        else
        {
            employeeList = _employees.LoadForTrainingReport(org)
                .Select(e => (org, e.Name, e.Profession, e.Department))
                .ToList();
            employeeList = EmployeeReportCollapse.Collapse(employeeList);
            SyncApplicationCommentsIntoConduct();
            conduct = _conduct.LoadForReports();
        }

        var kinds = _kinds.Load();
        if (!forArchive)
        {
            TrainingIndividualEntrepreneurStandards.EnsureForOrganization(
                org,
                _standards,
                _assignments,
                employeeList,
                kinds,
                out _);
        }

        var standards = _standards.LoadAll()
            .Where(s => !string.IsNullOrWhiteSpace(s.Organization))
            .Where(s => string.Equals(s.Organization.Trim(), org, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcher.Matches(org, s.Organization))
            .ToList();

        var assignmentMap = _assignments.LoadAll()
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First().StandardId, StringComparer.OrdinalIgnoreCase);

        return TrainingReportBuilder.BuildManualStatusReport(
            standards,
            org,
            employeeList,
            ReportDate == default ? DateTime.Today : ReportDate,
            conduct,
            assignmentMap,
            kinds,
            forArchive: forArchive);
    }

    private static Dictionary<string, string> ParseCellStyles(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null)
            {
                return result;
            }

            foreach (var (key, value) in map)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var k = key.Trim().ToLowerInvariant();
                if (k is not ("program" or "comment" or "protocol"))
                {
                    continue;
                }

                var css = SanitizeCellCss(value);
                if (!string.IsNullOrWhiteSpace(css))
                {
                    result[k] = css;
                }
            }
        }
        catch
        {
            // ignore bad client payload
        }

        return result;
    }

    private static string SanitizeCellCss(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > 200)
        {
            return string.Empty;
        }

        if (text.Contains('<') || text.Contains('>') || text.Contains("url(", StringComparison.OrdinalIgnoreCase)
            || text.Contains("expression", StringComparison.OrdinalIgnoreCase)
            || text.Contains('@'))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var chunk in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = chunk.IndexOf(':');
            if (idx <= 0)
            {
                continue;
            }

            var prop = chunk[..idx].Trim().ToLowerInvariant();
            var val = chunk[(idx + 1)..].Trim();
            if (prop is "color" or "font-weight")
            {
                parts.Add($"{prop}: {val}");
            }
        }

        return string.Join("; ", parts);
    }

    private static string PageRoute(bool isArchive) =>
        isArchive ? "/Training/Archive" : "/Training/Status";

    private string PageRoute() => PageRoute(IsArchive || ArchiveOnly);

    private bool IsArchiveRequest() =>
        (Request.Path.Value ?? string.Empty).Contains("/Training/Archive", StringComparison.OrdinalIgnoreCase);

    private object FilterRoute() => new
    {
        reportDate = ReportDate.ToString("yyyy-MM-dd"),
        selectedOrganization = SelectedOrganization,
        department = Department,
        trainingType = TrainingType,
        trainingKind = TrainingKind,
        profession = Profession,
        personQ = PersonQ,
        protocolMovement = ProtocolMovement,
        updStatus = UpdStatus,
        expandPrograms = ExpandPrograms ? (bool?)true : null
    };

    private void LoadReport()
    {
        ReportDate = DateTime.Today;
        if (!IsArchive)
        {
            IsArchive = ArchiveOnly || IsArchiveRequest();
        }

        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SelectedOrganization = Organization;
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsArchive ? "Архив обучений" : "Отчет по обучению";
        OrganizationOptions = _orgs.GetVisibleOrganizations(HttpContext);

        var selectedOrg = SelectedOrganization?.Trim() ?? string.Empty;
        ViewData["Subtitle"] = Organization;
        Message = TempData["Message"] as string;

        IncludeProgramRows = ExpandPrograms
            || (!string.IsNullOrWhiteSpace(PersonQ) && PersonQ.Trim().Length >= 2)
            || !string.IsNullOrWhiteSpace(TrainingKind)
            || !string.IsNullOrWhiteSpace(TrainingType)
            || !string.IsNullOrWhiteSpace(ProtocolMovement)
            || !string.IsNullOrWhiteSpace(UpdStatus);

        // «Все» — тот же список ЮЛ, что медосмотры/риски (без дублей алиасов).
        var orgsToLoad = string.IsNullOrWhiteSpace(selectedOrg)
            ? _employees.ResolveOrganizationsForAllReports(
                OrganizationOptions,
                _orgs.IsOrganizationHidden).ToList()
            : [selectedOrg];

        // Чиним проведения с неверным юрлицом — не чаще раза в 30 мин (дорого на большом conduct.json).
        if (DateTime.UtcNow - _repairOrgsUtc >= HeavyMaintenanceInterval)
        {
            _repairOrgsUtc = DateTime.UtcNow;
            foreach (var org in orgsToLoad)
            {
                if (_conduct.RepairMisfiledOrganizations(org) > 0)
                {
                    InvalidateRowsCache();
                }
            }
        }

        var employeeList = new List<(string Organization, string Name, string Profession, string Department)>();
        var employeesForHr = new List<OrganizationEmployee>();
        var employedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dismissedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dismissalByEmployee = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        var dismissalByOrgName = new List<(string Organization, string Name, DateTime? Date)>();
        var transferArchiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (IsArchive)
        {
        foreach (var org in orgsToLoad)
        {
                foreach (var e in _employees.LoadForTrainingReport(org))
                {
                    employedNames.Add(e.Name.Trim());
                }

                // Сам ИП — всегда «работающий» для архива (даже если в 1С помечен неактивным
                // или ФИО в отчёте сокращено: «Шварц Б.Э.» vs «Шварц Борис Эдуардович»).
                foreach (var e in _employees.LoadIncludingInactive(org))
                {
                    if (OrganizationMatcher.IsIndividualEntrepreneurPerson(org, e.Name, e.Profession))
                    {
                        employedNames.Add(e.Name.Trim());
                    }
                }
            }

            foreach (var org in orgsToLoad)
            {
                foreach (var e in _employees.LoadIncludingInactive(org)
                             .Where(e => !EmployeeHrStatus.IsEmployed(e))
                             .Where(e => !OrganizationMatcher.IsIndividualEntrepreneurPerson(
                                 org, e.Name, e.Profession)))
                {
                    var name = e.Name.Trim();
                    dismissedNames.Add(name);
                    employeeList.Add((org, name, e.Profession, e.Department));
                    employeesForHr.Add(e);
                    RememberDismissalDate(dismissalByEmployee, org, name, e.Profession, e.DismissalDate);
                    if (e.DismissalDate is not null)
                    {
                        dismissalByOrgName.Add((org, name, e.DismissalDate));
                    }
                }
            }
        }
        else
        {
            foreach (var org in orgsToLoad)
            {
                foreach (var e in _employees.LoadForTrainingReport(org))
                {
                    // Уволенные — только в архиве; в отчёт по обучению не попадают.
                    if (!EmployeeHrStatus.IsEmployed(e))
                    {
                        continue;
                    }

                    employeeList.Add((org, e.Name, e.Profession, e.Department));
                    employeesForHr.Add(e);
                }
            }
        }

        var conductForArchive = IsArchive ? _conduct.LoadAll() : null;
        if (IsArchive && conductForArchive is not null)
        {
            foreach (var org in orgsToLoad)
            {
                // Дополнительно: даты увольнения из 1С по всем неактивным (в т.ч. если ФИО в обучении сокращено)
                foreach (var e in _employees.LoadIncludingInactive(org))
                {
                    if (e.DismissalDate is null)
                    {
                        continue;
                    }

                    RememberDismissalDate(dismissalByEmployee, org, e.Name.Trim(), e.Profession, e.DismissalDate);
                    dismissalByOrgName.Add((org, e.Name.Trim(), e.DismissalDate));
                }
                foreach (var group in conductForArchive
                             .Where(c => c.IsArchivedSnapshot)
                             .Where(c => OrganizationMatchesOrg(c.Organization, org))
                             .Where(c => !string.IsNullOrWhiteSpace(c.Person))
                             .Where(c => employedNames.Contains(c.Person.Trim()))
                             .Where(c => !OrganizationMatcher.IsIndividualEntrepreneurPerson(
                                 org, c.Person, c.Profession))
                             .GroupBy(c => (
                                 Person: c.Person.Trim(),
                                 Profession: string.IsNullOrWhiteSpace(c.Profession) ? "—" : c.Profession.Trim(),
                                 Department: string.IsNullOrWhiteSpace(c.Department) ? "—" : c.Department.Trim())))
                {
                    var current = _employees.FindByName(org, group.Key.Person);
                    employeeList.Add((org, group.Key.Person, group.Key.Profession, group.Key.Department));
                    employeesForHr.Add(new OrganizationEmployee
                    {
                        Name = group.Key.Person,
                        Profession = group.Key.Profession,
                        Department = group.Key.Department,
                        IsActive = true,
                        HrStatus = "Перевод",
                        TransferDate = current?.TransferDate
                    });
                    RememberDismissalDate(
                        dismissalByEmployee,
                        org,
                        group.Key.Person,
                        group.Key.Profession,
                        current?.TransferDate);
                    transferArchiveKeys.Add(ArchiveEmployeeKey(org, group.Key.Person, group.Key.Profession));
                }

                foreach (var group in conductForArchive
                             .Where(c => !c.IsArchivedSnapshot)
                             .Where(c => OrganizationMatchesOrg(c.Organization, org))
                             .Where(c => !string.IsNullOrWhiteSpace(c.Person))
                             .Where(c => !employedNames.Contains(c.Person.Trim()))
                             .Where(c => !OrganizationMatcher.IsIndividualEntrepreneurPerson(
                                 org, c.Person, c.Profession))
                             .GroupBy(c => c.Person.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    if (employeeList.Any(e =>
                            string.Equals(e.Name, group.Key, StringComparison.OrdinalIgnoreCase)
                            && OrganizationMatchesOrg(e.Organization, org)))
                    {
                        continue;
                    }

                    dismissedNames.Add(group.Key);
                    var latest = group
                        .OrderByDescending(c => c.TrainingDate ?? DateTime.MinValue)
                        .First();
                    var profession = string.IsNullOrWhiteSpace(latest.Profession) ? "—" : latest.Profession.Trim();
                    employeeList.Add((
                        org,
                        group.Key,
                        profession,
                        string.IsNullOrWhiteSpace(latest.Department) ? "—" : latest.Department.Trim()));

                    var knownDismissal = ResolveDismissalDate(
                        dismissalByEmployee,
                        dismissalByOrgName,
                        org,
                        group.Key,
                        profession);
                    employeesForHr.Add(new OrganizationEmployee
                    {
                        Name = group.Key,
                        Profession = latest.Profession,
                        Department = latest.Department,
                        IsActive = false,
                        HrStatus = "Увольнение",
                        DismissalDate = knownDismissal
                    });
                    RememberDismissalDate(
                        dismissalByEmployee,
                        org,
                        group.Key,
                        profession,
                        knownDismissal);
                }
            }
        }

        DismissalDateByEmployee = dismissalByEmployee;
        TransferArchiveKeys = transferArchiveKeys;
        HrStatusByName = EmployeeHrStatus.BuildMap(employeesForHr);
        employeeList = IsArchive
            ? employeeList
                .GroupBy(
                    e => ArchiveEmployeeKey(e.Organization, e.Name, e.Profession),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList()
            : EmployeeReportCollapse.Collapse(employeeList);

        var kinds = _kinds.Load();
        if (!IsArchive)
        {
            var ieChanged = false;
            foreach (var org in orgsToLoad)
            {
                var orgEmployees = employeeList
                    .Where(e => string.Equals(e.Organization, org, StringComparison.OrdinalIgnoreCase)
                                || OrganizationMatcher.Matches(org, e.Organization))
                    .ToList();
                TrainingIndividualEntrepreneurStandards.EnsureForOrganization(
                    org,
                    _standards,
                    _assignments,
                    orgEmployees,
                    kinds,
                    out var changed);
                ieChanged |= changed;
            }

            if (ieChanged
                || orgsToLoad.Any(o => OrganizationMatcher.InferIndividualEntrepreneurEmployee(o) is not null))
            {
                // Для ИП всегда сбрасываем кэш строк — иначе «без стандарта» залипает до 5 минут.
                InvalidateRowsCache();
            }
        }

        var standards = _standards.LoadAll()
            .Where(s => !string.IsNullOrWhiteSpace(s.Organization))
            .Where(s => string.IsNullOrWhiteSpace(selectedOrg)
                || string.Equals(s.Organization.Trim(), selectedOrg, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(selectedOrg, s.Organization)
                || orgsToLoad.Any(o => OrganizationMatcher.Matches(o, s.Organization)
                    || string.Equals(s.Organization.Trim(), o, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        StandardOptions = standards.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        StandardsByOrganization = StandardOptions
            .Select(s => s.Organization?.Trim() ?? string.Empty)
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                o => o,
                o => (IReadOnlyList<TrainingStandard>)TrainingReportBuilder.StandardsForOrganization(StandardOptions, o),
                StringComparer.OrdinalIgnoreCase);

        var allAssignments = _assignments.LoadAll();
        var assignmentRecords = allAssignments
            .GroupBy(a => TrainingReportBuilder.AssignmentKey(a.Organization, a.Person, a.Profession))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var assignmentMap = assignmentRecords.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.StandardId,
            StringComparer.OrdinalIgnoreCase);

        var cacheKey = $"{selectedOrg}|{(IsArchive ? "a" : "w")}|{ReportDate:yyyy-MM-dd}|{employeeList.Count}";
        List<TrainingStatusRow> allRows;
        lock (RowsCacheGate)
        {
            if (_rowsCache is not null
                && string.Equals(_rowsCacheKey, cacheKey, StringComparison.Ordinal)
                && DateTime.UtcNow - _rowsCacheUtc < TimeSpan.FromMinutes(10))
            {
                allRows = _rowsCache;
            }
            else
            {
                IReadOnlyList<TrainingConductRecord> conduct;
                if (IsArchive && conductForArchive is not null)
                {
                    conduct = conductForArchive.Where(c =>
                        c.IsArchivedSnapshot
                        || dismissedNames.Contains((c.Person ?? string.Empty).Trim())).ToList();
                }
                else
                {
                    SyncApplicationCommentsIntoConduct();
                    conduct = _conduct.LoadForReports();
                }

                allRows = TrainingReportBuilder.BuildManualStatusReport(
                    standards,
                    selectedOrg,
                    employeeList,
                    ReportDate,
                    conduct,
                    assignmentMap,
                    kinds,
                    forArchive: IsArchive).ToList();

                var ebRecords = string.IsNullOrWhiteSpace(selectedOrg)
                    ? _electricalKnowledge.LoadAll()
                    : _electricalKnowledge.Load(selectedOrg);
                allRows.AddRange(ElectricalTrainingReportBridge.ToTrainingRows(ebRecords, ReportDate, employeeList));
                allRows = TrainingReportBuilder.UnifyPersonDepartments(allRows);
                // Подразделение только с карточки штата — не из старых записей проведения.
                allRows = TrainingReportBuilder.ApplyStaffDepartments(allRows, employeeList);
                allRows = allRows
                    .OrderBy(r => r.Organization, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(r => r.Department, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(r => r.Profession, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(r => r.Person, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(r => TrainingProgramDisplayOrder.GetRank(r.Program))
                    .ThenBy(r => r.Program, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (IsArchive)
                {
                    // В архиве инструктажи не нужны — оставляем обучения / аттестации.
                    allRows = allRows
                        .Where(r => string.IsNullOrWhiteSpace(r.Program)
                                    || !TrainingProgramCatalog.IsBriefingProgram(r.Program))
                        .ToList();
                }

                _rowsCacheKey = cacheKey;
                _rowsCacheUtc = DateTime.UtcNow;
                _rowsCache = allRows;
            }
        }

        DepartmentOptions = DistinctSorted(allRows.Select(r => r.Department));
        TrainingTypeOptions = DistinctSorted(allRows.Select(r => r.Status).Concat(allRows.Select(r => r.Result)));
        TrainingKindOptions = DistinctSorted(allRows.Select(r => r.Program));
        ProfessionOptions = DistinctSorted(allRows.Select(r => r.Profession));

        Rows = allRows
            .Where(r => string.IsNullOrWhiteSpace(Department) ||
                        r.Department.Contains(Department, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(TrainingType) ||
                        r.Status.Contains(TrainingType, StringComparison.OrdinalIgnoreCase) ||
                        r.Result.Contains(TrainingType, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(TrainingKind) ||
                        r.Program.Contains(TrainingKind, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(Profession) ||
                        r.Profession.Contains(Profession, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(PersonQ) ||
                        r.Person.Contains(PersonQ, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(ProtocolMovement) ||
                        MatchesProtocolMovement(r.ProtocolMovement, ProtocolMovement))
            .Where(r => string.IsNullOrWhiteSpace(UpdStatus) ||
                        MatchesUpdStatus(r.UpdStatus, UpdStatus))
            .ToList();

        TreeLines = BuildTree(
            Rows,
            DismissalDateByEmployee,
            dismissalByOrgName,
            IsArchive,
            TransferArchiveKeys,
            assignmentRecords,
            includeProgramRows: IncludeProgramRows);
        // Штат человек×ЮЛ (как МО/риски), не строки программ обучения.
        var staffForCount = employeeList
            .Where(p => string.IsNullOrWhiteSpace(Department)
                        || (p.Department ?? string.Empty)
                            .Contains(Department, StringComparison.OrdinalIgnoreCase))
            .Where(p => string.IsNullOrWhiteSpace(Profession)
                        || (p.Profession ?? string.Empty)
                            .Contains(Profession, StringComparison.OrdinalIgnoreCase)
                        || ProfessionMatcher.Matches(Profession, p.Profession))
            .Where(p => string.IsNullOrWhiteSpace(PersonQ)
                        || (p.Name ?? string.Empty)
                            .Contains(PersonQ, StringComparison.OrdinalIgnoreCase));
        PeopleCount = StaffHeadcount.CountPersonOrgCards(staffForCount);
    }

    private static bool OrganizationMatchesOrg(string? recordOrg, string selectedOrg)
    {
        var org = (recordOrg ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return true;
        }

        return string.Equals(org, selectedOrg, StringComparison.OrdinalIgnoreCase)
               || OrganizationMatcher.Matches(selectedOrg, org);
    }

    private static string NormalizeProtocolMovement(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v) || TrainingConductValues.IsBlankMarker(v))
        {
            return string.Empty;
        }

        if (v.Contains("подпис", StringComparison.OrdinalIgnoreCase))
        {
            return "на подписи";
        }

        if (v.Contains("готов", StringComparison.OrdinalIgnoreCase))
        {
            return "готов";
        }

        return v;
    }

    private static string NormalizeUpdStatus(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v) || TrainingConductValues.IsBlankMarker(v))
        {
            return string.Empty;
        }

        if (v.Contains("нет", StringComparison.OrdinalIgnoreCase))
        {
            return "УПД нет";
        }

        if (v.Contains("есть", StringComparison.OrdinalIgnoreCase))
        {
            return "УПД есть";
        }

        return v;
    }

    private static bool MatchesProtocolMovement(string? actual, string? filter)
    {
        var a = NormalizeProtocolMovement(actual);
        var f = NormalizeProtocolMovement(filter);
        if (string.IsNullOrWhiteSpace(f) && TrainingConductValues.IsBlankMarker(filter))
        {
            return string.IsNullOrWhiteSpace(a);
        }

        return string.Equals(a, f, StringComparison.OrdinalIgnoreCase)
               || (string.IsNullOrWhiteSpace(a) && TrainingConductValues.IsBlankMarker(filter));
    }

    private static bool MatchesUpdStatus(string? actual, string? filter)
    {
        var a = NormalizeUpdStatus(actual);
        var f = NormalizeUpdStatus(filter);
        if (TrainingConductValues.IsBlankMarker(filter))
        {
            return string.IsNullOrWhiteSpace(a);
        }

        return string.Equals(a, f, StringComparison.OrdinalIgnoreCase);
    }

    private static string ValueAt(string[]? values, int index) =>
        values is not null && index < values.Length ? values[index] ?? string.Empty : string.Empty;

    /// <summary>
    /// Один проход по ключам формы → словарь name → (index → value).
    /// </summary>
    private Dictionary<string, Dictionary<int, string>> ParseIndexedFormFields()
    {
        var result = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        var form = Request?.Form;
        if (form is null || form.Count == 0)
        {
            return result;
        }

        foreach (var key in form.Keys)
        {
            if (key is null)
            {
                continue;
            }

            var open = key.IndexOf('[', StringComparison.Ordinal);
            if (open <= 0 || !key.EndsWith(']'))
            {
                continue;
            }

            var inner = key[(open + 1)..^1];
            if (!int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                || idx < 0)
            {
                continue;
            }

            var field = key[..open];
            if (!result.TryGetValue(field, out var byIndex))
            {
                byIndex = new Dictionary<int, string>();
                result[field] = byIndex;
            }

            byIndex[idx] = form[key].ToString() ?? string.Empty;
        }

        return result;
    }

    private string[] FormArrayFromMap(
        Dictionary<string, Dictionary<int, string>> map,
        string name,
        string[]? bound)
    {
        if (map.TryGetValue(name, out var byIndex) && byIndex.Count > 0)
        {
            var max = -1;
            foreach (var idx in byIndex.Keys)
            {
                if (idx > max)
                {
                    max = idx;
                }
            }

            var arr = new string[max + 1];
            for (var i = 0; i <= max; i++)
            {
                arr[i] = byIndex.TryGetValue(i, out var v) ? v : string.Empty;
            }

            return arr;
        }

        var form = Request?.Form;
        if (form is not null && form.TryGetValue(name, out var repeated) && repeated.Count > 0)
        {
            return repeated.Select(v => v ?? string.Empty).ToArray();
        }

        return bound ?? [];
    }

    /// <summary>
    /// Собирает массив из form: и <c>name</c>/<c>name</c>/…, и <c>name[0]</c>/<c>name[1]</c>/….
    /// Индексы важнее — не сдвигаются, если браузер не отправил пустой date.
    /// </summary>
    private string[] FormArray(string name, string[]? bound) =>
        FormArrayFromMap(ParseIndexedFormFields(), name, bound);

    private static string ConductLookupKey(string? organization, string person, string program) =>
        ((organization ?? string.Empty).Trim() + "\n" + person.Trim() + "\n" + program.Trim()).ToLowerInvariant();

    /// <summary>Кэш FindByName: один Load(org) на организацию за сохранение.</summary>
    private sealed class EmployeeLookupCache(OrganizationEmployeesService employees)
    {
        private readonly Dictionary<string, Dictionary<string, OrganizationEmployee>> _byOrg =
            new(StringComparer.OrdinalIgnoreCase);

        public OrganizationEmployee? Find(string organization, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var org = (organization ?? string.Empty).Trim();
            if (!_byOrg.TryGetValue(org, out var byName))
            {
                byName = new Dictionary<string, OrganizationEmployee>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in employees.Load(org)
                             .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                             .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    // Как OrganizationEmployeesService.FindByName / SelectBestEmploymentRecord.
                    byName[group.Key] = group
                        .OrderByDescending(e =>
                            (e.IsActive ? 100 : 0)
                            + (OneCEmployeeListParser.IsPrimaryEmployment(e.EmploymentType) ? 10 : 0)
                            + Math.Min((e.EmploymentDate ?? DateTime.MinValue).Year - 2000, 99))
                        .First();
                }

                _byOrg[org] = byName;
            }

            return byName.TryGetValue(name.Trim(), out var hit) ? hit : null;
        }
    }

    private static int MaxLen(params string[]?[] arrays)
    {
        var max = 0;
        foreach (var a in arrays)
        {
            if (a is { Length: > 0 } && a.Length > max)
            {
                max = a.Length;
            }
        }

        return max;
    }

    private static bool IsNotTrainingFlag(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        return v is "1" or "true" or "yes" or "on"
               || string.Equals(v, TrainingConductValues.NotTrainingResult, StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return iso.Date;
        }

        if (DateTime.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var ru))
        {
            return ru.Date;
        }

        return DateTime.TryParse(text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var any)
            ? any.Date
            : null;
    }

    private static List<string> DistinctSorted(IEnumerable<string> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v) && v != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private string ResolveEmployeeProfession(string organization, string personName, string fallback)
    {
        var employee = _employees.FindByName(organization, personName);
        return !string.IsNullOrWhiteSpace(employee?.Profession)
            ? employee!.Profession.Trim()
            : fallback;
    }

    private string ResolveEmployeeDepartment(string organization, string personName, string fallback)
    {
        var employee = _employees.FindByName(organization, personName);
        return !string.IsNullOrWhiteSpace(employee?.Department)
            ? employee!.Department.Trim()
            : fallback;
    }

    private static List<TrainingStatusTreeLine> BuildTree(
        IReadOnlyList<TrainingStatusRow> rows,
        IReadOnlyDictionary<string, DateTime?> dismissalByEmployee,
        IReadOnlyList<(string Organization, string Name, DateTime? Date)>? dismissalByOrgName = null,
        bool isArchive = false,
        IReadOnlySet<string>? transferArchiveKeys = null,
        IReadOnlyDictionary<string, TrainingPersonStandardAssignment>? personAssignments = null,
        bool includeProgramRows = true)
    {
        personAssignments ??= new Dictionary<string, TrainingPersonStandardAssignment>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<TrainingStatusTreeLine>();
        foreach (var orgGroup in rows.GroupBy(r => r.Organization).OrderBy(g => g.Key))
        {
            var orgId = NodeId("o", orgGroup.Key);
            lines.Add(TrainingStatusTreeLine.Group(orgGroup.Key, 0, orgId, null));
            foreach (var deptGroup in orgGroup.GroupBy(r => r.Department).OrderBy(g => g.Key))
            {
                var deptId = NodeId("d", orgGroup.Key, deptGroup.Key);
                lines.Add(TrainingStatusTreeLine.Group(deptGroup.Key, 1, deptId, orgId));
                if (isArchive)
                {
                    foreach (var personGroup in deptGroup
                                 .GroupBy(r => (r.Person, r.Profession))
                                 .OrderBy(g => g.Key.Person, StringComparer.CurrentCultureIgnoreCase)
                                 .ThenBy(g => g.Key.Profession, StringComparer.CurrentCultureIgnoreCase))
                    {
                        AddPersonTreeLines(
                            lines,
                            personGroup,
                            personGroup.Key.Person,
                            orgGroup.Key,
                            deptGroup.Key,
                            deptId,
                            personLevel: 2,
                            dataLevel: 3,
                            dismissalByEmployee,
                            dismissalByOrgName,
                            isArchive: true,
                            transferArchiveKeys: transferArchiveKeys,
                            personAssignments: personAssignments,
                            includeProgramRows: includeProgramRows);
                    }
                }
                else
                {
                    foreach (var profGroup in deptGroup.GroupBy(r => r.Profession).OrderBy(g => g.Key))
                    {
                        var profId = NodeId("p", orgGroup.Key, deptGroup.Key, profGroup.Key);
                        lines.Add(TrainingStatusTreeLine.Group(profGroup.Key, 2, profId, deptId));
                        foreach (var personGroup in profGroup.GroupBy(r => r.Person).OrderBy(g => g.Key))
                        {
                            AddPersonTreeLines(
                                lines,
                                personGroup,
                                personGroup.Key,
                                orgGroup.Key,
                                deptGroup.Key,
                                profId,
                                personLevel: 3,
                                dataLevel: 4,
                                dismissalByEmployee,
                                dismissalByOrgName,
                                professionKey: profGroup.Key,
                                personAssignments: personAssignments,
                                includeProgramRows: includeProgramRows);
                        }
                    }
                }
            }
        }

        return lines;
    }

    private static void AddPersonTreeLines(
        List<TrainingStatusTreeLine> lines,
        IEnumerable<TrainingStatusRow> rows,
        string personName,
        string orgKey,
        string deptKey,
        string parentId,
        int personLevel,
        int dataLevel,
        IReadOnlyDictionary<string, DateTime?> dismissalByEmployee,
        IReadOnlyList<(string Organization, string Name, DateTime? Date)>? dismissalByOrgName = null,
        string? professionKey = null,
        bool isArchive = false,
        IReadOnlySet<string>? transferArchiveKeys = null,
        IReadOnlyDictionary<string, TrainingPersonStandardAssignment>? personAssignments = null,
        bool includeProgramRows = true)
    {
        var personRows = rows.ToList();
        if (personRows.Count == 0)
        {
            return;
        }

        var personId = professionKey is null
            ? NodeId("e", orgKey, deptKey, personName)
            : NodeId("e", orgKey, deptKey, professionKey, personName);
        var first = personRows[0];
        var archiveKey = ArchiveEmployeeKey(orgKey, personName, first.Profession);
        var dismissalDate = isArchive
            ? ResolveDismissalDate(
                dismissalByEmployee,
                dismissalByOrgName,
                orgKey,
                personName,
                first.Profession)
            : (dismissalByEmployee.TryGetValue($"{orgKey}|{personName}", out var byName) ? byName : null);
        var isTransferSnapshot = isArchive
                                 && transferArchiveKeys is not null
                                 && transferArchiveKeys.Contains(archiveKey);
        personAssignments ??= new Dictionary<string, TrainingPersonStandardAssignment>(StringComparer.OrdinalIgnoreCase);
        var assignKey = TrainingReportBuilder.AssignmentKey(first.Organization, personName, first.Profession);
        personAssignments.TryGetValue(assignKey, out var assignment);
        // Явное назначение (в т.ч. пустое = «без стандарта») важнее автоподбора в строках отчёта.
        var displayStandardId = assignment is not null ? assignment.StandardId : first.StandardId;
        var displayStandardName = assignment is not null && string.IsNullOrWhiteSpace(assignment.StandardId)
            ? null
            : first.StandardName;
        var objectNote = assignment?.ObjectNote?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(objectNote))
        {
            objectNote = personRows
                .Select(r => r.ObjectNote)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                ?.Trim() ?? string.Empty;
        }

        lines.Add(TrainingStatusTreeLine.Person(
            personName,
            personLevel,
            personId,
            parentId,
            first.Organization,
            first.Profession,
            displayStandardId,
            displayStandardName,
            dismissalDate,
            isTransferSnapshot,
            objectNote,
            assignment?.Journal ?? false,
            assignment?.Practice ?? false));

        if (!includeProgramRows)
        {
            return;
        }

        foreach (var row in personRows
                     .Where(r => !string.IsNullOrWhiteSpace(r.Program))
                     .OrderBy(r => TrainingProgramDisplayOrder.GetRank(r.Program))
                     .ThenBy(r => r.Program, StringComparer.CurrentCultureIgnoreCase))
        {
            lines.Add(TrainingStatusTreeLine.Data(
                row,
                dataLevel,
                row.ConductId ?? NodeId("prg", row.Organization, row.Person, row.Program),
                personId));
        }

        if (personRows.All(r => string.IsNullOrWhiteSpace(r.Program)))
        {
            lines.Add(TrainingStatusTreeLine.Data(
                first,
                dataLevel,
                NodeId("empty", first.Organization, first.Person),
                personId));
        }
    }

    private static string ArchiveProfessionKey(string? profession)
    {
        var value = (profession ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(value) || value == "—" ? "—" : value;
    }

    private static string ArchiveEmployeeKey(string organization, string person, string? profession) =>
        $"{(organization ?? string.Empty).Trim()}|{(person ?? string.Empty).Trim()}|{ArchiveProfessionKey(profession)}";

    private static string ArchivePersonKey(string organization, string person) =>
        $"{(organization ?? string.Empty).Trim()}|{(person ?? string.Empty).Trim()}";

    private static void RememberDismissalDate(
        IDictionary<string, DateTime?> dismissalByEmployee,
        string organization,
        string person,
        string? profession,
        DateTime? date)
    {
        var key = ArchiveEmployeeKey(organization, person, profession);
        if (!dismissalByEmployee.TryGetValue(key, out var existing) || (existing is null && date is not null))
        {
            dismissalByEmployee[key] = date;
        }

        var personKey = ArchivePersonKey(organization, person);
        if (!dismissalByEmployee.TryGetValue(personKey, out existing) || (existing is null && date is not null))
        {
            dismissalByEmployee[personKey] = date;
        }
    }

    private static DateTime? ResolveDismissalDate(
        IReadOnlyDictionary<string, DateTime?> dismissalByEmployee,
        IReadOnlyList<(string Organization, string Name, DateTime? Date)>? dismissalByOrgName,
        string organization,
        string person,
        string? profession)
    {
        if (dismissalByEmployee.TryGetValue(ArchiveEmployeeKey(organization, person, profession), out var byProfession)
            && byProfession is not null)
        {
            return byProfession;
        }

        if (dismissalByEmployee.TryGetValue(ArchivePersonKey(organization, person), out var byPerson)
            && byPerson is not null)
        {
            return byPerson;
        }

        var prefix = ArchivePersonKey(organization, person) + "|";
        foreach (var pair in dismissalByEmployee)
        {
            if (pair.Value is not null
                && pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        if (dismissalByOrgName is not null)
        {
            foreach (var entry in dismissalByOrgName)
            {
                if (entry.Date is null)
                {
                    continue;
                }

                if (!OrganizationMatchesOrg(entry.Organization, organization)
                    && !string.Equals(entry.Organization, organization, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (EmployeeNameMatcher.Matches(entry.Name, person))
                {
                    return entry.Date;
                }
            }
        }

        return byProfession ?? byPerson;
    }

    private static string NodeId(params string[] parts)
    {
        var key = string.Join("|", parts.Select(p => (p ?? "").Trim().ToLowerInvariant()));
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in key)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash.ToString("x8");
        }
    }
}

public sealed class TrainingStatusTreeLine
{
    public bool IsGroup { get; init; }
    public bool IsPerson { get; init; }
    public int Level { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string? ParentId { get; init; }
    public TrainingStatusRow? Row { get; init; }
    public string Organization { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string? StandardId { get; init; }
    public string? StandardName { get; init; }
    public string ObjectNote { get; init; } = string.Empty;
    public bool Journal { get; init; }
    public bool Practice { get; init; }
    public DateTime? DismissalDate { get; init; }
    public bool IsTransferSnapshot { get; init; }

    public static TrainingStatusTreeLine Group(string label, int level, string id, string? parentId) =>
        new() { IsGroup = true, Level = level, Label = label, Id = id, ParentId = parentId };

    public static TrainingStatusTreeLine Person(
        string label,
        int level,
        string id,
        string? parentId,
        string organization,
        string profession,
        string? standardId,
        string? standardName,
        DateTime? dismissalDate = null,
        bool isTransferSnapshot = false,
        string objectNote = "",
        bool journal = false,
        bool practice = false) =>
        new()
        {
            IsGroup = true,
            IsPerson = true,
            Level = level,
            Label = label,
            Id = id,
            ParentId = parentId,
            Organization = organization,
            Profession = profession,
            StandardId = standardId,
            StandardName = standardName,
            DismissalDate = dismissalDate,
            IsTransferSnapshot = isTransferSnapshot,
            ObjectNote = objectNote ?? string.Empty,
            Journal = journal,
            Practice = practice
        };

    public static TrainingStatusTreeLine Data(TrainingStatusRow row, int level, string id, string? parentId) =>
        new() { IsGroup = false, Level = level, Row = row, Label = row.Program, Id = id, ParentId = parentId };
}

public sealed class TrainingSaveIndexCounter
{
    public int Value { get; set; }
}

public sealed class TrainingProgramRowsPartialModel
{
    public required IReadOnlyList<TrainingStatusTreeLine> Lines { get; init; }
    public required IReadOnlyList<string> ProtocolMovementOptions { get; init; }
    public required IReadOnlyList<string> UpdStatusOptions { get; init; }
    public required TrainingSaveIndexCounter SaveIndex { get; init; }
}

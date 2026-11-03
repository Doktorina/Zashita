using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

[RequireLogin]
public class StatusModel : PageModel
{
    private readonly WorkplaceStore _workplaces;
    private readonly WorkplaceAssignmentStore _assignments;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesStore _employees;
    private readonly OrganizationEmployeesService _employeeService;
    private readonly SoutConditionsClassStore _classes;
    private readonly SoutAnnualPlanStore _annualPlans;

    public StatusModel(
        WorkplaceStore workplaces,
        WorkplaceAssignmentStore assignments,
        OrganizationStore orgs,
        OrganizationEmployeesStore employees,
        OrganizationEmployeesService employeeService,
        SoutConditionsClassStore classes,
        SoutAnnualPlanStore annualPlans)
    {
        _workplaces = workplaces;
        _assignments = assignments;
        _orgs = orgs;
        _employees = employees;
        _employeeService = employeeService;
        _classes = classes;
        _annualPlans = annualPlans;
    }

    [BindProperty]
    public DateTime ReportDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? SelectedOrganization { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Profession { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ConditionsClass { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    /// <summary>all | notconducted | overdue | expiring | assessed | planned</summary>
    [BindProperty(SupportsGet = true)]
    public string? NeedFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? WorkplaceType { get; set; }

    [BindProperty]
    public string? NewName { get; set; }

    [BindProperty]
    public string? NewProfession { get; set; }

    [BindProperty]
    public string? NewDepartment { get; set; }

    [BindProperty]
    public string? NewWorkplaceNumber { get; set; }

    [BindProperty]
    public string? NewConditionsClass { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> OrganizationOptions { get; private set; } = [];
    public IReadOnlyList<string> DepartmentOptions { get; private set; } = [];
    public IReadOnlyList<string> ProfessionOptions { get; private set; } = [];
    public IReadOnlyList<string> ConditionsClassOptions { get; private set; } = [];
    public IReadOnlyList<string> StatusOptions { get; private set; } =
        ["Актуально", "Просрочено", "Требуется внеплановая спецоценка"];
    public IReadOnlyList<SoutStatusTreeLine> TreeLines { get; private set; } = [];
    public IReadOnlyList<SoutCardPickOption> CardOptions { get; private set; } = [];
    public Dictionary<string, string> HrStatusByName { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public int LeafCount { get; private set; }
    public int WorkplaceCount { get; private set; }
    public int PeopleCount { get; private set; }
    public string ActiveNeed { get; private set; } = SoutWorkplaceNeedsBuilder.NeedAll;
    public int NotConductedCount { get; private set; }
    public int OverdueCount { get; private set; }
    public int ExpiringCount { get; private set; }
    public int AssessedCount { get; private set; }
    public int PlannedCount { get; private set; }
    public string? Message { get; private set; }

    public void OnGet()
    {
        Message = TempData["Message"] as string;
        LoadReport();
    }

    public IActionResult OnPostAttachCard(
        string? cardId,
        string? person,
        string? profession,
        string? department,
        string? workplaceIds,
        string? reportDate,
        string? selectedOrganization,
        string? filterDepartment,
        string? filterProfession,
        string? conditionsClass,
        string? statusFilter,
        string? workplaceType)
    {
        Organization = string.IsNullOrWhiteSpace(selectedOrganization)
            ? ResolveOrganization()
            : selectedOrganization.Trim();
        EnsurePersistedWorkplaces(Organization);

        var targetIds = (workplaceIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetIds.Count == 0)
        {
            TempData["Message"] = "Нет рабочих мест для привязки карты.";
            return RedirectToFiltersFromAttach(reportDate, selectedOrganization, filterDepartment, filterProfession, conditionsClass, statusFilter, workplaceType);
        }

        var all = _workplaces.LoadByOrganization(Organization);
        var byId = all.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(cardId))
        {
            var cleared = 0;
            foreach (var id in targetIds)
            {
                if (!byId.TryGetValue(id, out var wp))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(wp.LinkedSoutCardWorkplaceId))
                {
                    continue;
                }

                wp.LinkedSoutCardWorkplaceId = string.Empty;
                wp.ModifiedAt = DateTime.UtcNow;
                cleared++;
            }

            if (cleared > 0)
            {
                _workplaces.SaveForOrganization(Organization, all);
            }

            TempData["Message"] = string.IsNullOrWhiteSpace(person)
                ? "Привязка карты СОУТ снята."
                : $"Карта СОУТ для «{person}» снята.";
            return RedirectToFiltersFromAttach(reportDate, selectedOrganization, filterDepartment, filterProfession, conditionsClass, statusFilter, workplaceType);
        }

        if (!byId.TryGetValue(cardId.Trim(), out var source) || source.IsGroup)
        {
            TempData["Message"] = "Карта СОУТ не найдена.";
            return RedirectToFiltersFromAttach(reportDate, selectedOrganization, filterDepartment, filterProfession, conditionsClass, statusFilter, workplaceType);
        }

        var applied = 0;
        foreach (var id in targetIds)
        {
            if (!byId.TryGetValue(id, out var target) || target.IsGroup)
            {
                continue;
            }

            // Не копируем карту саму на себя — только помечаем привязку.
            if (string.Equals(target.Id, source.Id, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(target.LinkedSoutCardWorkplaceId, source.Id, StringComparison.OrdinalIgnoreCase))
                {
                    target.LinkedSoutCardWorkplaceId = source.Id;
                    target.ModifiedAt = DateTime.UtcNow;
                    applied++;
                }

                continue;
            }

            SoutStatusBuilder.ApplyCardFields(target, source);
            applied++;
        }

        // СНИЛС / сотрудник из отчёта → в карту СОУТ (поле 021)
        if (!string.IsNullOrWhiteSpace(person))
        {
            var staff = _employeeService.Load(Organization);
            var match = staff.FirstOrDefault(e =>
                string.Equals(e.Name?.Trim(), person.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null && !string.IsNullOrWhiteSpace(profession))
            {
                var byProf = staff
                    .Where(e => ProfessionMatcher.Matches(e.Profession, profession))
                    .ToList();
                if (byProf.Count == 1)
                {
                    match = byProf[0];
                }
            }

            if (match is not null)
            {
                SoutStatusBuilder.AttachEmployeeToCard(source, match);
                applied++;
            }
        }

        if (applied > 0)
        {
            _workplaces.SaveForOrganization(Organization, all);
        }

        var who = string.IsNullOrWhiteSpace(person) ? "РМ" : $"«{person}»";
        var cardLabel = string.IsNullOrWhiteSpace(source.SoutReportNumber)
            ? (string.IsNullOrWhiteSpace(source.Profession) ? source.Name : source.Profession)
            : ("№ " + source.SoutReportNumber.Trim());
        TempData["Message"] = applied > 0
            ? $"Карта СОУТ {cardLabel} привязана к {who}: данные обновлены, СНИЛС подставлен в карту."
            : "Изменений нет.";

        return RedirectToFiltersFromAttach(reportDate, selectedOrganization, filterDepartment, filterProfession, conditionsClass, statusFilter, workplaceType);
    }

    private IActionResult RedirectToFiltersFromAttach(
        string? reportDate,
        string? selectedOrganization,
        string? department,
        string? profession,
        string? conditionsClass,
        string? statusFilter,
        string? workplaceType)
    {
        ReportDate = DateTime.Today;
        SelectedOrganization = selectedOrganization;
        Department = department;
        Profession = profession;
        ConditionsClass = conditionsClass;
        StatusFilter = statusFilter;
        WorkplaceType = workplaceType;
        return RedirectToFilters();
    }

    public IActionResult OnPostSave()
    {
        Organization = ResolveOrganization();
        EnsurePersistedWorkplaces(Organization);

        var ids = Request.Form["workplaceId"];
        var names = Request.Form["workplaceName"];
        var numbers = Request.Form["workplaceNumber"];
        var classes = Request.Form["conditionsClass"];
        var conductDates = Request.Form["conductDate"];
        var reportNumbers = Request.Form["reportNumber"];
        var okCodes = Request.Form["okCode"];
        var hasLinkedCards = Request.Form["hasLinkedCard"];
        var declarations = Request.Form["declaration"];
        var unscheduled = Request.Form["unscheduled"];

        var all = _workplaces.LoadByOrganization(Organization);
        var byId = all.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);
        var staff = _employeeService.Load(Organization);
        var changed = 0;

        for (var i = 0; i < ids.Count; i++)
        {
            var id = ids[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(id) || !byId.TryGetValue(id, out var wp))
            {
                continue;
            }

            var name = i < names.Count ? names[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var number = i < numbers.Count ? numbers[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var hasCardFlag = i < hasLinkedCards.Count &&
                              string.Equals(hasLinkedCards[i].ToString()?.Trim(), "1", StringComparison.Ordinal);
            // Поля карты СОУТ редактируются только при выбранной карте — иначе не затираем данные РМ.
            var applyCardFields = hasCardFlag || !string.IsNullOrWhiteSpace(wp.LinkedSoutCardWorkplaceId);

            var cls = applyCardFields && i < classes.Count
                ? classes[i].ToString()?.Trim() ?? string.Empty
                : wp.ConditionsClass ?? string.Empty;
            var dateRaw = applyCardFields && i < conductDates.Count
                ? conductDates[i].ToString()?.Trim()
                : null;
            var num = applyCardFields && i < reportNumbers.Count
                ? reportNumbers[i].ToString()?.Trim() ?? string.Empty
                : wp.SoutReportNumber ?? string.Empty;
            var ok = applyCardFields && i < okCodes.Count
                ? okCodes[i].ToString()?.Trim() ?? string.Empty
                : wp.OkCode ?? string.Empty;

            var decl = applyCardFields
                ? declarations.Any(v => string.Equals(v.ToString(), id, StringComparison.OrdinalIgnoreCase))
                : wp.DeclarationOfConformity;
            var unsched = applyCardFields
                ? unscheduled.Any(v => string.Equals(v.ToString(), id, StringComparison.OrdinalIgnoreCase))
                : wp.RequiresUnscheduledSout;

            DateTime? conduct = wp.SoutAssessmentDate?.Date;
            if (applyCardFields)
            {
                conduct = null;
                if (!string.IsNullOrWhiteSpace(dateRaw))
                {
                    if (DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedInv))
                    {
                        conduct = parsedInv.Date;
                    }
                    else if (DateTime.TryParse(dateRaw, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var parsedRu))
                    {
                        conduct = parsedRu.Date;
                    }
                    else if (DateTime.TryParse(dateRaw, out var parsedAny))
                    {
                        conduct = parsedAny.Date;
                    }
                }
            }

            var dirty =
                (!string.IsNullOrWhiteSpace(name) && !string.Equals(wp.Name, name, StringComparison.Ordinal))
                || !string.Equals(wp.WorkplaceNumber ?? string.Empty, number, StringComparison.Ordinal)
                || (applyCardFields && (
                    !string.Equals(wp.ConditionsClass, cls, StringComparison.Ordinal)
                    || wp.SoutAssessmentDate?.Date != conduct
                    || !string.Equals(wp.SoutReportNumber ?? string.Empty, num, StringComparison.Ordinal)
                    || !string.Equals(wp.OkCode ?? string.Empty, ok, StringComparison.Ordinal)
                    || wp.DeclarationOfConformity != decl
                    || wp.RequiresUnscheduledSout != unsched));

            if (!dirty)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                wp.Name = name;
                if (string.IsNullOrWhiteSpace(wp.Profession))
                {
                    wp.Profession = name;
                }
            }

            wp.WorkplaceNumber = number;
            if (string.IsNullOrWhiteSpace(wp.MainWorkplaceNumber))
            {
                wp.MainWorkplaceNumber = number;
            }

            if (applyCardFields)
            {
                wp.ConditionsClass = cls;
                wp.SoutAssessmentDate = conduct;
                wp.SoutCard ??= new SoutCardDetails();
                // Дата в отчёте и «Дата составления» в карте — одно поле.
                wp.SoutCard.PreparedDate = conduct;
                wp.SoutReportNumber = num;
                wp.OkCode = ok;
                wp.DeclarationOfConformity = decl;
                wp.RequiresUnscheduledSout = unsched;
                if (decl || (!string.IsNullOrWhiteSpace(cls) && conduct.HasValue))
                {
                    wp.SoutAssessmentNotConducted = false;
                }

                if (!string.IsNullOrWhiteSpace(cls))
                {
                    wp.SoutCard.FinalClass = cls;
                }

                // Данные из отчёта → связанная карта СОУТ (+ СНИЛС сотрудника)
                SyncReportFieldsToLinkedCard(wp, byId, staff);
            }

            wp.ModifiedAt = DateTime.UtcNow;
            changed++;
        }

        var attachChanged = ApplyPendingCardAttachments(all, byId, staff);
        changed += attachChanged;

        var famChanged = SaveSoutFamiliarizationsFromForm(byId);
        changed += famChanged;

        if (changed > 0)
        {
            _workplaces.SaveForOrganization(Organization, all);
        }

        TempData["Message"] = changed > 0
            ? $"Сохранено изменений: {changed}. Карты СОУТ обновлены."
            : "Изменений нет.";

        return RedirectToFilters();
    }

    /// <summary>Ознакомление сотрудников с картой СОУТ — select «Ознакомлен / Не ознакомлен».</summary>
    private int SaveSoutFamiliarizationsFromForm(Dictionary<string, WorkplaceRecord> byId)
    {
        var cardIds = Request.Form["attachCardId"];
        var famWorkplaceIds = Request.Form["famWorkplaceId"];
        var employeeKeys = Request.Form["famEmployeeKey"];
        var employeeNames = Request.Form["famEmployeeName"];
        var statuses = Request.Form["famStatus"];
        var workplaceIdsRaw = Request.Form["attachWorkplaceIds"];
        if (famWorkplaceIds.Count == 0 && cardIds.Count == 0)
        {
            return 0;
        }

        var count = Math.Max(famWorkplaceIds.Count, Math.Max(cardIds.Count, employeeKeys.Count));
        var changed = 0;
        for (var i = 0; i < count; i++)
        {
            var key = AtForm(employeeKeys, i);
            var name = AtForm(employeeNames, i);
            if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var familiarized = IsFamiliarizedFormValue(AtForm(statuses, i));
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cardId = AtForm(cardIds, i);
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                targets.Add(cardId);
            }

            var famWp = AtForm(famWorkplaceIds, i);
            if (!string.IsNullOrWhiteSpace(famWp))
            {
                targets.Add(famWp);
            }

            foreach (var id in AtForm(workplaceIdsRaw, i)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    targets.Add(id);
                }
            }

            foreach (var workplaceId in targets)
            {
                if (!byId.TryGetValue(workplaceId, out var workplace) || workplace.IsGroup)
                {
                    continue;
                }

                var map = (workplace.SoutFamiliarizations ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x.EmployeeKey) || !string.IsNullOrWhiteSpace(x.EmployeeName))
                    .GroupBy(
                        x => string.IsNullOrWhiteSpace(x.EmployeeKey) ? "name:" + x.EmployeeName : x.EmployeeKey,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

                var mapKey = string.IsNullOrWhiteSpace(key) ? "name:" + name : key;
                var previous = map.TryGetValue(mapKey, out var existing) ? existing : null;
                var entry = new RiskCardFamiliarization
                {
                    EmployeeKey = key,
                    EmployeeName = name,
                    IsFamiliarized = familiarized
                };

                if (previous is null && !familiarized)
                {
                    continue;
                }

                if (previous is not null
                    && previous.IsFamiliarized == familiarized
                    && string.Equals(previous.EmployeeName, name, StringComparison.Ordinal)
                    && string.Equals(previous.EmployeeKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                map[mapKey] = entry;
                workplace.SoutFamiliarizations = map.Values.ToList();
                workplace.ModifiedAt = DateTime.UtcNow;
                changed++;
            }
        }

        return changed;
    }

    private static string AtForm(Microsoft.Extensions.Primitives.StringValues values, int index)
        => index < values.Count ? values[index].ToString()?.Trim() ?? string.Empty : string.Empty;

    private static bool IsFamiliarizedFormValue(string? raw)
        => string.Equals(raw, "1", StringComparison.Ordinal)
           || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(raw, "ознакомлен", StringComparison.OrdinalIgnoreCase);

    /// <summary>Привязка карт из выпадающих списков — только по кнопке «Сохранить».</summary>
    private int ApplyPendingCardAttachments(
        List<WorkplaceRecord> all,
        Dictionary<string, WorkplaceRecord> byId,
        IReadOnlyList<OrganizationEmployee> staff)
    {
        var cardIds = Request.Form["attachCardId"];
        var persons = Request.Form["attachPerson"];
        var professions = Request.Form["attachProfession"];
        var workplaceIdsRaw = Request.Form["attachWorkplaceIds"];
        if (cardIds.Count == 0)
        {
            return 0;
        }

        // Сначала собираем намерения: иначе пустой «без карты» у соседа по тому же РМ
        // затирает только что выбранную карту другого сотрудника.
        var intents = new List<(string CardId, string Person, string Profession, List<string> TargetIds)>();
        var claimedByAttach = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cardIds.Count; i++)
        {
            var cardId = cardIds[i].ToString()?.Trim() ?? string.Empty;
            var person = i < persons.Count ? persons[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var profession = i < professions.Count ? professions[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var targetIds = (i < workplaceIdsRaw.Count ? workplaceIdsRaw[i].ToString() : string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id => byId.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            intents.Add((cardId, person, profession, targetIds));
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                foreach (var id in targetIds)
                {
                    claimedByAttach.Add(id);
                }
            }
        }

        var changed = 0;
        foreach (var (cardId, person, profession, targetIds) in intents)
        {
            if (string.IsNullOrWhiteSpace(cardId))
            {
                foreach (var id in targetIds)
                {
                    // Не снимаем привязку с РМ, если другой сотрудник в этом же POST выбрал карту.
                    if (claimedByAttach.Contains(id))
                    {
                        continue;
                    }

                    if (!byId.TryGetValue(id, out var wp) || string.IsNullOrWhiteSpace(wp.LinkedSoutCardWorkplaceId))
                    {
                        continue;
                    }

                    wp.LinkedSoutCardWorkplaceId = string.Empty;
                    wp.ModifiedAt = DateTime.UtcNow;
                    changed++;
                }

                continue;
            }

            if (!byId.TryGetValue(cardId, out var source) || source.IsGroup)
            {
                continue;
            }

            var appliedToAny = false;
            foreach (var id in targetIds)
            {
                if (!byId.TryGetValue(id, out var target) || target.IsGroup)
                {
                    continue;
                }

                if (string.Equals(target.Id, source.Id, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(target.LinkedSoutCardWorkplaceId, source.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        target.LinkedSoutCardWorkplaceId = source.Id;
                        target.ModifiedAt = DateTime.UtcNow;
                        changed++;
                    }

                    appliedToAny = true;
                    continue;
                }

                SoutStatusBuilder.ApplyCardFields(target, source);
                appliedToAny = true;
                changed++;
            }

            var match = FindStaffForAttach(staff, person, profession);
            if (match is not null)
            {
                // Нет своего РМ в справочнике — достаточно привязать сотрудника к карте (стр. 021);
                // в следующем отчёте строка подтянется через EmployeeIds карты.
                if (!appliedToAny)
                {
                    SoutStatusBuilder.AttachEmployeeToCard(source, match);
                    if (string.IsNullOrWhiteSpace(source.LinkedSoutCardWorkplaceId))
                    {
                        source.LinkedSoutCardWorkplaceId = source.Id;
                    }

                    source.ModifiedAt = DateTime.UtcNow;
                    changed++;
                }
                else
                {
                    SoutStatusBuilder.AttachEmployeeToCard(source, match);
                    changed++;
                }
            }
        }

        return changed;
    }

    private static OrganizationEmployee? FindStaffForAttach(
        IReadOnlyList<OrganizationEmployee> staff,
        string person,
        string profession)
    {
        if (!string.IsNullOrWhiteSpace(person))
        {
            var byName = staff.FirstOrDefault(e =>
                string.Equals(e.Name?.Trim(), person, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        if (string.IsNullOrWhiteSpace(profession))
        {
            return null;
        }

        var byProf = staff
            .Where(e => ProfessionMatcher.Matches(e.Profession, profession))
            .ToList();
        return byProf.Count == 1 ? byProf[0] : null;
    }

    private void SyncReportFieldsToLinkedCard(
        WorkplaceRecord wp,
        IReadOnlyDictionary<string, WorkplaceRecord> byId,
        IReadOnlyList<OrganizationEmployee> staff)
    {
        var linkedId = wp.LinkedSoutCardWorkplaceId?.Trim() ?? string.Empty;
        WorkplaceRecord? card = null;
        if (!string.IsNullOrWhiteSpace(linkedId)
            && !string.Equals(linkedId, wp.Id, StringComparison.OrdinalIgnoreCase)
            && byId.TryGetValue(linkedId, out var linked))
        {
            card = linked;
        }
        else if (string.IsNullOrWhiteSpace(linkedId)
                 || string.Equals(linkedId, wp.Id, StringComparison.OrdinalIgnoreCase))
        {
            // Редактируем саму карту в строке отчёта.
            card = wp;
        }

        if (card is null)
        {
            return;
        }

        if (!ReferenceEquals(card, wp))
        {
            card.SoutReportNumber = wp.SoutReportNumber ?? string.Empty;
            card.OkCode = wp.OkCode ?? string.Empty;
            card.SoutAssessmentDate = wp.SoutAssessmentDate;
            card.ConditionsClass = wp.ConditionsClass ?? string.Empty;
            card.DeclarationOfConformity = wp.DeclarationOfConformity;
            card.RequiresUnscheduledSout = wp.RequiresUnscheduledSout;
            card.SoutAssessmentNotConducted = wp.SoutAssessmentNotConducted;
            if (wp.DeclarationOfConformity)
            {
                card.SoutAssessmentNotConducted = false;
            }

            card.WorkplaceNumber = string.IsNullOrWhiteSpace(wp.WorkplaceNumber)
                ? card.WorkplaceNumber
                : wp.WorkplaceNumber;
            card.SoutCard ??= new SoutCardDetails();
            card.SoutCard.PreparedDate = wp.SoutAssessmentDate;
            card.SoutCard.FinalClass = wp.ConditionsClass ?? string.Empty;
            SoutCardPrintBuilder.SyncAssessmentDates(card);
            card.ModifiedAt = DateTime.UtcNow;
        }

        // СНИЛС в карту (021): уже привязанные + единственный сотрудник по профессии.
        foreach (var empId in wp.SoutCard?.EmployeeIds ?? [])
        {
            var emp = staff.FirstOrDefault(e =>
                string.Equals(e.Id, empId, StringComparison.OrdinalIgnoreCase));
            if (emp is not null)
            {
                SoutStatusBuilder.AttachEmployeeToCard(card, emp);
            }
        }

        var prof = string.IsNullOrWhiteSpace(wp.Profession) ? wp.Name : wp.Profession;
        if (!string.IsNullOrWhiteSpace(prof))
        {
            var byProf = staff
                .Where(e => e.IsActive && ProfessionMatcher.Matches(e.Profession, prof))
                .ToList();
            if (byProf.Count == 1)
            {
                SoutStatusBuilder.AttachEmployeeToCard(card, byProf[0]);
            }
        }
    }

    public IActionResult OnPostAdd()
    {
        Organization = ResolveOrganization();
        EnsurePersistedWorkplaces(Organization);

        var profession = string.IsNullOrWhiteSpace(NewProfession) ? NewName : NewProfession;
        if (string.IsNullOrWhiteSpace(profession) && string.IsNullOrWhiteSpace(NewName))
        {
            TempData["Message"] = "Укажите профессию или наименование рабочего места (из штата 1С или вручную).";
            return RedirectToFilters();
        }

        SoutWorkplaceStaffSync.AddWorkplace(
            Organization,
            _workplaces,
            NewName ?? string.Empty,
            profession ?? string.Empty,
            NewDepartment ?? string.Empty,
            NewWorkplaceNumber,
            NewConditionsClass);

        TempData["Message"] = "Рабочее место добавлено. Данные можно редактировать в таблице.";
        return RedirectToFilters();
    }

    public IActionResult OnPostMarkNotConducted(string? workplaceId)
    {
        Organization = ResolveOrganization();
        if (string.IsNullOrWhiteSpace(workplaceId))
        {
            TempData["Message"] = "Не указано рабочее место.";
            return RedirectToFilters();
        }

        EnsurePersistedWorkplaces(Organization);
        var all = _workplaces.LoadByOrganization(Organization);
        var wp = all.FirstOrDefault(w => string.Equals(w.Id, workplaceId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (wp is null || wp.IsGroup)
        {
            TempData["Message"] = "Рабочее место не найдено.";
            return RedirectToFilters();
        }

        wp.SoutAssessmentNotConducted = true;
        wp.SoutAssessmentDate = null;
        wp.SoutReportNumber = string.Empty;
        wp.ConditionsClass = string.Empty;
        wp.DeclarationOfConformity = false;
        wp.RequiresUnscheduledSout = false;
        wp.SoutCard ??= new SoutCardDetails();
        wp.SoutCard.PreparedDate = null;
        wp.SoutCard.FinalClass = string.Empty;
        wp.ModifiedAt = DateTime.UtcNow;

        // Если это привязанная карта — синхронизируем и её.
        if (!string.IsNullOrWhiteSpace(wp.LinkedSoutCardWorkplaceId)
            && !string.Equals(wp.LinkedSoutCardWorkplaceId, wp.Id, StringComparison.OrdinalIgnoreCase))
        {
            var card = all.FirstOrDefault(w =>
                string.Equals(w.Id, wp.LinkedSoutCardWorkplaceId, StringComparison.OrdinalIgnoreCase));
            if (card is not null)
            {
                card.SoutAssessmentNotConducted = true;
                card.SoutAssessmentDate = null;
                card.SoutReportNumber = string.Empty;
                card.ConditionsClass = string.Empty;
                card.DeclarationOfConformity = false;
                card.RequiresUnscheduledSout = false;
                card.SoutCard ??= new SoutCardDetails();
                card.SoutCard.PreparedDate = null;
                card.SoutCard.FinalClass = string.Empty;
                card.ModifiedAt = DateTime.UtcNow;
            }
        }

        _workplaces.SaveForOrganization(Organization, all);
        TempData["Message"] = $"«{wp.Name}»: СОУТ не проведена.";
        return RedirectToFilters();
    }

    public IActionResult OnPostSyncFromStaff()
    {
        Organization = ResolveOrganization();
        EnsurePersistedWorkplaces(Organization);
        var staff = _employeeService.LoadForReport(Organization);
        SoutWorkplaceStaffSync.CollapseBlankProfessionShells(Organization, _workplaces);
        var added = SoutWorkplaceStaffSync.SyncMissingFromStaff(Organization, staff, _workplaces);
        SoutWorkplaceStaffSync.CollapseBlankProfessionShells(Organization, _workplaces);
        TempData["Message"] = added > 0
            ? $"Из штата 1С добавлено рабочих мест: {added} (по одной на профессию)."
            : "Новых профессий из штата нет — все уже есть в списке, либо штат пуст.";
        return RedirectToFilters();
    }

    private void LoadReport()
    {
        ReportDate = DateTime.Today;
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Отчет по СОУТ";

        OrganizationOptions = _orgs.GetVisibleOrganizations(HttpContext);
        string? selectedOrganization = null;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selectedOrganization, allowAll: true);
        SelectedOrganization = selectedOrganization;
        Organization = string.IsNullOrWhiteSpace(SelectedOrganization)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : SelectedOrganization;
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(SelectedOrganization) ? "Все организации" : Organization;
        ViewData["Organization"] = ViewData["Subtitle"];

        var orgsToLoad = string.IsNullOrWhiteSpace(SelectedOrganization)
            ? _employeeService.ResolveOrganizationsForAllReports(
                OrganizationOptions,
                _orgs.IsOrganizationHidden).ToList()
            : [Organization];

        foreach (var org in orgsToLoad)
        {
            EnsurePersistedWorkplaces(org);
        }

        ConditionsClassOptions = _classes.Load()
            .Where(c => c.Contains("класс", StringComparison.OrdinalIgnoreCase) || c.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staffPairs = _employeeService.LoadPairsForMedReport(orgsToLoad).ToList();
        var staff = staffPairs.Select(p => p.Employee).ToList();
        HrStatusByName = EmployeeHrStatus.BuildMap(staff);
        var workplaces = orgsToLoad.SelectMany(org => _workplaces.LoadTreeByOrganization(org)).ToList();
        foreach (var org in orgsToLoad)
        {
            var orgList = _workplaces.LoadByOrganization(org);
            var synced = false;
            foreach (var wp in orgList.Where(w => !w.IsGroup))
            {
                if (SoutCardPrintBuilder.SyncAssessmentDates(wp))
                {
                    wp.ModifiedAt = DateTime.UtcNow;
                    synced = true;
                }
            }

            if (synced)
            {
                _workplaces.SaveForOrganization(org, orgList);
            }
        }

        workplaces = orgsToLoad.SelectMany(org => _workplaces.LoadTreeByOrganization(org)).ToList();
        ActiveNeed = SoutWorkplaceNeedsBuilder.NormalizeNeedFilter(NeedFilter);
        var today = ReportDate.Date;
        var annualPlans = _annualPlans.LoadAll();
        foreach (var org in orgsToLoad)
        {
            var orgWorkplaces = _workplaces.LoadTreeByOrganization(org);
            var plannedIds = SoutWorkplaceNeedsBuilder.ResolvePlannedWorkplaceIds(annualPlans, org, today.Year);
            NotConductedCount += SoutWorkplaceNeedsBuilder.CountByBucket(
                orgWorkplaces, org, SoutWorkplaceNeedsBuilder.NeedNotConducted, today, plannedIds);
            OverdueCount += SoutWorkplaceNeedsBuilder.CountByBucket(
                orgWorkplaces, org, SoutWorkplaceNeedsBuilder.NeedOverdue, today, plannedIds);
            ExpiringCount += SoutWorkplaceNeedsBuilder.CountByBucket(
                orgWorkplaces, org, SoutWorkplaceNeedsBuilder.NeedExpiring, today, plannedIds);
            AssessedCount += SoutWorkplaceNeedsBuilder.CountByBucket(
                orgWorkplaces, org, SoutWorkplaceNeedsBuilder.NeedAssessed, today, plannedIds);
            PlannedCount += SoutWorkplaceNeedsBuilder.CountByBucket(
                orgWorkplaces, org, SoutWorkplaceNeedsBuilder.NeedPlanned, today, plannedIds);
        }

        ProfessionOptions = staff
            .Select(e => ProfessionMatcher.Normalize(e.Profession))
            .Concat(workplaces
                .Where(w => !w.IsGroup)
                .Select(w => ProfessionMatcher.Normalize(
                    string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession)))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        DepartmentOptions = workplaces
            .Where(w => !string.IsNullOrWhiteSpace(w.Department))
            .Select(w => w.Department.Trim())
            .Concat(staff.Select(e => e.Department?.Trim() ?? string.Empty))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var lines = new List<SoutStatusTreeLine>();
        var cards = new List<SoutCardPickOption>();
        foreach (var org in orgsToLoad)
        {
            var assignments = _assignments.LoadDocumentsByOrganization(org);
            var employees = _employeeService.Load(org);
            var orgWorkplaces = _workplaces.LoadTreeByOrganization(org);
            var plannedIds = SoutWorkplaceNeedsBuilder.ResolvePlannedWorkplaceIds(annualPlans, org, today.Year);
            lines.AddRange(SoutStatusBuilder.BuildTree(
                orgWorkplaces,
                assignments,
                employees,
                org,
                ReportDate,
                Department,
                ConditionsClass,
                StatusFilter,
                WorkplaceType,
                Profession,
                ActiveNeed,
                plannedIds));
            cards.AddRange(SoutStatusBuilder.BuildCardOptions(orgWorkplaces, org));
        }

        TreeLines = lines;
        CardOptions = cards
            .GroupBy(c => c.WorkplaceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        LeafCount = TreeLines.Count(l => !l.IsGroup && !l.IsPerson && l.Row is not null);
        WorkplaceCount = LeafCount;
        // Штат 1С человек×ЮЛ (как МО/обучение/риски), не узлы дерева после фильтров СОУТ.
        PeopleCount = StaffHeadcount.CountPersonOrgCards(
            staffPairs
                .Where(p => string.IsNullOrWhiteSpace(Department)
                            || (p.Employee.Department ?? string.Empty)
                                .Contains(Department, StringComparison.OrdinalIgnoreCase))
                .Where(p => string.IsNullOrWhiteSpace(Profession)
                            || ProfessionMatcher.Matches(Profession, p.Employee.Profession)
                            || (p.Employee.Profession ?? string.Empty)
                                .Contains(Profession, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Список РМ пустой, пока не загружен из штата 1С — демо не подставляем.
    /// Пустые дубли одной профессии схлопываем при каждом открытии отчёта.
    /// </summary>
    private void EnsurePersistedWorkplaces(string organization)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return;
        }

        var staff = _employeeService.LoadForReport(organization);
        // Пустые оболочки убираем; несколько заполненных (импорт Word / вручную) не трогаем.
        _ = SoutWorkplaceStaffSync.CollapseBlankProfessionShells(organization, _workplaces);
        _ = SoutWorkplaceStaffSync.SyncMissingFromStaff(organization, staff, _workplaces);
        _ = SoutWorkplaceStaffSync.CollapseBlankProfessionShells(organization, _workplaces);
        _ = SoutWorkplaceStaffSync.SyncWorkerCountsFromStaff(organization, staff, _workplaces);
    }

    private IActionResult RedirectToFilters()
    {
        var need = SoutWorkplaceNeedsBuilder.NormalizeNeedFilter(NeedFilter);
        return RedirectToPage(new
        {
            reportDate = ReportDate.ToString("yyyy-MM-dd"),
            selectedOrganization = SelectedOrganization,
            department = Department,
            profession = Profession,
            conditionsClass = ConditionsClass,
            statusFilter = StatusFilter,
            workplaceType = WorkplaceType,
            needFilter = need == SoutWorkplaceNeedsBuilder.NeedAll ? null : need
        });
    }

    private string ResolveOrganization()
    {
        // Берём организацию из формы (скрытое поле), иначе из сессии.
        string? selected = SelectedOrganization;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selected, allowAll: true);
        SelectedOrganization = selected;
        return string.IsNullOrWhiteSpace(selected)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : selected;
    }
}

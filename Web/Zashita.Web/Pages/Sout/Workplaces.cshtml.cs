using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

[RequireLogin]
public class WorkplacesModel : PageModel
{
    private readonly WorkplaceStore _store;
    private readonly WorkplaceAssignmentStore _assignments;
    private readonly SoutPreparationStore _preparation;
    private readonly SoutAnnualPlanStore _annualPlans;
    private readonly OrganizationStore _orgs;
    private readonly SoutConditionsClassStore _classes;
    private readonly OrganizationEmployeesService _employees;

    public WorkplacesModel(
        WorkplaceStore store,
        WorkplaceAssignmentStore assignments,
        SoutPreparationStore preparation,
        SoutAnnualPlanStore annualPlans,
        OrganizationStore orgs,
        SoutConditionsClassStore classes,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _assignments = assignments;
        _preparation = preparation;
        _annualPlans = annualPlans;
        _orgs = orgs;
        _classes = classes;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> ConditionsClassOptions { get; private set; } = [];
    public List<WorkplaceRecord> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<WorkplaceRecord>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public List<WorkplaceRecord> DepartmentGroups { get; private set; } = [];
    public IReadOnlyList<string> ProfessionOptions { get; private set; } = [];
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)]
    public bool UsedOnly { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public string? DepartmentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? View { get; set; }

    public bool IsAssessmentView =>
        string.Equals(View, "assessment", StringComparison.OrdinalIgnoreCase);

    public bool IsCardsView =>
        string.Equals(View, "cards", StringComparison.OrdinalIgnoreCase);

    public IActionResult OnGet()
    {
        // Устаревший режим «Оценка рабочих мест по условиям труда» больше не используется.
        if (IsAssessmentView)
        {
            return RedirectToPage("/Sout/Workplaces");
        }

        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SoutOrganizationBootstrap.Ensure(Organization, _store, _assignments, _preparation, _annualPlans);
        ConditionsClassOptions = _classes.Load();
        LoadPage();
        return Page();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ConditionsClassOptions = _classes.Load();
        var savingCardsView = string.Equals(Request.Form["View"].ToString(), "cards", StringComparison.OrdinalIgnoreCase);
        var existingById = _store.LoadByOrganization(Organization)
            .ToDictionary(x => x.Id, StringComparer.Ordinal);
        var groupsById = existingById.Values
            .Where(x => x.IsGroup)
            .ToDictionary(x => x.Id, StringComparer.Ordinal);
        var list = new List<WorkplaceRecord>();
        var names = Request.Form["Name"];
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var workplaceNumbers = Request.Form["WorkplaceNumber"];
        var mainNumbers = Request.Form["MainWorkplaceNumber"];
        var conditionsClasses = Request.Form["ConditionsClass"];
        var professions = Request.Form["Profession"];
        var workerCounts = Request.Form["WorkerCount"];
        var okCodes = Request.Form["OkCode"];
        var departments = Request.Form["Department"];
        var isUseds = Request.Form["IsUsed"];
        var deletes = Request.Form["Delete"];
        var trafficMarks = Request.Form["SoutTrafficMark"];
        var declarations = Request.Form["DeclarationOfConformity"];
        var manualFlags = Request.Form["IsManualSoutCard"];

        for (var i = 0; i < names.Count; i++)
        {
            if (TrainingFormHelpers.IsMarkedForDelete(deletes, i))
            {
                continue;
            }

            var name = names[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var isGroup = i < isGroups.Count && isGroups[i].ToString() == "true";
            _ = int.TryParse(i < workerCounts.Count ? workerCounts[i].ToString() : "0", out var workerCount);
            var department = i < departments.Count ? departments[i].ToString()?.Trim() ?? "" : "";
            var profession = i < professions.Count ? professions[i].ToString()?.Trim() ?? "" : "";
            var isUsed = i < isUseds.Count && isUseds[i].ToString() == "true";
            var parentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null);
            var id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i]) ? ids[i].ToString()! : Guid.NewGuid().ToString("N");
            existingById.TryGetValue(id, out var existing);
            var trafficMark = savingCardsView && i < trafficMarks.Count
                ? SoutStatusBuilder.NormalizeTrafficMark(trafficMarks[i].ToString())
                : SoutStatusBuilder.NormalizeTrafficMark(existing?.SoutTrafficMark);

            var declaration = savingCardsView && i < declarations.Count
                ? string.Equals(declarations[i].ToString(), "true", StringComparison.OrdinalIgnoreCase)
                : existing?.DeclarationOfConformity ?? false;

            if (!isGroup)
            {
                if (string.IsNullOrWhiteSpace(profession))
                {
                    profession = name;
                }
                else if (string.IsNullOrWhiteSpace(name))
                {
                    name = profession;
                }

                if (string.IsNullOrWhiteSpace(department)
                    && !string.IsNullOrWhiteSpace(parentId)
                    && groupsById.TryGetValue(parentId, out var parentGroup))
                {
                    department = string.IsNullOrWhiteSpace(parentGroup.Department)
                        ? parentGroup.Name
                        : parentGroup.Department;
                }

                if (savingCardsView)
                {
                    isUsed = true;
                }
            }

            var conditionsClass = i < conditionsClasses.Count ? conditionsClasses[i].ToString()?.Trim() ?? "" : "";
            var isManualSoutCard = existing?.IsManualSoutCard == true
                || (i < manualFlags.Count
                    && string.Equals(manualFlags[i].ToString(), "true", StringComparison.OrdinalIgnoreCase));
            // Новая строка в «Карты СОУТ» — всегда ручная (не оболочка из 1С).
            if (savingCardsView && !isGroup && existing is null)
            {
                isManualSoutCard = true;
            }

            // Пустая карта (нет даты / №) — класс «—», кроме ручной новой: сохраняем выбранный класс.
            if (savingCardsView && !isGroup && !isManualSoutCard
                && SoutStatusBuilder.IsSoutCardBlank(new WorkplaceRecord
                {
                    IsGroup = false,
                    SoutAssessmentDate = existing?.SoutAssessmentDate,
                    SoutReportNumber = existing?.SoutReportNumber ?? "",
                    SoutCard = existing?.SoutCard
                }))
            {
                conditionsClass = string.Empty;
            }

            var createdAt = existing is not null && existing.CreatedAt != default
                ? existing.CreatedAt
                : DateTime.UtcNow;

            var record = new WorkplaceRecord
            {
                Id = id,
                ParentId = parentId,
                IsGroup = isGroup,
                Name = name,
                WorkplaceNumber = i < workplaceNumbers.Count ? workplaceNumbers[i].ToString()?.Trim() ?? "" : "",
                MainWorkplaceNumber = i < mainNumbers.Count ? mainNumbers[i].ToString()?.Trim() ?? "" : "",
                ConditionsClass = conditionsClass,
                Profession = profession,
                WorkerCount = workerCount < 0 ? 0 : workerCount,
                PlanYear = isGroup
                    ? string.Empty
                    : SoutStatusBuilder.ResolvePlanYearDisplay(new WorkplaceRecord
                    {
                        IsGroup = false,
                        ConditionsClass = conditionsClass,
                        DeclarationOfConformity = declaration,
                        RequiresUnscheduledSout = existing?.RequiresUnscheduledSout ?? false,
                        SoutAssessmentNotConducted = existing?.SoutAssessmentNotConducted ?? false,
                        SoutAssessmentDate = existing?.SoutAssessmentDate
                    }),
                OkCode = i < okCodes.Count ? okCodes[i].ToString()?.Trim() ?? "" : "",
                Department = string.IsNullOrWhiteSpace(department) && isGroup ? name : department,
                IsUsed = isUsed,
                RiskAssessmentDate = existing?.RiskAssessmentDate,
                RiskCardNumber = existing?.RiskCardNumber ?? "",
                OverallRisk = existing?.OverallRisk ?? "",
                RiskPeriodicity = existing?.RiskPeriodicity ?? "",
                RiskAssessmentNotConducted = existing?.RiskAssessmentNotConducted ?? false,
                RiskLines = existing?.RiskLines ?? [],
                SoutAssessmentDate = existing?.SoutAssessmentDate,
                SoutReportNumber = existing?.SoutReportNumber ?? "",
                LinkedSoutCardWorkplaceId = existing?.LinkedSoutCardWorkplaceId ?? "",
                SoutCard = existing?.SoutCard ?? new SoutCardDetails(),
                DeclarationOfConformity = declaration,
                SoutAssessmentNotConducted = trafficMark == "green"
                    ? false
                    : existing?.SoutAssessmentNotConducted ?? false,
                // «Проведена» снимает внеплановую — иначе сводка держит РМ в «просрочена».
                RequiresUnscheduledSout = trafficMark == "green"
                    ? false
                    : existing?.RequiresUnscheduledSout ?? false,
                SoutTrafficMark = isGroup ? string.Empty : trafficMark,
                IsManualSoutCard = !isGroup && isManualSoutCard,
                RiskFamiliarizations = existing?.RiskFamiliarizations ?? [],
                SoutFamiliarizations = existing?.SoutFamiliarizations ?? [],
                PrintOverrides = existing?.PrintOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                CreatedAt = createdAt,
                ModifiedAt = DateTime.UtcNow
            };

            if (isGroup)
            {
                groupsById[id] = record;
            }

            list.Add(record);
        }

        _store.SaveForOrganization(Organization, list);
        Message = savingCardsView ? "Карты СОУТ сохранены." : "Рабочие места сохранены.";
        if (string.Equals(Request.Form["View"].ToString(), "cards", StringComparison.OrdinalIgnoreCase))
        {
            View = "cards";
        }
        else if (string.Equals(Request.Form["View"].ToString(), "assessment", StringComparison.OrdinalIgnoreCase))
        {
            View = "assessment";
        }

        LoadPage();
        return Page();
    }

    public IActionResult OnPostSyncFromStaff()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SoutOrganizationBootstrap.Ensure(Organization, _store, _assignments, _preparation, _annualPlans);
        var staff = _employees.LoadForReport(Organization);
        if (staff.Count == 0)
        {
            Message = "Штат 1С пуст — сначала загрузите сотрудников организации.";
            ConditionsClassOptions = _classes.Load();
            LoadPage();
            return Page();
        }

        var count = SoutWorkplaceStaffSync.ReplaceFromStaff(Organization, staff, _store);
        Message = count > 0
            ? $"Перечень очищен и загружен из штата 1С: {count} карт/РМ (по одной на профессию; доп. импортированные карты сохранены)."
            : "В штате 1С нет сотрудников с заполненной профессией — перечень очищен.";
        ConditionsClassOptions = _classes.Load();
        if (string.Equals(Request.Form["View"].ToString(), "cards", StringComparison.OrdinalIgnoreCase))
        {
            View = "cards";
        }

        LoadPage();
        return Page();
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsCardsView
            ? "Карты СОУТ"
            : IsAssessmentView
                ? "Оценка рабочих мест по условиям труда"
                : "Рабочие места";
        ViewData["Subtitle"] = Organization;

        // Сначала синк/схлопывание пустых дублей по профессии, потом читаем дерево.
        MaybeSyncFromStaff();

        Nodes = _store.LoadTreeByOrganization(Organization);

        // ИП — только в обучении: не показываем карту/РМ «Индивидуальный предприниматель».
        Nodes = Nodes
            .Where(n => n.IsGroup || !RiskPrintChrome.IsIndividualEntrepreneurWorkplace(n))
            .ToList();

        // Плановый год — из логики отчёта по СОУТ (дата + 5 лет / декларация / внеплановая).
        var planYearChanged = false;
        var staff = _employees.LoadForReport(Organization);
        foreach (var node in Nodes.Where(n => !n.IsGroup))
        {
            var computed = SoutStatusBuilder.ResolvePlanYearDisplay(node);
            if (!string.Equals(node.PlanYear, computed, StringComparison.Ordinal))
            {
                node.PlanYear = computed;
                planYearChanged = true;
            }
        }

        if (planYearChanged)
        {
            _store.SaveForOrganization(Organization, Nodes);
        }

        ProfessionOptions = staff
            .Select(e => ProfessionMatcher.Normalize(e.Profession))
            .Concat(Nodes
                .Where(w => !w.IsGroup)
                .Select(w => ProfessionMatcher.Normalize(
                    string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession)))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Where(p => !RiskPrintChrome.IsIndividualEntrepreneurProfessionLabel(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Nodes)
            {
                if (MatchesQuery(node, query))
                {
                    matchedIds.Add(node.Id);
                    AddAncestors(node, matchedIds);
                }
            }

            Nodes = Nodes.Where(node => matchedIds.Contains(node.Id)).ToList();
        }

        if (UsedOnly)
        {
            Nodes = Nodes.Where(node => node.IsGroup || node.IsUsed).ToList();
        }

        // DepartmentId только подсвечивает/прокручивает в UI — перечень подразделений не сворачиваем.
        DepartmentGroups = SoutWorkplaceDepartmentConsolidator.DistinctRootDepartmentGroups(Nodes);
    }

    private static DateTime _lastStaffSyncUtc = DateTime.MinValue;
    private static DateTime _lastWorkerCountSyncUtc = DateTime.MinValue;
    private static readonly object StaffSyncGate = new();

    private void MaybeSyncFromStaff()
    {
        if (string.IsNullOrWhiteSpace(Organization))
        {
            return;
        }

        lock (StaffSyncGate)
        {
            var now = DateTime.UtcNow;
            var staff = _employees.LoadForReport(Organization);
            // Сначала убрать пустые дубли профессии, потом добрать только отсутствующие профессии.
            SoutWorkplaceStaffSync.CollapseBlankProfessionShells(Organization, _store);
            // Из штата — строго 1 карта на профессию; доп. только импорт Word / вручную.
            var added = SoutWorkplaceStaffSync.SyncMissingFromStaff(Organization, staff, _store);
            // Ещё раз: SyncMissing мог создать оболочку при рассинхроне подразделения — убрать.
            SoutWorkplaceStaffSync.CollapseBlankProfessionShells(Organization, _store);

            var updated = 0;
            if (now - _lastWorkerCountSyncUtc >= TimeSpan.FromHours(6) || added > 0)
            {
                updated = SoutWorkplaceStaffSync.SyncWorkerCountsFromStaff(Organization, staff, _store);
                _lastWorkerCountSyncUtc = now;
            }

            _lastStaffSyncUtc = now;
            if (string.IsNullOrWhiteSpace(Message) && (added > 0 || updated > 0))
            {
                Message = added > 0
                    ? $"Из штата 1С добавлено карт/РМ: {added} (по одной на профессию)."
                    : "Количество работников обновлено из штата 1С.";
            }
        }
    }

    private static bool MatchesQuery(WorkplaceRecord node, string query) =>
        node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.WorkplaceNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.Profession.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.Department.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void AddAncestors(WorkplaceRecord node, HashSet<string> matchedIds)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId))
        {
            return;
        }

        var parent = Nodes.FirstOrDefault(item => item.Id == node.ParentId);
        if (parent is null || !matchedIds.Add(parent.Id))
        {
            return;
        }

        AddAncestors(parent, matchedIds);
    }
}

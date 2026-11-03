using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Reports;

[RequireLogin]
public class StatusModel : PageModel
{
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly RiskHazardAssessmentStore _hazards;
    private readonly RiskHazardCatalogStore _catalog;
    private readonly RiskWorkplaceStore _workplaces;
    private readonly RiskAssessmentObjectStore _objects;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public StatusModel(
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        RiskHazardAssessmentStore hazards,
        RiskHazardCatalogStore catalog,
        RiskWorkplaceStore workplaces,
        RiskAssessmentObjectStore objects,
        RisksWorkspaceContext risksWorkspace)
    {
        _orgs = orgs;
        _employees = employees;
        _hazards = hazards;
        _catalog = catalog;
        _workplaces = workplaces;
        _objects = objects;
        _risksWorkspace = risksWorkspace;
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
    public string? MeasureStatus { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ObjectId { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> OrganizationOptions { get; private set; } = [];
    public IReadOnlyList<string> DepartmentOptions { get; private set; } = [];
    public IReadOnlyList<string> ProfessionOptions { get; private set; } = [];
    public IReadOnlyList<string> StatusOptions { get; private set; } = RiskHazardStatuses.All;
    public IReadOnlyList<RiskHazardTreeLine> TreeLines { get; private set; } = [];
    public IReadOnlyList<RiskAssessmentObject> AssessmentObjects { get; private set; } = [];
    public string SelectedObjectId { get; private set; } = RiskAssessmentObjectIds.OrganizationWide;
    public RiskAssessmentObject? SelectedObject =>
        AssessmentObjects.FirstOrDefault(o =>
            string.Equals(o.Id, SelectedObjectId, StringComparison.OrdinalIgnoreCase));
    public string SelectedObjectDisplayName =>
        _risksWorkspace.DisplayName(
            string.IsNullOrWhiteSpace(SelectedOrganization) ? Organization : SelectedOrganization.Trim(),
            SelectedObjectId);
    public int WorkplaceCount { get; private set; }
    public int PeopleCount { get; private set; }
    public string? Message { get; private set; }

    public void OnGet() => LoadReport();

    public IActionResult OnPostSave()
    {
        string? selectedOrganization = null;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selectedOrganization, allowAll: true);
        SelectedOrganization = selectedOrganization;
        var org = string.IsNullOrWhiteSpace(SelectedOrganization)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : SelectedOrganization.Trim();
        ObjectId = _risksWorkspace.ResolveObjectId(HttpContext, org, Request.Form["ObjectId"].ToString());

        SaveWorkplaceCardsFromForm();
        SaveFamiliarizationsFromForm();
        TempData["Message"] = "Данные карт и ознакомление сохранены.";
        return RedirectToPage(FilterRoute());
    }

    public IActionResult OnPostCreateObject(string? objectName, string? officialName)
    {
        string? selectedOrganization = null;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selectedOrganization, allowAll: true);
        SelectedOrganization = selectedOrganization;
        var org = string.IsNullOrWhiteSpace(selectedOrganization)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : selectedOrganization.Trim();
        if (string.IsNullOrWhiteSpace(org) || string.Equals(org, "Все", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "Выберите организацию, чтобы создать объект.";
            return RedirectToPage(FilterRoute());
        }

        _ = SoutWorkplaceStaffSync.CollapseEmptyProfessionDuplicates(org, _workplaces);
        var orgWideIds = RiskObjectMembership.SnapshotOrganizationWideWorkplaceIds(
            _workplaces.LoadByOrganization(org));
        var created = _objects.Create(org, objectName ?? string.Empty, orgWideIds, officialName);
        ObjectId = created.Id;
        TempData["Message"] = $"Объект «{created.Name}» создан. Состав карт — снимок «по организации».";
        return RedirectToPage(FilterRoute());
    }

    public IActionResult OnPostDeleteObject(string? objectId)
    {
        string? selectedOrganization = null;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selectedOrganization, allowAll: true);
        SelectedOrganization = selectedOrganization;
        var org = string.IsNullOrWhiteSpace(selectedOrganization)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : selectedOrganization.Trim();

        if (string.IsNullOrWhiteSpace(objectId)
            || RiskObjectAssessmentScope.IsOrganizationWide(objectId))
        {
            ObjectId = RiskAssessmentObjectIds.OrganizationWide;
            return RedirectToPage(FilterRoute());
        }

        if (!string.IsNullOrWhiteSpace(org) && _objects.Delete(org, objectId))
        {
            TempData["Message"] = "Объект удалён из списка.";
        }

        ObjectId = RiskAssessmentObjectIds.OrganizationWide;
        return RedirectToPage(FilterRoute());
    }

    public IActionResult OnPostRenameObject(string? objectId, string? objectName, string? officialName)
    {
        string? selectedOrganization = null;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selectedOrganization, allowAll: true);
        SelectedOrganization = selectedOrganization;
        var org = string.IsNullOrWhiteSpace(selectedOrganization)
            ? _orgs.GetSelectedOrganization(HttpContext)
            : selectedOrganization.Trim();
        var id = (objectId ?? string.Empty).Trim();
        var name = (objectName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org) || string.Equals(org, "Все", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "Выберите организацию, чтобы переименовать объект.";
            return RedirectToPage(FilterRoute());
        }

        if (id.Length == 0 || name.Length == 0)
        {
            TempData["Message"] = "Укажите название объекта.";
            ObjectId = string.IsNullOrWhiteSpace(id) ? RiskAssessmentObjectIds.OrganizationWide : id;
            return RedirectToPage(FilterRoute());
        }

        if (_objects.Rename(org, id, name, officialName))
        {
            TempData["Message"] = $"Название изменено на «{name}».";
            ObjectId = id;
        }
        else
        {
            TempData["Message"] = "Не удалось переименовать: объект не найден или такое имя уже есть.";
            ObjectId = id;
        }

        return RedirectToPage(FilterRoute());
    }

    private void SaveWorkplaceCardsFromForm()
    {
        var workplaceIds = Request.Form["cardWorkplaceId"];
        if (workplaceIds.Count == 0)
        {
            return;
        }

        var cardNumbers = Request.Form["cardRiskCardNumber"];
        var assessmentDates = Request.Form["cardAssessmentDate"];
        var overallRisks = Request.Form["cardOverallRisk"];
        var periodicities = Request.Form["cardRiskPeriodicity"];
        var objectId = ResolveSaveObjectId();

        for (var i = 0; i < workplaceIds.Count; i++)
        {
            var workplaceId = At(workplaceIds, i);
            if (string.IsNullOrWhiteSpace(workplaceId))
            {
                continue;
            }

            var workplace = _workplaces.GetById(workplaceId);
            if (workplace is null || workplace.IsGroup)
            {
                continue;
            }

            var edited = RiskObjectAssessmentScope.ProjectForObject(workplace, objectId);
            edited.RiskCardNumber = At(cardNumbers, i);
            edited.OverallRisk = At(overallRisks, i);
            edited.RiskPeriodicity = At(periodicities, i);
            var assessmentDate = ParseDate(At(assessmentDates, i));
            if (assessmentDate.HasValue)
            {
                edited.RiskAssessmentDate = assessmentDate;
                edited.RiskAssessmentNotConducted = false;
            }

            RiskObjectAssessmentScope.SaveAssessment(workplace, edited, objectId);
            _workplaces.Upsert(workplace);
        }
    }

    private void SaveFamiliarizationsFromForm()
    {
        var workplaceIds = Request.Form["famWorkplaceId"];
        var employeeKeys = Request.Form["famEmployeeKey"];
        var employeeNames = Request.Form["famEmployeeName"];
        var statuses = Request.Form["famStatus"];
        if (workplaceIds.Count == 0)
        {
            return;
        }

        var objectId = ResolveSaveObjectId();
        var byWorkplace = new Dictionary<string, List<RiskCardFamiliarization>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < workplaceIds.Count; i++)
        {
            var workplaceId = At(workplaceIds, i);
            var key = At(employeeKeys, i);
            var name = At(employeeNames, i);
            if (string.IsNullOrWhiteSpace(workplaceId) || (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(name)))
            {
                continue;
            }

            var status = At(statuses, i);
            var notWorking = string.Equals(status, "away", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(status, "2", StringComparison.Ordinal)
                             || status.Contains("не работ", StringComparison.OrdinalIgnoreCase);
            var familiarized = !notWorking
                               && (string.Equals(status, "1", StringComparison.Ordinal)
                                   || string.Equals(status, "true", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(status, "ознакомлен", StringComparison.OrdinalIgnoreCase));

            if (!byWorkplace.TryGetValue(workplaceId, out var list))
            {
                list = [];
                byWorkplace[workplaceId] = list;
            }

            list.Add(new RiskCardFamiliarization
            {
                EmployeeKey = key,
                EmployeeName = name,
                IsFamiliarized = familiarized,
                NotWorkingOnObject = notWorking
            });
        }

        foreach (var (workplaceId, entries) in byWorkplace)
        {
            var workplace = _workplaces.GetById(workplaceId);
            if (workplace is null || workplace.IsGroup)
            {
                continue;
            }

            var edited = RiskObjectAssessmentScope.ProjectForObject(workplace, objectId);
            var map = (edited.RiskFamiliarizations ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.EmployeeKey) || !string.IsNullOrWhiteSpace(x.EmployeeName))
                .GroupBy(x => string.IsNullOrWhiteSpace(x.EmployeeKey) ? "name:" + x.EmployeeName : x.EmployeeKey,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var mapKey = string.IsNullOrWhiteSpace(entry.EmployeeKey)
                    ? "name:" + entry.EmployeeName
                    : entry.EmployeeKey;
                map[mapKey] = entry;
            }

            edited.RiskFamiliarizations = map.Values.ToList();
            RiskObjectAssessmentScope.SaveAssessment(workplace, edited, objectId);
            _workplaces.Upsert(workplace);
        }
    }

    private string ResolveSaveObjectId() =>
        RisksWorkspaceContext.ProjectionObjectId(
            Request.Form["ObjectId"].ToString().Trim().Length > 0
                ? Request.Form["ObjectId"].ToString().Trim()
                : SelectedObjectId);

    private object FilterRoute() => new
    {
        reportDate = ReportDate.ToString("yyyy-MM-dd"),
        selectedOrganization = SelectedOrganization,
        department = Department,
        profession = Profession,
        measureStatus = MeasureStatus
    };

    private void LoadReport()
    {
        ReportDate = DateTime.Today;
        string? selectedOrganization = null;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selectedOrganization, allowAll: true);
        SelectedOrganization = selectedOrganization;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        OrganizationOptions = _orgs.GetVisibleOrganizations(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Отчет по ОПР";
        Message = TempData["Message"] as string;

        var selectedOrg = string.IsNullOrWhiteSpace(SelectedOrganization)
            ? string.Empty
            : SelectedOrganization.Trim();
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(selectedOrg) ? "Все организации" : selectedOrg;
        ViewData["Organization"] = string.IsNullOrWhiteSpace(selectedOrg) ? Organization : selectedOrg;

        var orgsToLoad = string.IsNullOrWhiteSpace(selectedOrg)
            ? _employees.ResolveOrganizationsForAllReports(
                OrganizationOptions,
                _orgs.IsOrganizationHidden).ToList()
            : new List<string> { selectedOrg };

        var employeePairs = _employees.LoadPairsForMedReport(orgsToLoad).ToList();
        var workplaces = new List<WorkplaceRecord>();
        var objects = new List<RiskAssessmentObject>();
        foreach (var org in orgsToLoad)
        {
            workplaces.AddRange(_workplaces.LoadTreeByOrganization(org));
            objects.AddRange(_objects.Load(org));
        }

        AssessmentObjects = objects
            .GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(o => o.IsOrganizationWide ? 0 : 1)
            .ThenBy(o => o.SortOrder)
            .ThenBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (AssessmentObjects.Count == 0 && !string.IsNullOrWhiteSpace(selectedOrg))
        {
            AssessmentObjects = [_objects.EnsureOrganizationWide(selectedOrg)];
        }

        var objectOrg = string.IsNullOrWhiteSpace(selectedOrg) ? Organization : selectedOrg;
        SelectedObjectId = string.IsNullOrWhiteSpace(selectedOrg)
            ? RiskAssessmentObjectIds.OrganizationWide
            : _risksWorkspace.ResolveObjectId(HttpContext, objectOrg, ObjectId);
        ObjectId = SelectedObjectId;
        var selectedObject = SelectedObject;
        if (!string.IsNullOrWhiteSpace(selectedOrg))
        {
            workplaces = RisksWorkspaceContext.ApplyWorkplaceScope(
                workplaces,
                SelectedObjectId,
                selectedObject,
                obj => _objects.Upsert(obj));
        }

        DepartmentOptions = DistinctSorted(employeePairs
            .Select(x => string.IsNullOrWhiteSpace(x.Employee.Department) ? "Без подразделения" : x.Employee.Department.Trim())
            .Concat(workplaces.Where(w => !w.IsGroup).Select(w =>
                string.IsNullOrWhiteSpace(w.Department) ? "Без подразделения" : w.Department.Trim())));
        ProfessionOptions = DistinctSorted(employeePairs
            .Select(x =>
            {
                var n = ProfessionMatcher.Normalize(x.Employee.Profession);
                return string.IsNullOrWhiteSpace(n) ? "Без профессии" : n;
            })
            .Concat(workplaces.Where(w => !w.IsGroup).Select(w =>
            {
                var raw = string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession;
                var n = ProfessionMatcher.Normalize(raw);
                return string.IsNullOrWhiteSpace(n) ? "Без профессии" : n;
            })));

        TreeLines = RiskHazardStatusBuilder.BuildStaffTree(
            employeePairs,
            workplaces,
            ReportDate.Date,
            selectedOrg,
            Department,
            Profession,
            MeasureStatus,
            onlyProfessionsWithCard: RisksWorkspaceContext.ShouldFilterMembership(SelectedObjectId));

        WorkplaceCount = TreeLines.Count(l => l.IsGroup && l.Level == 2);
        // Штат человек×ЮЛ (как МО/обучение), не узлы дерева после фильтра статуса оценки.
        PeopleCount = StaffHeadcount.CountPersonOrgCards(
            employeePairs
                .Where(p => string.IsNullOrWhiteSpace(Department)
                            || (p.Employee.Department ?? string.Empty)
                                .Contains(Department, StringComparison.OrdinalIgnoreCase))
                .Where(p => string.IsNullOrWhiteSpace(Profession)
                            || ProfessionMatcher.Matches(Profession, p.Employee.Profession)
                            || (p.Employee.Profession ?? string.Empty)
                                .Contains(Profession, StringComparison.OrdinalIgnoreCase)));
    }

    private static string At(Microsoft.Extensions.Primitives.StringValues values, int index) =>
        index < values.Count ? values[index].ToString()?.Trim() ?? string.Empty : string.Empty;

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var invariant))
        {
            return invariant.Date;
        }

        return DateTime.TryParse(raw, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.AssumeLocal, out var ru)
            ? ru.Date
            : null;
    }

    private static IReadOnlyList<string> DistinctSorted(IEnumerable<string> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

[RequireLogin]
public class IdentificationScheduleModel : PageModel
{
    private readonly RiskPreparationStore _store;
    private readonly RiskWorkplaceStore _workplaces;
    private readonly RiskHazardAssessmentStore _assessments;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public IdentificationScheduleModel(
        RiskPreparationStore store,
        RiskWorkplaceStore workplaces,
        RiskHazardAssessmentStore assessments,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _workplaces = workplaces;
        _assessments = assessments;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)]
    public int? ScheduleYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<RiskPreparationDocument> Rows { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public RiskPreparationDocument? SelectedDocument =>
        string.IsNullOrWhiteSpace(PrintId) ? null : _store.GetById(PrintId);
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet() => LoadPage();

    public IActionResult OnPostFromStatusReport()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        RiskOrganizationBootstrap.Ensure(Organization, _workplaces, _store);
        var year = ReadYear();
        var department = Request.Form["department"].ToString()?.Trim();
        ScheduleYear = year;
        Department = department;

        var workplaces = _workplaces.LoadTreeByOrganization(Organization);
        var staff = _employees.Load(Organization);
        var employees = staff
            .Select(e => (Organization, e))
            .ToList();
        var saved = _assessments.LoadAll();

        var document = RiskIdentificationScheduleBuilder.BuildFromStatusReport(
            Organization,
            year,
            department,
            workplaces,
            employees,
            saved);
        document.Number = _store.SuggestNextNumber();
        _store.Upsert(document);

        var deptCount = document.PrintOverrides.TryGetValue("schedule-workplaces", out var countText)
            ? countText
            : "0";
        TempData["Message"] = int.TryParse(deptCount, out var count) && count > 0
            ? $"График {document.Number} сформирован из отчёта по рискам: {count} раб. мест, {CountDepartments(document)} подразделений. Откройте предпечать."
            : $"График {document.Number} создан, но в отчёте нет РМ для {year} года (просроченных / без оценки / с истекающим сроком). Проверьте отчёт или сформируйте из перечня РМ.";

        return RedirectToPage("/Risks/IdentificationSchedule", new { scheduleYear = year, department, printId = document.Id });
    }

    public IActionResult OnPostFromWorkplaces()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        RiskOrganizationBootstrap.Ensure(Organization, _workplaces, _store);
        var year = ReadYear();
        var department = Request.Form["department"].ToString()?.Trim();
        ScheduleYear = year;
        Department = department;

        var workplaces = _workplaces.LoadTreeByOrganization(Organization);
        var document = RiskIdentificationScheduleBuilder.BuildFromWorkplaces(
            Organization,
            year,
            department,
            workplaces);
        document.Number = _store.SuggestNextNumber();
        _store.Upsert(document);

        var deptCount = document.PrintOverrides.TryGetValue("schedule-workplaces", out var countText)
            ? countText
            : "0";
        TempData["Message"] = int.TryParse(deptCount, out var count) && count > 0
            ? $"График {document.Number} сформирован из перечня РМ: {count} раб. мест, {CountDepartments(document)} подразделений. Откройте предпечать."
            : $"График {document.Number} создан, но в перечне нет РМ для оценки в {year} году. Проверьте справочник рабочих мест.";

        return RedirectToPage("/Risks/IdentificationSchedule", new { scheduleYear = year, department, printId = document.Id });
    }

    public IActionResult OnPostDelete(string id)
    {
        var scheduleYear = int.TryParse(Request.Form["scheduleYear"], out var y) ? (int?)y : null;
        var department = Request.Form["department"].ToString()?.Trim();
        var q = Request.Form["q"].ToString()?.Trim();
        var printId = Request.Form["printId"].ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["Message"] = "Не указан документ для удаления.";
            TempData["IsError"] = true;
            return RedirectToPage("/Risks/IdentificationSchedule");
        }

        var document = _store.GetById(id);
        if (document is null)
        {
            TempData["Message"] = "Документ не найден.";
            TempData["IsError"] = true;
            return RedirectToPage("/Risks/IdentificationSchedule");
        }

        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (!string.IsNullOrWhiteSpace(Organization)
            && !string.Equals(document.Organization, Organization, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "Нельзя удалить документ другой организации.";
            TempData["IsError"] = true;
            return RedirectToPage("/Risks/IdentificationSchedule");
        }

        _store.Delete(id);
        TempData["Message"] = $"График {document.Number} удалён.";
        if (string.Equals(printId, id, StringComparison.OrdinalIgnoreCase))
        {
            printId = null;
        }

        return RedirectToPage("/Risks/IdentificationSchedule", new
        {
            scheduleYear,
            department,
            q,
            printId
        });
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        RiskOrganizationBootstrap.Ensure(Organization, _workplaces, _store);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "График идентификации опасностей и оценки рисков";
        ViewData["Subtitle"] = Organization;
        Message = TempData["Message"] as string;
        IsError = TempData["IsError"] as bool? == true;

        var year = ScheduleYear ?? DateTime.Today.Year;
        var rows = _store.LoadByOrganization(Organization)
            .Where(doc => doc.PrintOverrides.ContainsKey("schedule-source")
                          || doc.PrintOverrides.Keys.Any(k => k.StartsWith("sch-dept-", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (!rows.Any(doc => doc.PrintOverrides.ContainsKey("schedule-source")))
        {
            rows = _store.LoadByOrganization(Organization);
        }

        rows = rows
            .Where(doc => !ScheduleYear.HasValue || ResolveYear(doc) == year)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Department))
        {
            rows = rows.Where(doc =>
                    doc.Department.Contains(Department, StringComparison.OrdinalIgnoreCase)
                    || doc.PrintOverrides.Values.Any(v =>
                        v.Contains(Department, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows.Where(doc =>
                    doc.Number.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || doc.Scenario.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || doc.Department.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Rows = rows;
        Departments = _workplaces.LoadTreeByOrganization(Organization)
            .Where(item => !item.IsGroup && item.IsUsed)
            .Select(item => item.Department)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(PrintId))
        {
            PrintId = Rows.FirstOrDefault()?.Id;
        }
    }

    private int ReadYear()
    {
        if (int.TryParse(Request.Form["scheduleYear"], out var year) && year is >= 2000 and <= 2100)
        {
            return year;
        }

        return DateTime.Today.Year;
    }

    private static int ResolveYear(RiskPreparationDocument document)
    {
        if (document.PrintOverrides.TryGetValue("schedule-year", out var yearText)
            && int.TryParse(yearText, out var year))
        {
            return year;
        }

        return (document.StartDate ?? document.Date).Year;
    }

    private static int CountDepartments(RiskPreparationDocument document)
    {
        var count = 0;
        for (var n = 1; n <= 500; n++)
        {
            if (!document.PrintOverrides.TryGetValue($"sch-dept-{n}", out var dept) || string.IsNullOrWhiteSpace(dept))
            {
                break;
            }

            count++;
        }

        return count;
    }
}

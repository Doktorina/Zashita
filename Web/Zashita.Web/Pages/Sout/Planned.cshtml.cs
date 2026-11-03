using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

[RequireLogin]
public class PlannedModel : PageModel
{
    private readonly SoutAnnualPlanStore _store;
    private readonly WorkplaceStore _workplaces;
    private readonly WorkplaceAssignmentStore _assignments;
    private readonly SoutPreparationStore _preparation;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public PlannedModel(
        SoutAnnualPlanStore store,
        WorkplaceStore workplaces,
        WorkplaceAssignmentStore assignments,
        SoutPreparationStore preparation,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _workplaces = workplaces;
        _assignments = assignments;
        _preparation = preparation;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)]
    public int? PlanYear { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<SoutAnnualPlanDocument> Rows { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet() => LoadPage();

    public IActionResult OnPostFromStatusReport()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SoutOrganizationBootstrap.Ensure(Organization, _workplaces, _assignments, _preparation, _store);
        var year = int.TryParse(Request.Form["planYear"], out var y) && y > 0 ? y : DateTime.Today.Year;
        var department = Request.Form["department"].ToString()?.Trim();
        PlanYear = year;
        Department = department;

        var document = SoutAnnualPlanBuilder.Build(
            Organization,
            year,
            department,
            _workplaces.LoadTreeByOrganization(Organization),
            _assignments.LoadDocumentsByOrganization(Organization),
            _employees.Load(Organization));
        document.Number = _store.SuggestNextNumber();
        _store.Upsert(document);

        TempData["Message"] = document.DetailRows.Count > 0
            ? $"План {document.Number} сформирован из отчёта СОУТ: {document.DetailRows.Count} раб. мест. Проверьте расшифровку и запишите."
            : $"План {document.Number} создан, но в отчёте СОУТ нет РМ для года {year} (просроченных / без СОУТ / с сроком в этом году). Заполните отчёт или добавьте строки вручную.";

        return RedirectToPage("/Sout/Planned/Edit", new { id = document.Id });
    }

    public IActionResult OnPostDelete(string id)
    {
        var planYear = int.TryParse(Request.Form["planYear"], out var y) ? (int?)y : null;
        var department = Request.Form["department"].ToString()?.Trim();
        var q = Request.Form["q"].ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["Message"] = "Не указан план для удаления.";
            TempData["IsError"] = true;
            return RedirectToPage("/Sout/Planned");
        }

        var document = _store.GetById(id);
        if (document is null)
        {
            TempData["Message"] = "План не найден.";
            TempData["IsError"] = true;
            return RedirectToPage("/Sout/Planned");
        }

        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (!string.IsNullOrWhiteSpace(Organization)
            && !string.Equals(document.Organization, Organization, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "Нельзя удалить план другой организации.";
            TempData["IsError"] = true;
            return RedirectToPage("/Sout/Planned");
        }

        _store.Delete(id);
        TempData["Message"] = $"План {document.Number} удалён.";
        return RedirectToPage("/Sout/Planned", new
        {
            planYear,
            department,
            q
        });
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SoutOrganizationBootstrap.Ensure(Organization, _workplaces, _assignments, _preparation, _store);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Годовой план по специальной оценке";
        ViewData["Subtitle"] = Organization;
        Message = TempData["Message"] as string;
        IsError = TempData["IsError"] as bool? == true;

        var rows = _store.LoadByOrganization(Organization);
        if (PlanYear is not null)
        {
            rows = rows.Where(row => row.PlanYear == PlanYear.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Department))
        {
            rows = rows.Where(row => row.Department.Contains(Department, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows.Where(row =>
                    row.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.PlanType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Scenario.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Department.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Organization.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Rows = rows;
        Departments = _workplaces.LoadTreeByOrganization(Organization)
            .Where(item => item.IsGroup)
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

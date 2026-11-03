using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.Acceptance;

[RequireLogin]
public class IndexModel : PageModel
{
    private readonly OrganizationStore _orgs;
    private readonly PpeAcceptanceStore _store;
    private readonly OrganizationEmployeesStore _employees;

    public IndexModel(OrganizationStore orgs, PpeAcceptanceStore store, OrganizationEmployeesStore employees)
    {
        _orgs = orgs;
        _store = store;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<PpeAcceptanceDocument> Rows { get; private set; } = [];
    public PpeAcceptanceDocument? SelectedPrintDocument =>
        string.IsNullOrWhiteSpace(PrintId) ? null : _store.GetById(PrintId);

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? Department { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public bool InGroup { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? PrintId { get; set; }

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Приемка спецодежды, СИЗ";
        ViewData["Subtitle"] = Organization;

        var all = _store.LoadByOrganization(Organization);
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, Organization),
            all.Select(r => r.Department));

        var rows = all;
        if (string.Equals(Department, "__empty__", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(r => string.IsNullOrWhiteSpace(r.Department)).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(Department))
        {
            rows = InGroup
                ? rows.Where(r => string.Equals(r.Department, Department.Trim(), StringComparison.OrdinalIgnoreCase)).ToList()
                : rows.Where(r => r.Department.Contains(Department.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows.Where(r =>
                r.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Warehouse.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Department.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Rows = rows;
        if (string.IsNullOrWhiteSpace(PrintId) && rows.Count > 0)
        {
            PrintId = rows[0].Id;
        }
    }
}

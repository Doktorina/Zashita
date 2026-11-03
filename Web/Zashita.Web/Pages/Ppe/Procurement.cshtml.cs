using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class ProcurementModel : PageModel
{
    private readonly IssueStandardsStore _standards;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesStore _employees;

    public ProcurementModel(IssueStandardsStore standards, OrganizationStore orgs, OrganizationEmployeesStore employees)
    {
        _standards = standards;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> Organizations { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public IReadOnlyList<ProcurementListRow> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool InGroup { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EtnName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FilterOrg { get; set; }

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Установки потребностей в спецодежде, СИЗ";
        var standards = _standards.LoadAll()
            .Where(s => s.Items.Any(x => !string.IsNullOrWhiteSpace(x)))
            .ToList();
        Organizations = standards
            .Select(s => (s.Organization ?? "").Trim())
            .Where(o => o.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (string.IsNullOrWhiteSpace(FilterOrg))
        {
            FilterOrg = Organizations.Any(o => string.Equals(o, Organization, StringComparison.OrdinalIgnoreCase))
                ? Organization
                : "";
        }

        var filtered = string.IsNullOrWhiteSpace(FilterOrg)
            ? standards
            : standards.Where(s => (s.Organization ?? "").Contains(FilterOrg, StringComparison.OrdinalIgnoreCase)).ToList();

        var rows = filtered
            .OrderByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select((s, index) => new ProcurementListRow(
                s.Id,
                s.CreatedAt == default ? DateTime.Today.AddDays(-index) : s.CreatedAt.ToLocalTime().Date,
                $"ОТ-{index + 20:000000}",
                string.IsNullOrWhiteSpace(s.Profession) ? "Производство" : s.Profession,
                string.IsNullOrWhiteSpace(s.Organization) ? "—" : s.Organization,
                s.Name,
                s.StandardCategory))
            .ToList();

        if (!string.IsNullOrWhiteSpace(Department))
        {
            rows = rows.Where(r => r.Department.Contains(Department.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (InGroup && !string.IsNullOrWhiteSpace(Department))
        {
            rows = rows.Where(r => string.Equals(r.Department, Department.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(EtnName))
        {
            rows = rows.Where(r => r.EtnName.Contains(EtnName.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows.Where(r =>
                r.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Department.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.OrganizationName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.EtnName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        Rows = rows;
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, Organization),
            filtered.Select(s => string.IsNullOrWhiteSpace(s.Profession) ? "Производство" : s.Profession));
        if (string.IsNullOrWhiteSpace(SelectedId) && rows.Count > 0)
        {
            SelectedId = rows[0].Id;
        }
    }

    public sealed record ProcurementListRow(
        string Id,
        DateTime Date,
        string Number,
        string Department,
        string OrganizationName,
        string EtnName,
        string Category);
}

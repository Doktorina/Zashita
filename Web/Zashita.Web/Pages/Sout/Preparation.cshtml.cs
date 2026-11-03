using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

[RequireLogin]
public class PreparationModel : PageModel
{
    private readonly SoutPreparationStore _store;
    private readonly WorkplaceStore _workplaces;
    private readonly WorkplaceAssignmentStore _assignments;
    private readonly SoutAnnualPlanStore _annualPlans;
    private readonly OrganizationStore _orgs;

    public PreparationModel(
        SoutPreparationStore store,
        WorkplaceStore workplaces,
        WorkplaceAssignmentStore assignments,
        SoutAnnualPlanStore annualPlans,
        OrganizationStore orgs)
    {
        _store = store;
        _workplaces = workplaces;
        _assignments = assignments;
        _annualPlans = annualPlans;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    /// <summary>list=1 — реестр документов; иначе редирект на хаб шаблонов.</summary>
    [BindProperty(SupportsGet = true)]
    public string? List { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<SoutPreparationDocument> Rows { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public SoutPreparationDocument? SelectedPrintDocument =>
        string.IsNullOrWhiteSpace(PrintId) ? null : _store.GetById(PrintId);

    public IActionResult OnGet()
    {
        var openList = string.Equals(List, "1", StringComparison.OrdinalIgnoreCase) ||
                       !string.IsNullOrWhiteSpace(PrintId) ||
                       !string.IsNullOrWhiteSpace(Q) ||
                       !string.IsNullOrWhiteSpace(Department);
        if (!openList)
        {
            return RedirectToPage("/Sout/Preparation/Templates");
        }

        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SoutOrganizationBootstrap.Ensure(Organization, _workplaces, _assignments, _store, _annualPlans);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Подготовка проведения СОУТ";
        ViewData["Subtitle"] = Organization;

        var rows = _store.LoadByOrganization(Organization);
        if (!string.IsNullOrWhiteSpace(Department))
        {
            rows = rows
                .Where(row => row.Department.Contains(Department, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            rows = rows
                .Where(row =>
                    row.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Department.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.ContractorOrganization.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Rows = rows;
        Departments = _store.LoadByOrganization(Organization)
            .Select(row => row.Department)
            .Concat(_workplaces.LoadTreeByOrganization(Organization)
                .Where(item => item.IsGroup)
                .Select(item => item.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Page();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

[RequireLogin]
public class DocumentsModel : PageModel
{
    public const string TabHub = "hub";
    public const string TabOrder = "order";
    public const string TabPreparation = "preparation";

    private readonly RiskUnscheduledOrderStore _orders;
    private readonly RiskPreparationStore _preparation;
    private readonly RiskWorkplaceStore _workplaces;
    private readonly OrganizationStore _orgs;

    public DocumentsModel(
        RiskUnscheduledOrderStore orders,
        RiskPreparationStore preparation,
        RiskWorkplaceStore workplaces,
        OrganizationStore orgs)
    {
        _orders = orders;
        _preparation = preparation;
        _workplaces = workplaces;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    public string ActiveTab { get; private set; } = TabHub;
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<RiskUnscheduledOrderDocument> OrderRows { get; private set; } = [];
    public IReadOnlyList<RiskPreparationDocument> PreparationRows { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];

    public RiskUnscheduledOrderDocument? SelectedOrder =>
        string.IsNullOrWhiteSpace(PrintId) ? null : _orders.GetById(PrintId);

    public RiskPreparationDocument? SelectedPreparation =>
        string.IsNullOrWhiteSpace(PrintId) ? null : _preparation.GetById(PrintId);

    public string? Message { get; private set; }

    public IActionResult OnGet()
    {
        Message = TempData["Message"] as string;
        var tab = (Tab ?? string.Empty).Trim().ToLowerInvariant();
        if (tab is TabOrder or "orders")
        {
            return RedirectToPage("/Risks/UnscheduledOrders/SelectCommission");
        }

        LoadPage();
        return Page();
    }

    public IActionResult OnPostDeleteOrder(string? id)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["Message"] = "Не указан приказ для удаления.";
            return RedirectToPage(new { tab = TabOrder, department = Department, q = Q });
        }

        var doc = _orders.GetById(id.Trim());
        if (doc is null
            || !string.Equals(doc.Organization, Organization, StringComparison.OrdinalIgnoreCase))
        {
            TempData["Message"] = "Приказ не найден.";
            return RedirectToPage(new { tab = TabOrder, department = Department, q = Q });
        }

        var number = doc.Number;
        _orders.Delete(doc.Id);
        TempData["Message"] = $"Приказ «{number}» удалён.";
        return RedirectToPage(new { tab = TabOrder, department = Department, q = Q });
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        RiskOrganizationBootstrap.Ensure(Organization, _workplaces, _preparation, _orders);
        ActiveTab = NormalizeTab(Tab);

        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = " ";
        ViewData["Subtitle"] = Organization;

        OrderRows = FilterOrders(_orders.LoadByOrganization(Organization));
        PreparationRows = FilterPreparation(_preparation.LoadByOrganization(Organization));

        if (!string.IsNullOrWhiteSpace(PrintId) && ActiveTab == TabHub)
        {
            ActiveTab = TabOrder;
        }

        if (string.IsNullOrWhiteSpace(PrintId) && ActiveTab != TabHub)
        {
            PrintId = ActiveTab == TabPreparation
                ? PreparationRows.FirstOrDefault()?.Id
                : OrderRows.FirstOrDefault()?.Id;
        }

        Departments = OrderRows.Select(r => r.Department)
            .Concat(PreparationRows.Select(r => r.Department))
            .Concat(_workplaces.LoadTreeByOrganization(Organization)
                .Where(w => w.IsGroup)
                .Select(w => w.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string NormalizeTab(string? tab)
    {
        if (string.Equals(tab, TabPreparation, StringComparison.OrdinalIgnoreCase))
        {
            return TabPreparation;
        }

        if (string.Equals(tab, TabOrder, StringComparison.OrdinalIgnoreCase))
        {
            return TabOrder;
        }

        return TabHub;
    }

    private List<RiskUnscheduledOrderDocument> FilterOrders(List<RiskUnscheduledOrderDocument> rows)
    {
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
                    row.Department.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return rows;
    }

    private List<RiskPreparationDocument> FilterPreparation(List<RiskPreparationDocument> rows)
    {
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
                    row.Department.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return rows;
    }
}

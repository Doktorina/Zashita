using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.UnscheduledOrders;

[RequireLogin]
public class SelectCommissionModel : PageModel
{
    private readonly SoutCommissionStore _commissions;
    private readonly RiskUnscheduledOrderStore _orders;
    private readonly OrganizationStore _orgs;

    public SelectCommissionModel(
        SoutCommissionStore commissions,
        RiskUnscheduledOrderStore orders,
        OrganizationStore orgs)
    {
        _commissions = commissions;
        _orders = orders;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true, Name = "orderId")]
    public string? OrderId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedCommissionId { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public string? RiskFolderId { get; private set; }
    public bool IsChangeMode => !string.IsNullOrWhiteSpace(OrderId);
    public string ReturnHereUrl { get; private set; } = "/Risks/UnscheduledOrders/SelectCommission";
    public string? Message { get; private set; }
    public IReadOnlyList<CommissionRow> CommissionRows { get; private set; } = [];
    public IReadOnlyList<OrderHistoryRow> OrderRows { get; private set; } = [];

    public void OnGet()
    {
        Message = TempData["Message"] as string;
        if (TempData["SelectedCommissionId"] is string fromTemp && !string.IsNullOrWhiteSpace(fromTemp))
        {
            SelectedCommissionId = fromTemp;
        }

        LoadPage();
    }

    public IActionResult OnPostCreate(string commissionId, string? orderId)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(commissionId))
        {
            LoadPage();
            return Page();
        }

        var all = _commissions.Load(Organization);
        var commission = RiskCommissionResolver.Resolve(Organization, all, commissionId);
        if (commission is null)
        {
            LoadPage();
            return Page();
        }

        RiskUnscheduledOrderDocument document;
        if (!string.IsNullOrWhiteSpace(orderId))
        {
            document = _orders.GetById(orderId) ?? new RiskUnscheduledOrderDocument();
            if (string.IsNullOrWhiteSpace(document.Id))
            {
                LoadPage();
                return Page();
            }

            document.CommissionId = commission.Id;
            ApplyCommissionOrderRequisites(document, commission, fallbackNumber: document.Number);
            if (document.PrintOverrides is not null)
            {
                foreach (var key in document.PrintOverrides.Keys.ToList())
                {
                    if (key.StartsWith("order-chairman", StringComparison.OrdinalIgnoreCase)
                        || key.StartsWith("order-member", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "order-members", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "order-title", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "order-date", StringComparison.OrdinalIgnoreCase))
                    {
                        document.PrintOverrides.Remove(key);
                    }
                }
            }
        }
        else
        {
            document = new RiskUnscheduledOrderDocument
            {
                Organization = Organization,
                CommissionId = commission.Id,
                CreatedAt = DateTime.UtcNow
            };
            ApplyCommissionOrderRequisites(document, commission, fallbackNumber: _orders.SuggestNextNumber());
        }

        _orders.Upsert(document);
        return RedirectToPage("/Risks/UnscheduledOrders/Preview", new { id = document.Id, mode = "order" });
    }

    private static void ApplyCommissionOrderRequisites(
        RiskUnscheduledOrderDocument document,
        SoutCommissionRecord commission,
        string? fallbackNumber)
    {
        var fromCommission = NormalizeCommissionOrderNumber(commission.OrderNumber);
        document.Number = !string.IsNullOrWhiteSpace(fromCommission)
            ? fromCommission
            : (fallbackNumber?.Trim() ?? string.Empty);

        document.Date = commission.OrderDate is { Year: >= 2000 } d
            ? d.Date
            : (document.Date.Year >= 2000 ? document.Date.Date : DateTime.Today);
    }

    private static string NormalizeCommissionOrderNumber(string? raw)
    {
        var n = (raw ?? string.Empty).Trim();
        if (n.StartsWith("№", StringComparison.OrdinalIgnoreCase))
        {
            n = n[1..].Trim();
        }

        return n;
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsChangeMode
            ? "Изменить комиссию в приказе"
            : "Приказ о создании комиссии";
        ViewData["Subtitle"] = Organization;

        ReturnHereUrl = string.IsNullOrWhiteSpace(OrderId)
            ? "/Risks/UnscheduledOrders/SelectCommission"
            : $"/Risks/UnscheduledOrders/SelectCommission?orderId={Uri.EscapeDataString(OrderId!)}";

        var all = _commissions.Load(Organization);
        RiskFolderId = RiskCommissionResolver.FindRiskFolderId(all);
        var selectable = RiskCommissionResolver.ListSelectable(Organization, all);
        var currentCommissionId = !string.IsNullOrWhiteSpace(OrderId)
            ? _orders.GetById(OrderId)?.CommissionId
            : null;
        var recommendedId = !string.IsNullOrWhiteSpace(SelectedCommissionId)
            ? SelectedCommissionId
            : !string.IsNullOrWhiteSpace(currentCommissionId)
                ? currentCommissionId
                : selectable.FirstOrDefault()?.Id;

        // OrganizationMatcher может подтянуть «родственные» организации с теми же Id
        // (короткое / полное имя ИП) — без дедупа ToDictionary падает.
        CommissionRows = selectable
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(record =>
            {
                SoutCommissionMemberHelpers.SyncComposition(record);
                return new CommissionRow(
                    record.Id,
                    record.Name?.Trim() ?? "—",
                    SoutCommissionListHelpers.ChairmanDisplay(record),
                    SoutCommissionListHelpers.MembersDisplay(record),
                    SoutCommissionListHelpers.OrderNumberAndDate(record),
                    string.Equals(record.Id, recommendedId, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        var commissionNames = all
            .Where(c => !c.IsGroup && !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => string.IsNullOrWhiteSpace(g.First().Name) ? "—" : g.First().Name.Trim(),
                StringComparer.OrdinalIgnoreCase);

        OrderRows = _orders.LoadByOrganization(Organization)
            .Where(o => OrganizationMatcher.Matches(Organization, o.Organization)
                        || string.Equals(Organization, o.Organization, StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(o.Organization))
            .Select(o => new OrderHistoryRow(
                o.Id,
                o.Date,
                o.Number ?? string.Empty,
                ResolveCommissionName(o.CommissionId, commissionNames),
                o.IsPosted))
            .ToList();
    }

    private static string ResolveCommissionName(
        string? commissionId,
        IReadOnlyDictionary<string, string> names)
    {
        if (string.IsNullOrWhiteSpace(commissionId))
        {
            return "—";
        }

        return names.TryGetValue(commissionId.Trim(), out var name) ? name : "—";
    }

    public sealed record CommissionRow(
        string Id,
        string Name,
        string Chairman,
        string Members,
        string OrderInfo,
        bool IsRecommended);

    public sealed record OrderHistoryRow(
        string Id,
        DateTime Date,
        string Number,
        string CommissionName,
        bool IsPosted);
}

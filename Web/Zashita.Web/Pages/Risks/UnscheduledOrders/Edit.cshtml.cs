using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.UnscheduledOrders;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly RiskUnscheduledOrderStore _store;
    private readonly OrganizationStore _orgs;
    private readonly SoutCommissionStore _commissions;

    public EditModel(RiskUnscheduledOrderStore store, OrganizationStore orgs, SoutCommissionStore commissions)
    {
        _store = store;
        _orgs = orgs;
        _commissions = commissions;
    }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string? Id { get; set; }

    public RiskUnscheduledOrderDocument Document { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public string CommissionName { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        if (IsNew)
        {
            return RedirectToPage("/Risks/UnscheduledOrders/SelectCommission");
        }

        LoadPage();
        if (string.IsNullOrWhiteSpace(Document.Id) || !string.Equals(Document.Id, Id, StringComparison.Ordinal))
        {
            return RedirectToPage("/Risks/Documents", new { tab = "order" });
        }

        return Page();
    }

    public IActionResult OnPostSave() => SaveDocument(post: false, close: false);

    public IActionResult OnPostPost() => SaveDocument(post: true, close: false);

    public IActionResult OnPostPostAndClose() => SaveDocument(post: true, close: true);

    public IActionResult OnPostDelete()
    {
        if (!string.IsNullOrWhiteSpace(Id))
        {
            _store.Delete(Id);
        }

        return RedirectToPage("/Risks/Documents", new { tab = "order" });
    }

    private IActionResult SaveDocument(bool post, bool close)
    {
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);
        var document = BuildDocumentFromForm();
        if (string.IsNullOrWhiteSpace(document.Number))
        {
            document.Number = _store.SuggestNextNumber();
        }

        document.IsPosted = post || (existing?.IsPosted ?? false);
        _store.Upsert(document);

        return close
            ? RedirectToPage("/Risks/Documents", new { tab = "order" })
            : RedirectToPage("/Risks/UnscheduledOrders/Edit", new { id = document.Id });
    }

    private RiskUnscheduledOrderDocument BuildDocumentFromForm()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        var existing = string.IsNullOrWhiteSpace(Id) ? null : _store.GetById(Id);

        _ = DateTime.TryParse(Request.Form["date"].ToString(), out var date);

        var number = Request.Form["number"].ToString()?.Trim() ?? string.Empty;
        if (string.Equals(number, "<Авто>", StringComparison.OrdinalIgnoreCase))
        {
            number = string.Empty;
        }

        return new RiskUnscheduledOrderDocument
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id!,
            Organization = Organization,
            Number = number,
            Date = date == default ? DateTime.Now : date,
            Department = Request.Form["department"].ToString()?.Trim() ?? string.Empty,
            CommissionId = existing?.CommissionId ?? string.Empty,
            IsPosted = existing?.IsPosted ?? false,
            PrintOverrides = existing?.PrintOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
        };
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew
            ? "Приказ на оценку рисков. Новый"
            : "Приказ на оценку рисков";
        ViewData["Subtitle"] = Organization;

        if (!IsNew && !string.IsNullOrWhiteSpace(Id))
        {
            Document = _store.GetById(Id) ?? new RiskUnscheduledOrderDocument();
            if (!string.IsNullOrWhiteSpace(Document.CommissionId))
            {
                var commission = _commissions.Find(Organization, Document.CommissionId);
                CommissionName = commission?.Name?.Trim() ?? string.Empty;
            }
        }
    }
}

using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.Acceptance;

[RequireLogin]
public class PrintReturnStatementModel : PageModel
{
    private readonly OrganizationStore _orgs;
    private readonly PpeAcceptanceStore _store;

    public PrintReturnStatementModel(OrganizationStore orgs, PpeAcceptanceStore store)
    {
        _orgs = orgs;
        _store = store;
    }

    public string Html { get; private set; } = string.Empty;

    public void OnGet(string? id)
    {
        var org = _orgs.GetSelectedOrganization(HttpContext);
        var doc = !string.IsNullOrWhiteSpace(id) ? _store.GetById(id) : null;
        Html = PpeAcceptancePrintBuilder.BuildReturnStatement(
            org,
            doc?.Warehouse ?? "Склад спецодежды",
            doc?.Number ?? "ПРМ-000001",
            (doc?.Date ?? DateTime.Today).Date,
            doc?.Lines?.Count > 0 ? doc.Lines : PpeAcceptancePrintBuilder.DemoLines());
    }
}

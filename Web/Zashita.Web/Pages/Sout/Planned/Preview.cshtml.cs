using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Planned;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly SoutAnnualPlanStore _store;
    private readonly OrganizationStore _orgs;

    public PreviewModel(SoutAnnualPlanStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true, Name = "id")]
    public string? Id { get; set; }

    public SoutAnnualPlanDocument Document { get; private set; } = new();
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }

    public IActionResult OnGet()
    {
        var organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        if (string.IsNullOrWhiteSpace(Id))
        {
            NotFound = true;
            return Page();
        }

        var doc = _store.GetById(Id);
        if (doc is null ||
            (!string.IsNullOrWhiteSpace(organization) &&
             !string.Equals(doc.Organization, organization, StringComparison.OrdinalIgnoreCase)))
        {
            NotFound = true;
            return Page();
        }

        Document = doc;
        PreviewHtml = SoutAnnualPlanPrintBuilder.Build(doc);
        return Page();
    }
}

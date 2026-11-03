using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Workplaces;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly WorkplaceStore _store;
    private readonly OrganizationStore _orgs;

    public PreviewModel(WorkplaceStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? View { get; set; }

    public bool IsAssessmentView =>
        string.Equals(View, "assessment", StringComparison.OrdinalIgnoreCase);

    public string Organization { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        var workplaces = _store.LoadTreeByOrganization(Organization);
        PreviewHtml = SoutWorkplaceAssessmentPrintBuilder.Build(Organization, workplaces);
    }
}

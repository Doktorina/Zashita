using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Workspace;

[RequireLogin]
public class SetModel : PageModel
{
    private readonly OrganizationStore _organizations;
    private readonly RisksWorkspaceContext _risksWorkspace;

    public SetModel(OrganizationStore organizations, RisksWorkspaceContext risksWorkspace)
    {
        _organizations = organizations;
        _risksWorkspace = risksWorkspace;
    }

    public IActionResult OnPost(string? organization, string? organizationType, string? organizationGroup, string? returnUrl)
    {
        var previousOrg = _organizations.IsAllOrganizationsSelected(HttpContext)
            ? string.Empty
            : _organizations.GetSelectedOrganization(HttpContext);

        _organizations.SetWorkspaceContext(HttpContext, organization, organizationType, organizationGroup);

        var nextOrg = _organizations.IsAllOrganizationsSelected(HttpContext)
            ? string.Empty
            : _organizations.GetSelectedOrganization(HttpContext);

        if (!string.Equals(previousOrg, nextOrg, StringComparison.OrdinalIgnoreCase))
        {
            _risksWorkspace.ResetToOrganizationWide(HttpContext);
        }

        var target = WorkspaceReturnUrl.Sanitize(returnUrl);
        if (Url.IsLocalUrl(target))
        {
            return LocalRedirect(target);
        }

        return RedirectToPage("/Index");
    }
}

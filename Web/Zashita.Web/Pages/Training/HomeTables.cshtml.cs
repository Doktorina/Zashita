using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class HomeTablesModel : PageModel
{
    private readonly OrganizationStore _organizations;

    public HomeTablesModel(OrganizationStore organizations)
    {
        _organizations = organizations;
    }

    public string Organization { get; private set; } = string.Empty;

    public void OnGet()
    {
        Organization = _organizations.GetSelectedOrganization(HttpContext);
        ViewData["Title"] = "Таблицы обучения";
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(Organization) ? "Обучение" : Organization;
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
    }
}

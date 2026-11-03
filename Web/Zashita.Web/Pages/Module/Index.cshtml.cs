using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Navigation;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Module;

[RequireLogin]
public class IndexModel : PageModel
{
    private readonly OrganizationStore _organizations;

    public IndexModel(OrganizationStore organizations)
    {
        _organizations = organizations;
    }

    [BindProperty(SupportsGet = true)]
    public string Slug { get; set; } = string.Empty;

    public ModulePageDefinition? Module { get; private set; }
    public string Subtitle { get; private set; } = string.Empty;
    public IReadOnlyList<ModuleSection> AllSections { get; private set; } = [];
    public string SelectedOrganization { get; private set; } = string.Empty;
    public bool IsAllOrganizationsSelected { get; private set; }
    public IReadOnlyList<string> VisibleOrganizations { get; private set; } = [];
    public bool ShowReportSummary { get; private set; }
    public string SummaryModuleKey { get; private set; } = string.Empty;
    public string ReportUrl { get; private set; } = string.Empty;
    public bool IsTrainingModule { get; private set; }

    public IActionResult OnGet()
    {
        Module = ModuleUiCatalog.ResolveModule(Slug);
        if (Module is null)
        {
            return Page();
        }

        ViewData["ActiveModule"] = Slug;
        ViewData["IsModulePage"] = true;
        Subtitle = ModuleUiCatalog.GetSubtitle(Module.ModuleName);
        AllSections = Module.LeftSections.Concat(Module.RightSections).ToList();
        IsAllOrganizationsSelected = _organizations.IsAllOrganizationsSelected(HttpContext);
        VisibleOrganizations = _organizations.GetVisibleOrganizations(HttpContext);
        SelectedOrganization = _organizations.GetSelectedOrganization(HttpContext);
        ShowReportSummary = ModuleHomeReports.TryGet(Slug, out var report);
        SummaryModuleKey = report?.ModuleName ?? string.Empty;
        ReportUrl = report?.Url ?? string.Empty;
        IsTrainingModule = TrainingItemRouter.IsTrainingModule(Slug);
        return Page();
    }
}

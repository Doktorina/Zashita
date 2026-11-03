using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages;

[RequireLogin]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class TasksModel : PageModel
{
    private readonly HomeDeadlinesService _deadlines;
    private readonly OrganizationStore _orgs;

    public TasksModel(HomeDeadlinesService deadlines, OrganizationStore orgs)
    {
        _deadlines = deadlines;
        _orgs = orgs;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<HomeDeadlineSection> Sections { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int OverdueCount { get; private set; }

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["Title"] = "Сроки и задачи";
        ViewData["Subtitle"] = Organization;
        ViewData["IsHome"] = false;

        var items = _deadlines.Load(Organization);
        Sections = _deadlines.GroupBySection(items);
        TotalCount = items.Count;
        OverdueCount = items.Count(i =>
            string.Equals(i.BadgeKind, "danger", StringComparison.OrdinalIgnoreCase));
    }
}

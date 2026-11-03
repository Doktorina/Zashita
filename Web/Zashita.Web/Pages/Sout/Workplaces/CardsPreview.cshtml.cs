using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Workplaces;

[RequireLogin]
public class CardsPreviewModel : PageModel
{
    private readonly WorkplaceStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public CardsPreviewModel(
        WorkplaceStore store,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public int CardCount { get; private set; }
    public string PreviewHtml { get; private set; } = string.Empty;

    public void OnGet(string? ids = null)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;

        var all = _store.LoadByOrganization(Organization)
            .Where(w => !w.IsGroup)
            .ToList();

        List<WorkplaceRecord> selected;
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var idSet = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            selected = all.Where(w => idSet.Contains(w.Id)).ToList();
        }
        else
        {
            selected = all;
        }

        CardCount = selected.Count;
        var changed = false;
        foreach (var workplace in selected)
        {
            if (SoutCardPrintBuilder.EnsurePreparedDate(workplace))
            {
                workplace.ModifiedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (changed)
        {
            _store.SaveForOrganization(Organization, all);
        }

        PreviewHtml = SoutCardPrintBuilder.BuildMany(
            selected,
            Organization,
            _employees.LoadForReport(Organization));
    }
}

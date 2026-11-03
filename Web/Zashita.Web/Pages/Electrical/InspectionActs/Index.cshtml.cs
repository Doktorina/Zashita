using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Compliance;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Electrical.InspectionActs;

[RequireLogin]
public class IndexModel : PageModel
{
    private readonly InspectionActStore _store;
    private readonly OrganizationStore _orgs;

    public IndexModel(InspectionActStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<InspectionActRecord> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public InspectionActRecord? SelectedPrintRecord { get; private set; }

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Акт проверки";
        ViewData["Subtitle"] = Organization;

        var rows = _store.LoadByOrganization(Organization);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var token = Q.Trim();
            rows = rows.Where(x =>
                    x.Number.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    x.ConstructionObject.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    x.Contractor.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    x.RepresentativeName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                    x.InspectorName.Contains(token, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Rows = rows;
        if (!string.IsNullOrWhiteSpace(PrintId))
        {
            SelectedPrintRecord = _store.GetById(PrintId);
        }
    }

    public IActionResult OnPostDelete(string deleteId)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage();
    }
}

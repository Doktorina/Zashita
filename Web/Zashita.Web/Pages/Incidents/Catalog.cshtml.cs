using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Incidents;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Incidents;

[RequireLogin]
public class CatalogModel : PageModel
{
    private readonly IncidentCatalogStore _store;
    private readonly OrganizationStore _orgs;

    public CatalogModel(IncidentCatalogStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    public string Organization { get; private set; } = string.Empty;
    public string Kind { get; private set; } = IncidentCatalogStore.KindKey(IncidentCatalogKind.IncidentTypes);
    public string PageTitle { get; private set; } = string.Empty;
    public IReadOnlyList<IncidentCatalogItem> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? EditId { get; set; }
    [BindProperty] public string Code { get; set; } = string.Empty;
    [BindProperty] public string Name { get; set; } = string.Empty;

    public void OnGet(string? kind)
    {
        Kind = ParseKind(kind);
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "несчастные-случаи";
        ViewData["IsModulePage"] = true;
        PageTitle = IncidentCatalogStore.KindTitle(ParseKindEnum(Kind));
        Rows = _store.LoadByKind(ParseKindEnum(Kind));

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var row = Rows.FirstOrDefault(r => r.Id == EditId);
            if (row is not null)
            {
                Code = row.Code;
                Name = row.Name;
            }
        }
    }

    public IActionResult OnPostSave(string? kind)
    {
        Kind = ParseKind(kind);
        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        _store.Upsert(new IncidentCatalogItem(id, Kind, Code.Trim(), Name.Trim()));
        return RedirectToPage(new { kind = Kind, editId = id });
    }

    public IActionResult OnPostDelete(string deleteId, string? kind)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage(new { kind = ParseKind(kind) });
    }

    private static string ParseKind(string? kind) =>
        string.IsNullOrWhiteSpace(kind)
            ? IncidentCatalogStore.KindKey(IncidentCatalogKind.IncidentTypes)
            : kind.Trim();

    private static IncidentCatalogKind ParseKindEnum(string kind) =>
        Enum.TryParse<IncidentCatalogKind>(kind, true, out var parsed)
            ? parsed
            : IncidentCatalogKind.IncidentTypes;
}

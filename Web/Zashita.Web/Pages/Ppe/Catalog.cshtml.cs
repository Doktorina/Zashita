using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class CatalogModel : PageModel
{
    private readonly PpeReferenceCatalogStore _store;
    private readonly OrganizationStore _orgs;

    public CatalogModel(PpeReferenceCatalogStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    public string Organization { get; private set; } = string.Empty;
    public PpeReferenceCatalogKind KindEnum { get; private set; } = PpeReferenceCatalogKind.ProtectiveProperties;
    public string Kind { get; private set; } = PpeReferenceCatalogKinds.Key(PpeReferenceCatalogKind.ProtectiveProperties);
    public string PageTitle { get; private set; } = string.Empty;
    public IReadOnlyList<PpeReferenceCatalogItem> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? EditId { get; set; }
    [BindProperty] public string Code { get; set; } = string.Empty;
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string Note { get; set; } = string.Empty;

    public void OnGet(string? kind)
    {
        KindEnum = PpeReferenceCatalogKinds.Parse(kind);
        Kind = PpeReferenceCatalogKinds.Key(KindEnum);
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        PageTitle = PpeReferenceCatalogKinds.Title(KindEnum);
        Rows = _store.LoadByKind(KindEnum);

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var row = Rows.FirstOrDefault(r => r.Id == EditId);
            if (row is not null)
            {
                Code = row.Code;
                Name = row.Name;
                Note = row.Note ?? string.Empty;
            }
        }
    }

    public IActionResult OnPostSave(string? kind)
    {
        KindEnum = PpeReferenceCatalogKinds.Parse(kind);
        Kind = PpeReferenceCatalogKinds.Key(KindEnum);
        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        _store.Upsert(new PpeReferenceCatalogItem(
            id,
            Kind,
            Code.Trim(),
            Name.Trim(),
            string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()));
        return RedirectToPage(new { kind = Kind, editId = id });
    }

    public IActionResult OnPostDelete(string deleteId, string? kind)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage(new { kind = PpeReferenceCatalogKinds.Key(PpeReferenceCatalogKinds.Parse(kind)) });
    }
}

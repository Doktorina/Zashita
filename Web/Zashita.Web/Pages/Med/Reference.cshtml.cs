using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class ReferenceModel : PageModel
{
    private readonly MedReferenceStore _store;
    private readonly MedContextService _med;

    public ReferenceModel(MedReferenceStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    public string Organization { get; private set; } = string.Empty;
    public string Category { get; private set; } = "factors";
    public string PageTitle { get; private set; } = "Справочник";
    public IReadOnlyList<MedReferenceEntry> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string? Details { get; set; }

    public void OnGet(string? category)
    {
        Category = string.IsNullOrWhiteSpace(category) ? "factors" : category.Trim().ToLowerInvariant();
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        PageTitle = ResolveTitle(Category);
        Rows = _store.LoadByCategory(Category);

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var row = Rows.FirstOrDefault(r => r.Id == EditId);
            if (row is not null)
            {
                Name = row.Name;
                Details = row.Details;
            }
        }
    }

    public IActionResult OnPostSave(string? category)
    {
        Category = string.IsNullOrWhiteSpace(category) ? "factors" : category.Trim().ToLowerInvariant();
        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId;
        _store.Upsert(new MedReferenceEntry(id, Category, Name.Trim(), Details?.Trim()));
        return RedirectToPage(new { category = Category, editId = id });
    }

    public IActionResult OnPostDelete(string deleteId, string? category)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            _store.Delete(deleteId);
        }

        return RedirectToPage(new { category = category ?? "factors" });
    }

    private static string ResolveTitle(string category) => category switch
    {
        "factors" => "Перечень факторов и видов работ",
        "factor-sets" => "Наборы факторов и видов работ",
        "contraindications" => "Медицинские противопоказания",
        "shift-types" => "Виды ежесменных медосмотров",
        "shift-legend" => "Условные обозначения",
        _ => "Справочник"
    };
}

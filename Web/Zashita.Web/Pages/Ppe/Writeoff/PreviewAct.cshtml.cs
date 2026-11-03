using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.Writeoff;

[RequireLogin]
public class PreviewActModel : PageModel
{
    private readonly PpeWriteoffStore _store;

    public PreviewActModel(PpeWriteoffStore store) => _store = store;

    public PpeActRecord? Record { get; private set; }
    public string Id { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }
    public string ActTitle => "МБ-8 (акт на списание)";

    public IActionResult OnGet(string id)
    {
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;

        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;

        var row = _store.GetById(id);
        if (row is null)
        {
            NotFound = true;
            return Page();
        }

        Record = row;
        PreviewHtml = PpeAcceptancePrintBuilder.BuildWriteoffMb8Body(row);
        return Page();
    }
}

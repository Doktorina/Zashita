using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.Changes;

[RequireLogin]
public class PreviewActModel : PageModel
{
    private readonly PpeChangesStore _store;

    public PreviewActModel(PpeChangesStore store) => _store = store;

    public PpeActRecord? Record { get; private set; }
    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = "all";
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }
    public string ActTitle => Mode == "single"
        ? "Акт осмотра (одна номенклатура)"
        : "Акт осмотра спецодежды (все номенклатуры)";

    public IActionResult OnGet(string id, string? mode)
    {
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;

        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        Mode = string.Equals(mode, "single", StringComparison.OrdinalIgnoreCase) ? "single" : "all";

        var row = _store.GetById(id);
        if (row is null)
        {
            NotFound = true;
            return Page();
        }

        Record = row;
        PreviewHtml = PpeAcceptancePrintBuilder.BuildInspectionActBody(
            row,
            "Изменения спецодежды, СИЗ",
            singleLine: Mode == "single");
        return Page();
    }
}

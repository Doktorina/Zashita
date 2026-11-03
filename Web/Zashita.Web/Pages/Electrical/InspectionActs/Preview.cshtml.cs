using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Compliance;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Electrical.InspectionActs;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly InspectionActStore _store;

    public PreviewModel(InspectionActStore store) => _store = store;

    public InspectionActRecord? Record { get; private set; }
    public string Id { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }

    public IActionResult OnGet(string id)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Предпросмотр акта проверки";

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
        ViewData["Subtitle"] = $"№ {row.Number} от {row.Date:dd.MM.yyyy}";
        PreviewHtml = InspectionActPrintBuilder.BuildBody(row);
        return Page();
    }
}

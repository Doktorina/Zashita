using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.Changes;

[RequireLogin]
public class PrintActModel : PageModel
{
    private readonly PpeChangesStore _store;

    public PrintActModel(PpeChangesStore store) => _store = store;

    public string Html { get; private set; } = string.Empty;

    public IActionResult OnGet(string id, string? mode)
    {
        var row = _store.GetById(id);
        if (row is null)
        {
            return RedirectToPage("/Ppe/Changes");
        }

        var single = string.Equals(mode, "single", StringComparison.OrdinalIgnoreCase);
        Html = PpeAcceptancePrintBuilder.BuildInspectionAct(row, "Изменения спецодежды, СИЗ", single);
        return Page();
    }
}

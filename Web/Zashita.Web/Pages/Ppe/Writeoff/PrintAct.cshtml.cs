using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.Writeoff;

[RequireLogin]
public class PrintActModel : PageModel
{
    private readonly PpeWriteoffStore _store;

    public PrintActModel(PpeWriteoffStore store) => _store = store;

    public string Html { get; private set; } = string.Empty;

    public IActionResult OnGet(string id)
    {
        var row = _store.GetById(id);
        if (row is null)
        {
            return RedirectToPage("/Ppe/Writeoff");
        }

        Html = PpeAcceptancePrintBuilder.BuildWriteoffMb8(row);
        return Page();
    }
}

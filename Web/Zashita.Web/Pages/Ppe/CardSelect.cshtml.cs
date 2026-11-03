using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class CardSelectModel : PageModel
{
  public IReadOnlyList<string> CardTypes => PpeCardTypes.All;

  public IActionResult OnPost(string documentName)
  {
    if (string.IsNullOrWhiteSpace(documentName) || !PpeCardTypes.All.Contains(documentName))
    {
      return Page();
    }

    SiosCardWizardSession.Clear(HttpContext.Session);
    return RedirectToPage("/Ppe/SiosCard/Index", new { doc = documentName });
  }
}

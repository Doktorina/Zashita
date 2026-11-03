using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

/// <summary>Мастер «Внесение изменений» — шаг 1: реестр опасностей.</summary>
[RequireLogin]
public class ChangesModel : PageModel
{
    public IActionResult OnGet() =>
        RedirectToPage("/Risks/HazardCatalog", new { flow = "changes" });
}

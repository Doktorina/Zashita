using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

/// <summary>Старый URL справочника «Виды работ» — страница убрана из меню.</summary>
[RequireLogin]
public class WorkTypesModel : PageModel
{
    public IActionResult OnGet() =>
        RedirectToPage("/Training/WorkTypeMapping");
}

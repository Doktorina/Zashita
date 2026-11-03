using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class QualificationModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Training/Standards");
}

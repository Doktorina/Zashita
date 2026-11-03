using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Reports;

[RequireLogin]
public class WithoutAssessmentModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IActionResult OnGet() =>
        RedirectToPage("/Risks/AssessmentList", new
        {
            needFilter = "notconducted",
            q = Q
        });
}

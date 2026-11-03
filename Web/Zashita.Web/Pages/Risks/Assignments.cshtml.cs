using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks;

/// <summary>Закрепление сотрудников в модуле «Риски» отключено — редирект на карты оценки.</summary>
[RequireLogin]
public class AssignmentsModel : PageModel
{
    public IActionResult OnGet() =>
        RedirectToPage("/Risks/Workplaces", new { view = "assessment" });

    public IActionResult OnPost() =>
        RedirectToPage("/Risks/Workplaces", new { view = "assessment" });
}

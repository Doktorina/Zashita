using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

/// <summary>Закрепление сотрудников убрано из меню — редирект на отчёт по СОУТ.</summary>
[RequireLogin]
public class AssignmentsModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Sout/Status");

    public IActionResult OnPost() => RedirectToPage("/Sout/Status");
}

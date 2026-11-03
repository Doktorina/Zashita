using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

[RequireLogin]
public class ConditionsClassesModel : PageModel
{
    private readonly SoutConditionsClassStore _store;
    private readonly OrganizationStore _orgs;

    public ConditionsClassesModel(SoutConditionsClassStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    public string Organization { get; private set; } = string.Empty;

    public List<string> Values { get; private set; } = [];

    public string? Message { get; private set; }

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Классы условий труда";
        ViewData["Subtitle"] = "общий для всех ЮЛ";

        Message = TempData["Message"] as string;
        Values = _store.Load().ToList();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Классы условий труда";
        ViewData["Subtitle"] = "общий для всех ЮЛ";

        var values = Request.Form["Value"]
            .Select(v => v.ToString() ?? string.Empty)
            .ToList();
        _store.Save(values);

        TempData["Message"] = "Справочник классов условий труда сохранён.";
        return RedirectToPage();
    }
}


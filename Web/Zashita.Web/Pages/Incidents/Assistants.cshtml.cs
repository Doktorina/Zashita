using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Incidents;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Incidents;

[RequireLogin]
public class AssistantsModel : PageModel
{
    public IReadOnlyList<IncidentAssistantEntry> Entries => IncidentsAssistantsCatalog.Entries;
    public string StateDate => DateTime.Today.ToString("dd.MM.yyyy");

    public void OnGet()
    {
        ViewData["ActiveModule"] = "несчастные-случаи";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Помощники по несчастным случаям";
        ViewData["Subtitle"] = "Несчастные случаи и микротравмы";
    }
}

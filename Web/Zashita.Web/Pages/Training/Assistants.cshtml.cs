using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class AssistantsModel : PageModel
{
    public IReadOnlyList<TrainingAssistantEntry> Entries => TrainingAssistantsCatalog.Entries;
    public string StateDate => DateTime.Today.ToString("dd.MM.yyyy");

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Помощники по обучению";
        ViewData["Subtitle"] = "Обучение, инструктажи, проверка знаний, стажировка";
    }
}

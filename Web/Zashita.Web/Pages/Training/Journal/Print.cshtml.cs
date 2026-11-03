using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training.Journals;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training.Journal;

[RequireLogin]
public class PrintModel : PageModel
{
    private readonly TrainingJournalStore _journalStore;

    public PrintModel(TrainingJournalStore journalStore) => _journalStore = journalStore;

    public string Html { get; private set; } = string.Empty;

    public IActionResult OnGet(TrainingJournalKind kind, bool? showTitlePage)
    {
        TrainingJournalDefinition definition;
        try
        {
            definition = TrainingJournalRegistry.Get(kind);
        }
        catch
        {
            return RedirectToPage("/Training/Reports");
        }

        var saved = _journalStore.Load(kind);
        var parameters = saved.Params;
        var entries = saved.Entries;
        var show = showTitlePage ?? parameters.ShowTitlePage;

        Html = TrainingJournalPrintBuilder.BuildHtmlDocument(definition, entries, parameters, show);
        return Page();
    }
}

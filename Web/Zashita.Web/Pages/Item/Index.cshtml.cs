using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Excel;
using Zashita.Core.Navigation;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Item;

[RequireLogin]
public class IndexModel : PageModel
{
    private readonly ExcelWorkbookStore _store;

    public IndexModel(ExcelWorkbookStore store)
    {
        _store = store;
    }

    [BindProperty(SupportsGet = true)]
    public string Module { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Item { get; set; } = string.Empty;

    public ModulePageDefinition? ModuleDef { get; private set; }
    public string ModuleSlug => Module;
    public string? ItemTitle { get; private set; }
    public bool HasWorkbook => _store.HasWorkbook;
    public string? PreviewSheet { get; private set; }
    public string PreviewHtml { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        ModuleDef = ModuleUiCatalog.ResolveModule(Module);
        if (ModuleDef is null)
        {
            return Page();
        }

        ViewData["ActiveModule"] = Module;

        ItemTitle = ModuleMenuRegistry.EnumerateAllItemTitles(ModuleDef)
            .FirstOrDefault(title => Zashita.Core.Navigation.ModuleSlug.ToSlug(title) == Item);

        if (PpeItemRouter.TryResolveRedirect(Module, ItemTitle, Item, out var ppeUrl) && ppeUrl is not null)
        {
            return Redirect(ppeUrl);
        }

        if (TrainingItemRouter.TryResolveRedirect(Module, ItemTitle, Item, out var trainingUrl))
        {
            return Redirect(trainingUrl!);
        }

        if (MedItemRouter.TryResolveRedirect(Module, ItemTitle, Item, out var medUrl))
        {
            return Redirect(medUrl!);
        }

        if (IncidentsItemRouter.TryResolveRedirect(Module, ItemTitle, Item, out var incidentsUrl))
        {
            return Redirect(incidentsUrl!);
        }

        if (SoutItemRouter.TryResolveRedirect(Module, ItemTitle, Item, out var soutUrl))
        {
            return Redirect(soutUrl!);
        }

        if (SuotItemRouter.TryResolveRedirect(Module, ItemTitle, Item, out var suotUrl))
        {
            return Redirect(suotUrl!);
        }

        if (RisksItemRouter.TryResolveRedirect(Module, ItemTitle, Item, out var risksUrl) && !string.IsNullOrWhiteSpace(risksUrl))
        {
            return Redirect(risksUrl);
        }

        if (ElectricalItemRouter.TryResolveRedirect(Module, ItemTitle, Item, out var electricalUrl) && !string.IsNullOrWhiteSpace(electricalUrl))
        {
            return Redirect(electricalUrl);
        }

        if (HasWorkbook && !string.IsNullOrWhiteSpace(ItemTitle))
        {
            PreviewSheet = _store.SheetNames.FirstOrDefault(s =>
                string.Equals(s, ItemTitle, StringComparison.OrdinalIgnoreCase) ||
                Zashita.Core.Navigation.ModuleSlug.ToSlug(s) == Item);

            if (PreviewSheet is not null)
            {
                PreviewHtml = _store.BuildHtmlTable(PreviewSheet, 50);
            }
        }

        return Page();
    }
}

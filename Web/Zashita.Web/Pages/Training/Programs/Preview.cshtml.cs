using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training.Programs;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly TrainingProgramStore _store;
    private readonly OrganizationStore _orgs;

    public PreviewModel(TrainingProgramStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = "program";
    public string Title { get; private set; } = "Программа";
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }

    public IActionResult OnGet(string id, string? mode)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;

        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        Mode = NormalizeMode(mode);

        var program = _store.GetById(id);
        if (program is null || program.IsGroup)
        {
            NotFound = true;
            return Page();
        }

        var organization = _orgs.GetSelectedOrganization(HttpContext);
        Title = Mode switch
        {
            "order" => "Приказ об утверждении программы инструктажа",
            "briefing" => "Программа инструктажа",
            "detailed" => "Программа подробная",
            "detailed-groups" => "Программа подробная (с группами)",
            "custom" => "Настраиваемый комплект документов",
            _ => "Программа"
        };

        PreviewHtml = TrainingProgramPrintBuilder.Build(program, Mode, organization);
        return Page();
    }

    private static string NormalizeMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "order" => "order",
        "briefing" => "briefing",
        "detailed" => "detailed",
        "detailed-groups" => "detailed-groups",
        "custom" => "custom",
        _ => "program"
    };
}

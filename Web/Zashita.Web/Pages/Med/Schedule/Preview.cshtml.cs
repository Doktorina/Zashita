using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.Schedule;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly MedScheduleStore _store;
    private readonly MedContextService _med;

    public PreviewModel(MedScheduleStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = MedSchedulePrintModes.General;
    public string Title { get; private set; } = string.Empty;
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }

    public IActionResult OnGet(string id, string? mode) => Load(id, mode);

    public IActionResult OnPostSave(string id, string? mode)
    {
        var doc = _store.GetById(id);
        if (doc is null)
        {
            NotFound = true;
            return Page();
        }

        doc.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keys = Request.Form["overrideKey"];
        var values = Request.Form["overrideValue"];
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            doc.PrintOverrides[key] = i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;
        }

        _store.Upsert(doc);
        Message = "Изменения сохранены.";
        return Load(id, mode);
    }

    private IActionResult Load(string id, string? mode)
    {
        var doc = _store.GetById(id);
        if (doc is null)
        {
            NotFound = true;
            return Page();
        }

        var organization = string.IsNullOrWhiteSpace(doc.Organization)
            ? _med.GetOrganization(HttpContext)
            : doc.Organization;
        MedDashboardBuilder.EnrichScheduleLinesFromEmployees(doc, _med.GetEmployees(organization));

        Id = id;
        Mode = MedSchedulePrintModes.Normalize(mode);
        Title = MedSchedulePrintModes.Title(Mode);
        PreviewHtml = MedSchedulePrintBuilder.Build(doc, Mode);
        return Page();
    }
}

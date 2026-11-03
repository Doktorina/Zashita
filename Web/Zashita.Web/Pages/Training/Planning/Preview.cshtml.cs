using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training.Planning;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly TrainingPlanningStore _store;
    private readonly OrganizationRequisitesStore _requisites;
    private readonly OrganizationEmployeesService _employees;

    public PreviewModel(
        TrainingPlanningStore store,
        OrganizationRequisitesStore requisites,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _requisites = requisites;
        _employees = employees;
    }

    public string Id { get; private set; } = string.Empty;
    public string Mode { get; private set; } = TrainingApplicationPrintModes.ApplicationV2;
    public string Title { get; private set; } = "Заявка на обучение";
    public string DownloadFileName { get; private set; } = "Заявка.doc";
    public string PreviewHtml { get; private set; } = string.Empty;
    public bool NotFound { get; private set; }
    public string? Message { get; private set; }

    public IActionResult OnGet(string id, string? mode)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        return Load(id, mode);
    }

    public IActionResult OnPostSave(string id, string? mode)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;

        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        Mode = TrainingApplicationPrintModes.Normalize(mode);
        var record = _store.GetById(id);
        if (record is null)
        {
            NotFound = true;
            return Page();
        }

        record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keys = Request.Form["overrideKey"];
        var values = Request.Form["overrideValue"];
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;
            record.PrintOverrides[key] = value;
        }

        _store.Upsert(record);
        Message = "Изменения в форме сохранены.";
        Title = TrainingApplicationPrintModes.Title(Mode);
        DownloadFileName = TrainingApplicationDownloadFileName.WithExtension(record);
        PreviewHtml = BuildHtml(record);
        return Page();
    }

    private IActionResult Load(string id, string? mode)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            NotFound = true;
            return Page();
        }

        Id = id;
        Mode = TrainingApplicationPrintModes.Normalize(mode);
        var record = _store.GetById(id);
        if (record is null)
        {
            NotFound = true;
            return Page();
        }

        Title = TrainingApplicationPrintModes.Title(Mode);
        DownloadFileName = TrainingApplicationDownloadFileName.WithExtension(record);
        PreviewHtml = BuildHtml(record);
        return Page();
    }

    private string BuildHtml(TrainingPlanningRecord record)
    {
        EnrichForPrint(record);
        return TrainingApplicationPrintBuilder.Build(record, Mode);
    }

    private void EnrichForPrint(TrainingPlanningRecord record) =>
        TrainingApplicationPrintEnrichment.Apply(record, _requisites, _employees);
}

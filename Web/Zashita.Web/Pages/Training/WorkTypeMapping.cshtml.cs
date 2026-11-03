using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class WorkTypeMappingModel : PageModel
{
    private readonly TrainingMappingStore _mapping;
    private readonly TrainingKindStore _kinds;
    private readonly TrainingProgramStore _programs;

    public WorkTypeMappingModel(
        TrainingMappingStore mapping,
        TrainingKindStore kinds,
        TrainingProgramStore programs)
    {
        _mapping = mapping;
        _kinds = kinds;
        _programs = programs;
    }

    public List<TrainingKindProgramMappingRow> Rows { get; set; } = [];
    public IReadOnlyList<string> ProgramOptions { get; private set; } = [];
    public string? Message { get; private set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Соответствие видов и программ обучения";
        ViewData["Subtitle"] = "Сопоставление видов обучения с программами (курсами).";
        LoadPage();
    }

    public IActionResult OnPostSave()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Соответствие видов и программ обучения";
        ViewData["Subtitle"] = "Сопоставление видов обучения с программами (курсами).";

        var kindIds = Request.Form["KindId"];
        var kindNames = Request.Form["KindName"];
        var programs = Request.Form["TargetProgram"];
        var rows = new List<TrainingKindProgramMappingRow>();

        for (var i = 0; i < kindNames.Count; i++)
        {
            var kindName = kindNames[i].ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(kindName))
            {
                continue;
            }

            rows.Add(new TrainingKindProgramMappingRow
            {
                KindId = i < kindIds.Count ? kindIds[i].ToString()?.Trim() ?? string.Empty : string.Empty,
                KindName = kindName,
                ProgramName = i < programs.Count ? programs[i].ToString()?.Trim() ?? string.Empty : string.Empty
            });
        }

        _mapping.Save(TrainingMappingKind.WorkTypes, TrainingKindProgramMapping.ToStorage(rows));
        Message = "Соответствие сохранено.";
        LoadPage();
        return Page();
    }

    private void LoadPage()
    {
        var saved = _mapping.Load(TrainingMappingKind.WorkTypes);
        Rows = TrainingKindProgramMapping.BuildRows(_kinds.Load(), saved);
        ProgramOptions = _programs.Load()
            .Where(program => !program.IsGroup)
            .Select(program => program.Name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public abstract class TrainingMappingPageModel : PageModel
{
    private readonly TrainingMappingStore _store;

    protected TrainingMappingPageModel(TrainingMappingStore store) => _store = store;

    protected abstract TrainingMappingKind MappingKind { get; }
    protected abstract string PageTitle { get; }
    protected abstract string PageSubtitle { get; }

    public List<TrainingProgramMappingLine> Lines { get; set; } = [];
    public List<TrainingMappingGroup> Groups => TrainingMappingGroups.Build(Lines);
    public string? Message { get; private set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = PageTitle;
        ViewData["Subtitle"] = PageSubtitle;
        Lines = _store.Load(MappingKind);
    }

    public IActionResult OnPostSave()
    {
        var ids = Request.Form["Id"];
        var sourcePrograms = Request.Form["SourceProgram"];
        var targetKinds = Request.Form["TargetKind"];
        var targetPrograms = Request.Form["TargetProgram"];
        var deletes = Request.Form["Delete"];

        var list = new List<TrainingProgramMappingLine>();
        for (var i = 0; i < sourcePrograms.Count; i++)
        {
            if (i < deletes.Count && deletes[i].ToString() == "true")
            {
                continue;
            }

            var source = sourcePrograms[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            list.Add(new TrainingProgramMappingLine
            {
                Id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i]) ? ids[i].ToString()! : Guid.NewGuid().ToString("N"),
                SourceProgram = source,
                TargetKind = i < targetKinds.Count ? targetKinds[i].ToString()?.Trim() ?? "" : "",
                TargetProgram = i < targetPrograms.Count ? targetPrograms[i].ToString()?.Trim() ?? "" : ""
            });
        }

        _store.Save(MappingKind, list);
        Message = "Соответствие сохранено.";
        Lines = list;
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = PageTitle;
        ViewData["Subtitle"] = PageSubtitle;
        return Page();
    }
}

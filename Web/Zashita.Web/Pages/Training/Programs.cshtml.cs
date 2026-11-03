using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class ProgramsModel : PageModel
{
    private readonly TrainingProgramStore _store;

    public ProgramsModel(TrainingProgramStore store) => _store = store;

    [BindProperty(SupportsGet = true)]
    public string? PrintId { get; set; }

    public List<TrainingProgramCourseNode> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<TrainingProgramCourseNode>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public string? Message { get; private set; }
    public string? ActivePrintId => PrintId;
    public TrainingProgramCourseNode? SelectedPrintProgram =>
        string.IsNullOrWhiteSpace(PrintId) ? null : _store.GetById(PrintId);

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Message = TempData["Message"]?.ToString();
        Nodes = _store.Load();
    }

    public IActionResult OnPostSave()
    {
        var existingById = _store.Load()
            .GroupBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var list = new List<TrainingProgramCourseNode>();
        var names = Request.Form["Name"];
        var sections = Request.Form["Section"];
        var hours = Request.Form["TotalHours"];
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var deletes = Request.Form["Delete"];

        for (var i = 0; i < names.Count; i++)
        {
            if (IsMarkedForDelete(deletes, i))
            {
                continue;
            }

            var name = names[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var rowId = i < ids.Count ? ids[i].ToString()?.Trim() ?? "" : "";
            var id = !string.IsNullOrWhiteSpace(rowId) ? rowId : Guid.NewGuid().ToString("N");
            existingById.TryGetValue(id, out var previous);
            decimal.TryParse(i < hours.Count ? hours[i].ToString() : "0", out var h);
            list.Add(new TrainingProgramCourseNode
            {
                Id = id,
                ParentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null),
                IsGroup = i < isGroups.Count && isGroups[i].ToString() == "true",
                Name = name,
                Code = previous?.Code ?? (i + 1).ToString("000000"),
                Section = i < sections.Count ? sections[i].ToString() ?? "Охрана труда" : "Охрана труда",
                ApprovalDate = previous?.ApprovalDate,
                TotalHours = h
            });
        }

        _store.Save(list);
        TempData["Message"] = "Программы (курсы) сохранены.";
        return RedirectToPage(new { printId = PrintId });
    }

    private static bool IsMarkedForDelete(IReadOnlyList<string> deletes, int index) =>
        index < deletes.Count &&
        string.Equals(deletes[index].ToString(), "true", StringComparison.OrdinalIgnoreCase);
}

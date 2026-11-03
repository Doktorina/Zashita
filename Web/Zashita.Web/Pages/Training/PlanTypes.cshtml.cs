using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class PlanTypesModel : PageModel
{
    private readonly TrainingPlanTypeStore _store;

    public PlanTypesModel(TrainingPlanTypeStore store) => _store = store;

    public List<TrainingPlanTypeNode> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<TrainingPlanTypeNode>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public string? Message { get; private set; }

    public void OnGet()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Nodes = _store.Load();
    }

    public IActionResult OnPostSave()
    {
        var names = Request.Form["Name"];
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var routeUrls = Request.Form["RouteUrl"];
        var deletes = Request.Form["Delete"];

        var list = new List<TrainingPlanTypeNode>();
        for (var i = 0; i < names.Count; i++)
        {
            if (i < deletes.Count && deletes[i].ToString() == "true")
            {
                continue;
            }

            var name = names[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var route = i < routeUrls.Count ? routeUrls[i].ToString()?.Trim() : null;
            list.Add(new TrainingPlanTypeNode
            {
                Id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i]) ? ids[i].ToString()! : Guid.NewGuid().ToString("N"),
                ParentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null),
                IsGroup = i < isGroups.Count && isGroups[i].ToString() == "true",
                Name = name,
                RouteUrl = string.IsNullOrWhiteSpace(route) ? null : route
            });
        }

        _store.Save(list);
        Message = "Виды планов сохранены.";
        Nodes = list;
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        return Page();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Training;

[RequireLogin]
public class TreeModel : PageModel
{
    private readonly TrainingSimpleTreeStore _store;

    public TreeModel(TrainingSimpleTreeStore store) => _store = store;

    public TrainingSimpleTreeKind Kind { get; private set; } = TrainingSimpleTreeKind.Topics;
    public string KindTitle { get; private set; } = "Темы";
    public List<TrainingSimpleTreeNode> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<TrainingSimpleTreeNode>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public bool IsWorkTypes => Kind == TrainingSimpleTreeKind.WorkTypes;
    public bool ShowExtraColumn => !IsWorkTypes;
    public string ExtraColumnTitle => Kind == TrainingSimpleTreeKind.Topics ? "Часы" : "Описание";
    public string? Message { get; private set; }

    public IActionResult OnGet(string? kind)
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        Kind = ResolveKind(kind, HttpContext.Request.Path.Value);
        KindTitle = TrainingSimpleTreeKindLabels.Title(Kind);
        Nodes = _store.Load(Kind);
        return Page();
    }

    public IActionResult OnPostSave(string? kind)
    {
        Kind = ResolveKind(kind, HttpContext.Request.Path.Value);
        KindTitle = TrainingSimpleTreeKindLabels.Title(Kind);
        var list = new List<TrainingSimpleTreeNode>();
        var names = Request.Form["Name"];
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var hours = Request.Form["Hours"];
        var descriptions = Request.Form["Description"];
        var deletes = Request.Form["Delete"];

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

            decimal? h = decimal.TryParse(i < hours.Count ? hours[i].ToString() : "", out var hv) ? hv : null;
            list.Add(new TrainingSimpleTreeNode
            {
                Id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i]) ? ids[i].ToString()! : Guid.NewGuid().ToString("N"),
                ParentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null),
                IsGroup = i < isGroups.Count && isGroups[i].ToString() == "true",
                Name = name,
                Code = (i + 1).ToString("000000"),
                Hours = h,
                Description = i < descriptions.Count ? descriptions[i].ToString() : null
            });
        }

        _store.Save(Kind, list);
        Message = $"{KindTitle}: сохранено.";
        Nodes = list;
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        return Page();
    }

    private static TrainingSimpleTreeKind ResolveKind(string? kind, string? path)
    {
        if (string.IsNullOrWhiteSpace(kind) &&
            path?.Contains("/WorkTypes", StringComparison.OrdinalIgnoreCase) == true)
        {
            return TrainingSimpleTreeKind.WorkTypes;
        }

        return TrainingSimpleTreeKindLabels.TryParse(kind) ?? TrainingSimpleTreeKind.Topics;
    }
}

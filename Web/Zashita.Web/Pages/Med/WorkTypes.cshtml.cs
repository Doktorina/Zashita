using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class WorkTypesModel : PageModel
{
    private readonly MedWorkTypeStore _store;
    private readonly MedContextService _med;

    public WorkTypesModel(MedWorkTypeStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public string Organization { get; private set; } = string.Empty;
    public List<MedWorkTypeNode> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<MedWorkTypeNode>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public string? Message { get; private set; }

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Виды работ";
        ViewData["Subtitle"] = Organization;
        Nodes = _store.LoadAll();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Nodes.Where(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                matched.Add(node.Id);
                AddAncestors(node, matched);
            }
            Nodes = Nodes.Where(n => matched.Contains(n.Id)).ToList();
        }
    }

    public IActionResult OnPostSave()
    {
        Organization = _med.GetOrganization(HttpContext);
        var existing = _store.LoadAll().ToDictionary(n => n.Id, StringComparer.Ordinal);
        var list = new List<MedWorkTypeNode>();
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var names = Request.Form["Name"];
        var deletes = Request.Form["Delete"];

        for (var i = 0; i < names.Count; i++)
        {
            if (i < deletes.Count && deletes[i].ToString() == "true") continue;
            var name = names[i].ToString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            var id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i]) ? ids[i].ToString()! : Guid.NewGuid().ToString("N");
            existing.TryGetValue(id, out var prev);
            list.Add(new MedWorkTypeNode
            {
                Id = id,
                ParentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null),
                IsGroup = i < isGroups.Count && isGroups[i].ToString() == "true",
                Name = name,
                Code = prev?.Code ?? "",
                ItemNumber = prev?.ItemNumber ?? "",
                Description = prev?.Description ?? "",
                FactorSetName = prev?.FactorSetName ?? ""
            });
        }

        _store.UpsertAll(list);
        Message = "Справочник сохранён.";
        OnGet();
        return Page();
    }

    private void AddAncestors(MedWorkTypeNode node, HashSet<string> matched)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId)) return;
        var parent = Nodes.FirstOrDefault(n => n.Id == node.ParentId);
        if (parent is null || !matched.Add(parent.Id)) return;
        AddAncestors(parent, matched);
    }
}

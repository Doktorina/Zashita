using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class FactorsModel : PageModel
{
    private readonly MedFactorStore _store;
    private readonly MedContextService _med;

    public FactorsModel(MedFactorStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public List<MedFactorNode> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<MedFactorNode>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Перечень факторов и видов работ";
        ViewData["Subtitle"] = Organization;
        Nodes = _store.LoadAll();

        if (TempData["Message"] is string tempMessage)
        {
            Message = tempMessage;
            IsError = TempData["IsError"] is true;
        }

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Nodes.Where(n =>
                         n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         n.ItemNumber.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                matched.Add(node.Id);
                AddAncestors(node, matched);
            }

            Nodes = Nodes.Where(n => matched.Contains(n.Id)).ToList();
        }
    }

    public IActionResult OnPostDelete(string id)
    {
        Organization = _med.GetOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["Message"] = "Не указана запись для удаления.";
            TempData["IsError"] = true;
            return RedirectToPage(new { q = Q });
        }

        _store.Delete(id);

        TempData["Message"] = "Запись удалена.";
        return RedirectToPage(new { q = Q });
    }

    private void AddAncestors(MedFactorNode node, HashSet<string> matched)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId))
        {
            return;
        }

        var parent = Nodes.FirstOrDefault(n => n.Id == node.ParentId);
        if (parent is null || !matched.Add(parent.Id))
        {
            return;
        }

        AddAncestors(parent, matched);
    }
}

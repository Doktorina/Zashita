using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med.Factors;

[RequireLogin]
public class PreviewModel : PageModel
{
    private readonly MedFactorStore _store;
    private readonly MedContextService _med;

    public PreviewModel(MedFactorStore store, MedContextService med)
    {
        _store = store;
        _med = med;
    }

    public string Organization { get; private set; } = string.Empty;
    public List<MedFactorNode> Nodes { get; private set; } = [];
    public List<TrainingCatalogTreeItem<MedFactorNode>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        Nodes = _store.LoadAll();
    }
}

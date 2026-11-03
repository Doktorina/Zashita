using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Changes;

/// <summary>Мастер «Внесение изменений» — шаг 2: выбор рабочего места.</summary>
[RequireLogin]
public class WorkplacesModel : PageModel
{
    private readonly RiskWorkplaceStore _store;
    private readonly OrganizationStore _orgs;

    public WorkplacesModel(RiskWorkplaceStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public List<TrainingCatalogTreeItem<WorkplaceRecord>> TreeItems { get; private set; } = [];

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Внесение изменений — выбор рабочего места";
        ViewData["Subtitle"] = Organization;

        var nodes = _store.LoadTreeByOrganization(Organization)
            .Where(n => n.IsGroup || n.IsUsed)
            .ToList();

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                if (node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    node.Profession.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    node.WorkplaceNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    node.Department.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(node.Id);
                    var parentId = node.ParentId;
                    while (!string.IsNullOrWhiteSpace(parentId))
                    {
                        if (!matched.Add(parentId))
                        {
                            break;
                        }

                        parentId = nodes.FirstOrDefault(n => n.Id == parentId)?.ParentId;
                    }
                }
            }

            nodes = nodes.Where(n => matched.Contains(n.Id)).ToList();
        }

        TreeItems = TrainingCatalogTree.Order(nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    }
}

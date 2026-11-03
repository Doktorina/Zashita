using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Electrical;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Electrical;

[RequireLogin]
public class CommissionsModel : PageModel
{
    private readonly ElectricalCommissionStore _store;
    private readonly OrganizationStore _orgs;

    public CommissionsModel(ElectricalCommissionStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public List<ElectricalCommissionRecord> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<ElectricalCommissionRecord>> TreeItems =>
        TrainingCatalogTree.Order(Nodes, n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name);
    public string? Message { get; private set; }

    public IActionResult OnGet()
    {
        LoadPage();
        return Page();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SetViewData();
        Nodes = _store.Load(Organization);
        var list = BuildRecordsFromForm();
        _store.Save(Organization, list);
        Message = "Изменения сохранены.";
        Nodes = list;
        return Page();
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SetViewData();
        Nodes = _store.Load(Organization);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Nodes)
            {
                if (MatchesQuery(node, query))
                {
                    matchedIds.Add(node.Id);
                    AddAncestors(node, matchedIds);
                }
            }

            Nodes = Nodes.Where(node => matchedIds.Contains(node.Id)).ToList();
        }
    }

    private void SetViewData()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Комиссия по ЭБ";
        ViewData["Subtitle"] = Organization;
    }

    private List<ElectricalCommissionRecord> BuildRecordsFromForm()
    {
        var existingById = Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var list = new List<ElectricalCommissionRecord>();
        var names = Request.Form["Name"];
        var ids = Request.Form["Id"];
        var parentIds = Request.Form["ParentId"];
        var isGroups = Request.Form["IsGroup"];
        var deletes = Request.Form["Delete"];

        for (var i = 0; i < names.Count; i++)
        {
            if (TrainingFormHelpers.IsMarkedForDelete(deletes, i))
            {
                continue;
            }

            var name = names[i].ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = i < ids.Count && !string.IsNullOrWhiteSpace(ids[i])
                ? ids[i].ToString()!
                : Guid.NewGuid().ToString("N");
            existingById.TryGetValue(id, out var existing);
            var isGroup = i < isGroups.Count && isGroups[i].ToString() == "true";
            var members = existing?.Members ?? [];
            var certifiedCount = existing?.CertifiedInRostechnadzorCount;
            var draft = new ElectricalCommissionRecord
            {
                Id = id,
                Organization = Organization,
                ParentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null),
                IsGroup = isGroup,
                Name = name,
                Composition = existing?.Composition ?? string.Empty,
                Members = members,
                OrderDate = existing?.OrderDate,
                OrderNumber = existing?.OrderNumber ?? string.Empty,
                CompositionApprovalDate = existing?.CompositionApprovalDate,
                CompositionCheckPeriodMonths = existing?.CompositionCheckPeriodMonths,
                CertifiedInRostechnadzorCount = certifiedCount,
                IsInactive = existing?.IsInactive ?? false,
                CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };
            draft.CompositionCompliance = isGroup
                ? string.Empty
                : ElectricalCommissionCalculator.SuggestCompliance(draft);
            list.Add(draft);
        }

        return list;
    }

    private static bool MatchesQuery(ElectricalCommissionRecord node, string query) =>
        (node.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (node.Composition?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (node.OrderNumber?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (node.Members ?? []).Any(m =>
            (m.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (m.Position?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

    private void AddAncestors(ElectricalCommissionRecord node, HashSet<string> matchedIds)
    {
        var parentId = node.ParentId;
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            if (!matchedIds.Add(parentId))
            {
                break;
            }

            var parent = Nodes.FirstOrDefault(n => string.Equals(n.Id, parentId, StringComparison.Ordinal));
            parentId = parent?.ParentId;
        }
    }
}

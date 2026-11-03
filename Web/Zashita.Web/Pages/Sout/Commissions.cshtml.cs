using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout;

[RequireLogin]
public class CommissionsModel : PageModel
{
    private readonly SoutCommissionStore _store;
    private readonly OrganizationStore _orgs;

    public CommissionsModel(SoutCommissionStore store, OrganizationStore orgs)
    {
        _store = store;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public List<SoutCommissionRecord> Nodes { get; set; } = [];
    public List<TrainingCatalogTreeItem<SoutCommissionRecord>> TreeItems =>
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
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Комиссии (СОУТ)";
        ViewData["Subtitle"] = Organization;

        _store.MigrateScopeSeparation(Organization);
        var all = _store.Load(Organization);
        Nodes = SoutCommissionScope.Filter(all, SoutCommissionScopeKind.Sout);
        var list = BuildRecordsFromForm();
        _store.SaveScope(Organization, list, SoutCommissionScopeKind.Sout);
        Message = "Изменения сохранены.";
        Nodes = list;
        return Page();
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Комиссии (СОУТ)";
        ViewData["Subtitle"] = Organization;
        _store.MigrateScopeSeparation(Organization);
        Nodes = SoutCommissionScope.Filter(_store.Load(Organization), SoutCommissionScopeKind.Sout);

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

    private List<SoutCommissionRecord> BuildRecordsFromForm()
    {
            var existingById = Nodes.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var list = new List<SoutCommissionRecord>();
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

            list.Add(new SoutCommissionRecord
            {
                Id = id,
                Organization = Organization,
                ParentId = TrainingFormHelpers.NormalizeParentId(i < parentIds.Count ? parentIds[i].ToString() : null),
                IsGroup = isGroup,
                Name = name,
                Composition = isGroup ? string.Empty : existing?.Composition ?? string.Empty,
                Members = isGroup ? [] : existing?.Members ?? [],
                OrderDate = isGroup ? null : existing?.OrderDate,
                OrderNumber = isGroup ? string.Empty : existing?.OrderNumber ?? string.Empty,
                CompositionApprovalDate = isGroup ? null : existing?.CompositionApprovalDate,
                CompositionCheckPeriodMonths = isGroup ? null : existing?.CompositionCheckPeriodMonths,
                CertifiedInRostechnadzorCount = isGroup ? null : existing?.CertifiedInRostechnadzorCount,
                CompositionCompliance = isGroup ? string.Empty : existing?.CompositionCompliance ?? string.Empty,
                IsInactive = existing?.IsInactive ?? false,
                PrintOverrides = isGroup
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : existing?.PrintOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            });
            if (!isGroup)
            {
                SoutCommissionMemberHelpers.SyncComposition(list[^1]);
            }
        }

        return list;
    }

    private static bool MatchesQuery(SoutCommissionRecord node, string query) =>
        node.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.Composition.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        node.OrderNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (node.Members ?? []).Any(m =>
            (m.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (m.Position?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (m.Status?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

    private void AddAncestors(SoutCommissionRecord node, HashSet<string> matchedIds)
    {
        if (string.IsNullOrWhiteSpace(node.ParentId))
        {
            return;
        }

        var parent = Nodes.FirstOrDefault(item => item.Id == node.ParentId);
        if (parent is null || !matchedIds.Add(parent.Id))
        {
            return;
        }

        AddAncestors(parent, matchedIds);
    }
}

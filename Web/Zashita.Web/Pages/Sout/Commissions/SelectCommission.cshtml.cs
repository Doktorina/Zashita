using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Commissions;

[RequireLogin]
public class SelectCommissionModel : PageModel
{
    private readonly SoutCommissionStore _commissions;
    private readonly OrganizationStore _orgs;

    public SelectCommissionModel(SoutCommissionStore commissions, OrganizationStore orgs)
    {
        _commissions = commissions;
        _orgs = orgs;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<CommissionRow> CommissionRows { get; private set; } = [];

    public void OnGet()
    {
        LoadPage();
    }

    public IActionResult OnPostOpen(string commissionId)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (string.IsNullOrWhiteSpace(commissionId))
        {
            LoadPage();
            return Page();
        }

        var all = _commissions.Load(Organization);
        var commission = SoutCommissionResolver.Resolve(Organization, all, commissionId);
        if (commission is null || commission.IsGroup)
        {
            LoadPage();
            return Page();
        }

        return RedirectToPage("/Sout/Commissions/Preview", new { id = commission.Id });
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Приказ о создании комиссии СОУТ — выбор комиссии";
        ViewData["Subtitle"] = Organization;

        var all = _commissions.Load(Organization);
        var selectable = SoutCommissionResolver.ListSelectable(Organization, all);
        var recommendedId = selectable.FirstOrDefault()?.Id;

        CommissionRows = selectable
            .Select(record =>
            {
                SoutCommissionMemberHelpers.SyncComposition(record);
                return new CommissionRow(
                    record.Id,
                    string.IsNullOrWhiteSpace(record.Name) ? "—" : record.Name.Trim(),
                    SoutCommissionListHelpers.ChairmanDisplay(record),
                    SoutCommissionListHelpers.MembersDisplay(record),
                    SoutCommissionListHelpers.OrderNumberAndDate(record),
                    string.Equals(record.Id, recommendedId, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    public sealed record CommissionRow(
        string Id,
        string Name,
        string Chairman,
        string Members,
        string OrderInfo,
        bool IsRecommended);
}

using Microsoft.AspNetCore.Mvc;
using Zashita.Web.Services;

namespace Zashita.Web.ViewComponents;

public sealed class StaffingOnboardingAlertsViewComponent : ViewComponent
{
    private readonly StaffingOnboardingQuery _query;

    public StaffingOnboardingAlertsViewComponent(StaffingOnboardingQuery query) =>
        _query = query;

    public IViewComponentResult Invoke(string? organization, string? moduleKey = null)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            return Content(string.Empty);
        }

        var items = _query.LoadOpen(organization, moduleKey, take: 12);
        if (items.Count == 0)
        {
            return Content(string.Empty);
        }

        return View(new StaffingOnboardingAlertsView(organization, moduleKey ?? "", items));
    }
}

public sealed record StaffingOnboardingAlertsView(
    string Organization,
    string ModuleKey,
    IReadOnlyList<StaffingOnboardingItem> Items);

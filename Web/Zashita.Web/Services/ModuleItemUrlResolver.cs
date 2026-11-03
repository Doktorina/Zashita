using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public static class ModuleItemUrlResolver
{
    public static string Resolve(string moduleSlug, string itemTitle)
    {
        if (RisksItemRouter.IsRisksModule(moduleSlug) &&
            RisksModuleLinks.TryResolve(itemTitle, out var risksUrl) &&
            !string.IsNullOrWhiteSpace(risksUrl))
        {
            return risksUrl;
        }

        if (PpeItemRouter.TryResolveRedirect(moduleSlug, itemTitle, out var ppeUrl) && !string.IsNullOrWhiteSpace(ppeUrl))
        {
            return ppeUrl;
        }

        if (TrainingItemRouter.TryResolveRedirect(moduleSlug, itemTitle, out var trainingUrl) && !string.IsNullOrWhiteSpace(trainingUrl))
        {
            return trainingUrl;
        }

        if (MedItemRouter.TryResolveRedirect(moduleSlug, itemTitle, itemSlug: null, out var medUrl) && !string.IsNullOrWhiteSpace(medUrl))
        {
            return medUrl;
        }

        if (IncidentsItemRouter.TryResolveRedirect(moduleSlug, itemTitle, itemSlug: null, out var incidentsUrl) && !string.IsNullOrWhiteSpace(incidentsUrl))
        {
            return incidentsUrl;
        }

        if (SoutItemRouter.TryResolveRedirect(moduleSlug, itemTitle, itemSlug: null, out var soutUrl) && !string.IsNullOrWhiteSpace(soutUrl))
        {
            return soutUrl;
        }

        if (SuotItemRouter.TryResolveRedirect(moduleSlug, itemTitle, itemSlug: null, out var suotUrl) && !string.IsNullOrWhiteSpace(suotUrl))
        {
            return suotUrl;
        }

        if (ElectricalItemRouter.TryResolveRedirect(moduleSlug, itemTitle, itemSlug: null, out var electricalUrl) &&
            !string.IsNullOrWhiteSpace(electricalUrl))
        {
            return electricalUrl;
        }

        return $"/item/{Uri.EscapeDataString(moduleSlug)}/{Uri.EscapeDataString(ModuleSlug.ToSlug(itemTitle))}";
    }
}

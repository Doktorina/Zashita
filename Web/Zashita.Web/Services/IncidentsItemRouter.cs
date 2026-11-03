using Zashita.Core.Incidents;
using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public static class IncidentsItemRouter
{
    private const string IncidentsSlug = "несчастные-случаи";

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, string? itemSlug, out string? url)
    {
        url = null;
        if (!IsIncidentsModule(moduleSlug))
        {
            return false;
        }

        foreach (var token in new[] { itemTitle, SlugToText(itemSlug) })
        {
            if (!string.IsNullOrWhiteSpace(token) && TryResolveByToken(token, out url))
            {
                return true;
            }
        }

        url = "/Incidents/Hub";
        return true;
    }

    private static string? SlugToText(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : slug.Replace('-', ' ');

    private static bool TryResolveByToken(string title, out string? url)
    {
        url = null;
        var lower = title.ToLowerInvariant().Replace('ё', 'е');

        if (lower.Contains("несчастн") && lower.Contains("производств"))
        {
            url = "/Incidents/Workplace";
            return true;
        }

        if (lower.Contains("расследован") && lower.Contains("несчастн"))
        {
            url = "/Incidents/Investigations";
            return true;
        }

        if (lower.Contains("учет") && lower.Contains("микротравм"))
        {
            url = "/Incidents/Microtraumas";
            return true;
        }

        if (lower.Contains("расследован") && lower.Contains("микротравм"))
        {
            url = "/Incidents/MicrotraumaInvestigations";
            return true;
        }

        if (lower.Contains("вид") && lower.Contains("происшеств"))
        {
            url = $"/Incidents/Catalog?kind={IncidentCatalogStore.KindKey(IncidentCatalogKind.IncidentTypes)}";
            return true;
        }

        if (lower.Contains("групп") && lower.Contains("занят"))
        {
            url = $"/Incidents/Catalog?kind={IncidentCatalogStore.KindKey(IncidentCatalogKind.OccupationGroups)}";
            return true;
        }

        if (lower.Contains("травм") && lower.Contains("классификатор"))
        {
            url = $"/Incidents/Catalog?kind={IncidentCatalogStore.KindKey(IncidentCatalogKind.InjuryTypes)}";
            return true;
        }

        if (lower.Contains("част") && lower.Contains("тел"))
        {
            url = $"/Incidents/Catalog?kind={IncidentCatalogStore.KindKey(IncidentCatalogKind.BodyParts)}";
            return true;
        }

        if (lower.Contains("последств"))
        {
            url = $"/Incidents/Catalog?kind={IncidentCatalogStore.KindKey(IncidentCatalogKind.AccidentConsequences)}";
            return true;
        }

        if (lower.Contains("причин"))
        {
            url = $"/Incidents/Catalog?kind={IncidentCatalogStore.KindKey(IncidentCatalogKind.IncidentCauses)}";
            return true;
        }

        if (lower.Contains("характер"))
        {
            url = $"/Incidents/Catalog?kind={IncidentCatalogStore.KindKey(IncidentCatalogKind.IncidentNature)}";
            return true;
        }

        if (lower.Contains("отчет") && lower.Contains("несчастн"))
        {
            url = "/Incidents/Hub";
            return true;
        }

        if (lower.Contains("помощник"))
        {
            url = "/Incidents/Assistants";
            return true;
        }

        return false;
    }

    public static bool IsIncidentsModule(string moduleSlug) =>
        moduleSlug.Equals(IncidentsSlug, StringComparison.OrdinalIgnoreCase) ||
        moduleSlug.Equals(ModuleSlug.ToSlug(ModuleMenuRegistry.Incidents.ModuleName), StringComparison.OrdinalIgnoreCase);
}

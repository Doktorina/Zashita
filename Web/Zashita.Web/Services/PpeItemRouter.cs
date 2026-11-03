using Zashita.Core.Navigation;
using Zashita.Core.Ppe;

namespace Zashita.Web.Services;

public static class PpeItemRouter
{
    private const string PpeSlug = "спецодежда-и-сиз";

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, out string? url) =>
        TryResolveRedirect(moduleSlug, itemTitle, itemSlug: null, out url);

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, string? itemSlug, out string? url)
    {
        url = null;
        if (!IsPpeModule(moduleSlug))
        {
            return false;
        }

        foreach (var token in new[] { itemTitle, itemSlug?.Replace('-', ' ') })
        {
            if (!string.IsNullOrWhiteSpace(token) && TryResolveByToken(token, out url))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveByToken(string t, out string? url)
    {
        url = null;
        var lower = t.ToLowerInvariant().Replace('ё', 'е');

        if (t.Equals(PpeCardTypes.ProvisionMenuTitle, StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("обеспечен") && (lower.Contains("спецодежд") || lower.Contains("сиз"))))
        {
            url = "/Ppe/Status";
            return true;
        }

        if (t.Equals(PpeCardTypes.CardHubMenuTitle, StringComparison.OrdinalIgnoreCase))
        {
            url = "/Ppe/CardSelect";
            return true;
        }

        if (lower.Contains("приемк") && lower.Contains("спецодежд"))
        {
            url = "/Ppe/Acceptance";
            return true;
        }

        if ((lower.Contains("изменени") || lower.Contains("изменен")) &&
            (lower.Contains("спецодежд") || lower.Contains("сиз")))
        {
            url = "/Ppe/Changes";
            return true;
        }

        if ((lower.Contains("списани") || lower.Contains("списан")) &&
            (lower.Contains("спецодежд") || lower.Contains("сиз")))
        {
            url = "/Ppe/Writeoff";
            return true;
        }

        if (lower.Contains("карточ") && (lower.Contains("сохран") || lower.Contains("история")))
        {
            url = "/Ppe/SavedCards";
            return true;
        }

        var doc = PpeCardTypes.ResolveMenuTitle(t);
        if (doc is not null)
        {
            url = $"/Ppe/SiosCard?doc={Uri.EscapeDataString(doc)}";
            return true;
        }

        if (lower.Contains("карточ"))
        {
            url = "/Ppe/CardSelect";
            return true;
        }

        if ((lower.Contains("стандарт") || lower.Contains("типов")) && !lower.Contains("закупк"))
        {
            if (t.Equals("Стандарты выдачи", StringComparison.OrdinalIgnoreCase))
            {
                url = "/Ppe/IssueStandards";
                return true;
            }
        }

        if (t.Equals("Номенклатура ЕТН", StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("номенклатур") && lower.Contains("етн") && !lower.Contains("професс")))
        {
            url = "/Ppe/EtnNomenclature";
            return true;
        }

        if (t.Equals("Производственные загрязнители ЕТН", StringComparison.OrdinalIgnoreCase) ||
            (lower.Contains("загрязнител") && lower.Contains("етн")))
        {
            url = "/Ppe/PollutantsEtn";
            return true;
        }

        if (t.Equals("Номенклатура", StringComparison.Ordinal))
        {
            url = "/Ppe/Nomenclature";
            return true;
        }

        var catalogKind = PpeReferenceCatalogKinds.ResolveMenuTitle(t);
        if (catalogKind is not null)
        {
            url = $"/Ppe/Catalog?kind={Uri.EscapeDataString(catalogKind)}";
            return true;
        }

        if (t.Equals("Внутренние заказы", StringComparison.OrdinalIgnoreCase))
        {
            url = "/Ppe/InternalOrders";
            return true;
        }

        if (lower.Contains("размер") && (lower.Contains("физ") || lower.Contains("лиц")))
        {
            url = "/Organizations";
            return true;
        }

        if (lower.Contains("установк") && lower.Contains("норм") && (lower.Contains("спецодежд") || lower.Contains("сиз")))
        {
            url = "/Ppe/NormsRegister";
            return true;
        }

        if (lower.Contains("закупк") && lower.Contains("заявк"))
        {
            url = "/Ppe/Procurement";
            return true;
        }

        if (lower.Contains("установк") && lower.Contains("потребност") &&
            (lower.Contains("спецодежд") || lower.Contains("сиз")))
        {
            url = "/Ppe/Procurement";
            return true;
        }

        if (t.Equals("Стандарты выдачи", StringComparison.OrdinalIgnoreCase))
        {
            url = "/Ppe/IssueStandards";
            return true;
        }

        return false;
    }

    public static bool IsPpeModule(string moduleSlug) =>
        moduleSlug.Equals(PpeSlug, StringComparison.OrdinalIgnoreCase);

}

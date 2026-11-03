using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public static class ElectricalItemRouter
{
    private const string ElectricalSlug = "электробезопасность";

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, string? itemSlug, out string? url)
    {
        url = null;
        if (!IsElectricalModule(moduleSlug))
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

        // Главная страница модуля — реестр с вкладками.
        url = "/Electrical/Register";
        return true;
    }

    private static bool TryResolveByToken(string title, out string? url)
    {
        url = null;
        var lower = title.ToLowerInvariant().Replace('ё', 'е');

        if (lower.Contains("реестр") ||
            (lower.Contains("проверк") && lower.Contains("знан")) ||
            (lower.Contains("категор") && lower.Contains("персонал")))
        {
            url = "/Electrical/Register";
            return true;
        }

        if (lower.Contains("комисси") && (lower.Contains("эб") || lower.Contains("электро") || lower.Length < 20))
        {
            url = "/Electrical/Commissions";
            return true;
        }

        if (lower.Contains("комисси"))
        {
            url = "/Electrical/Commissions";
            return true;
        }

        if (lower.Contains("акт") && lower.Contains("проверк") && !lower.Contains("сиз"))
        {
            url = "/Electrical/InspectionActs";
            return true;
        }

        if (lower.Contains("предписан"))
        {
            url = "/Electrical/InspectionActs";
            return true;
        }

        if (lower.Contains("план") && lower.Contains("проверк"))
        {
            url = "/Electrical/InspectionActs";
            return true;
        }

        return false;
    }

    public static bool IsElectricalModule(string moduleSlug)
    {
        if (string.IsNullOrWhiteSpace(moduleSlug))
        {
            return false;
        }

        var slug = moduleSlug.Trim();
        return slug.Equals(ElectricalSlug, StringComparison.OrdinalIgnoreCase) ||
               slug.Equals(ModuleSlug.ToSlug("Электробезопасность"), StringComparison.OrdinalIgnoreCase) ||
               slug.Contains("электробез", StringComparison.OrdinalIgnoreCase);
    }
}

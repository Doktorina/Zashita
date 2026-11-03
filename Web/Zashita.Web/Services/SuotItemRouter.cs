using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public static class SuotItemRouter
{
    private const string SuotSlug = "система-управления-охраной-труда";

    public static bool TryResolveRedirect(string moduleSlug, string? itemTitle, string? itemSlug, out string? url)
    {
        url = null;
        if (!IsSuotModule(moduleSlug))
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

        url = "/Suot/Indicators";
        return true;
    }

    private static string? SlugToText(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : slug.Replace('-', ' ');

    private static bool TryResolveByToken(string title, out string? url)
    {
        url = null;
        var lower = title.ToLowerInvariant().Replace('ё', 'е');

        if (title.Equals("Сотрудники", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("сотрудник"))
        {
            url = "/Organizations";
            return true;
        }

        if (lower.Contains("отражен") && lower.Contains("факт"))
        {
            url = "/Suot/Facts";
            return true;
        }

        if (lower.Contains("показател"))
        {
            url = "/Suot/Indicators";
            return true;
        }

        return false;
    }

    public static bool IsSuotModule(string moduleSlug) =>
        moduleSlug.Equals(SuotSlug, StringComparison.OrdinalIgnoreCase) ||
        moduleSlug.Equals(ModuleSlug.ToSlug("Система управления охраной труда"), StringComparison.OrdinalIgnoreCase);
}

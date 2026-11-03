using Microsoft.AspNetCore.WebUtilities;

namespace Zashita.Web.Services;

public static class WorkspaceReturnUrl
{
    private static readonly string[] OrganizationQueryKeys =
    [
        "selectedOrganization",
        "SelectedOrganization",
        "organization",
        "Organization",
        "company",
        "Company"
    ];

    /// <summary>
    /// Убирает из returnUrl параметры фильтра организации, чтобы после смены
    /// в тулбаре страница подхватила значение из сессии, а не старое из query.
    /// </summary>
    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/'))
        {
            return "/";
        }

        var qIndex = returnUrl.IndexOf('?', StringComparison.Ordinal);
        if (qIndex < 0)
        {
            return returnUrl;
        }

        var path = returnUrl[..qIndex];
        var query = QueryHelpers.ParseQuery(returnUrl[qIndex..]);
        var filtered = query
            .Where(pair => !OrganizationQueryKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToString());

        return filtered.Count == 0
            ? path
            : QueryHelpers.AddQueryString(path, filtered);
    }
}

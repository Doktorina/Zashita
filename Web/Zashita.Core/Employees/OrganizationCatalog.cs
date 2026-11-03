namespace Zashita.Core.Employees;

/// <summary>
/// Справочник организаций. В портфолио — только демо, без боевых ИП/ООО.
/// </summary>
public static class OrganizationCatalog
{
    public static IReadOnlyList<string> LegacyManualShellOrganizations { get; } = [];

    public static IReadOnlyList<string> BuiltInOrganizations { get; } =
    [
        "ООО «Демо Строй»",
        "ООО «Северные Технологии»",
        "ИП Пример Иван Иванович"
    ];

    public static IReadOnlyList<string> AdditionalExpectedOrganizations { get; } = [];

    public static bool IsLegacyManualShell(string organization)
    {
        var value = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return LegacyManualShellOrganizations.Any(legacy =>
            string.Equals(legacy, value, StringComparison.OrdinalIgnoreCase)
            || OrganizationMatcher.MatchesForCatalog(legacy, value));
    }

    public static IEnumerable<string> MergeWithCatalog(
        IEnumerable<string> fromOneC,
        IEnumerable<string>? kristinaOrganizations = null)
    {
        var merged = fromOneC.Concat(AdditionalExpectedOrganizations);

        if (kristinaOrganizations is not null)
        {
            merged = merged.Concat(kristinaOrganizations);
        }

        return OrganizationMatcher.DeduplicateOrganizations(
            OrganizationLegalEntitySplit.EnsurePreservedOrganizations(merged));
    }
}

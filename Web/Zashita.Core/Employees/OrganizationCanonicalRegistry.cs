namespace Zashita.Core.Employees;

/// <summary>
/// Канонический список юрлиц для портфолио (только демо).
/// </summary>
public static class OrganizationCanonicalRegistry
{
    public static IReadOnlyList<string> CanonicalNames { get; } =
    [
        "ООО «Демо Строй»",
        "ООО «Северные Технологии»",
        "ИП Пример Иван Иванович"
    ];

    public static string? TryGetLegalEntityId(string? organization)
    {
        var raw = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var core = OrganizationMatcher.GetCoreName(raw);
        var key = ToKey(core);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (ByKey.TryGetValue(key, out var id))
        {
            return id;
        }

        foreach (var pair in ByKey)
        {
            if (key.Contains(pair.Key, StringComparison.Ordinal)
                || pair.Key.Contains(key, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }

    public static string? ResolveCanonicalName(string? organization)
    {
        var id = TryGetLegalEntityId(organization);
        if (id is null)
        {
            return null;
        }

        return IdToName.TryGetValue(id, out var name) ? name : null;
    }

    public static bool AreDistinctLegalEntities(string? left, string? right)
    {
        var a = TryGetLegalEntityId(left);
        var b = TryGetLegalEntityId(right);
        if (a is null || b is null)
        {
            return false;
        }

        return !string.Equals(a, b, StringComparison.Ordinal);
    }

    private static readonly Dictionary<string, string> ByKey;
    private static readonly Dictionary<string, string> IdToName;

    static OrganizationCanonicalRegistry()
    {
        ByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        IdToName = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < CanonicalNames.Count; i++)
        {
            var name = CanonicalNames[i];
            var id = $"demo-le-{i + 1:D2}";
            IdToName[id] = name;
            var key = ToKey(OrganizationMatcher.GetCoreName(name));
            if (!string.IsNullOrWhiteSpace(key))
            {
                ByKey[key] = id;
            }
        }
    }

    private static string ToKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch))
            .ToArray();
        return new string(chars);
    }
}

namespace Zashita.Core.Med;

/// <summary>Разбор нескольких факторов/пунктов в одной профессии (Enter или «|»).</summary>
public static class MedContingentFactorLines
{
    public static IReadOnlyList<string> Split(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [string.Empty];
        }

        var parts = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split(['\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !IsJunkFactorLine(x))
            .ToList();
        return parts.Count == 0 ? [string.Empty] : parts;
    }

    public static int LineCount(string? factors, string? order988, string? order29) =>
        Math.Max(1, Math.Max(Split(factors).Count, Math.Max(Split(order988).Count, Split(order29).Count)));

    public static string At(IReadOnlyList<string> values, int index) =>
        index < values.Count ? values[index] : string.Empty;

    public static string Join(IEnumerable<string?> lines)
    {
        var parts = lines
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .ToList();
        return parts.Count == 0 ? string.Empty : string.Join('\n', parts);
    }

    /// <summary>Есть ли реальный пункт приказа (не пусто и не «-»).</summary>
    public static bool HasOrderCode(string? value) =>
        Split(value).Any(line =>
        {
            var t = line.Trim();
            return t.Length > 0 && t is not ("-" or "—" or "–");
        });

    private static bool IsJunkFactorLine(string value)
    {
        var t = value.Trim().TrimEnd('.').ToLowerInvariant().Replace('ё', 'е');
        return t is "периодический" or "предварительный" or "психиатрическое освидетельствование"
            or "психиатрический" or "внеочередной" or "предсменный" or "послесменный"
            or "медосмотр" or "осмотр";
    }
}

using System.Text.RegularExpressions;

namespace Zashita.Core.Med;

/// <summary>Номер пункта приказа 29н / справочника факторов (например 4.2.5).</summary>
public static partial class MedItemNumber
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        text = PrefixRegex().Replace(text, string.Empty).Trim();
        text = text.TrimStart('.', ' ', '\t');
        return text;
    }

    public static string Display(string? value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : $"п.{normalized}";
    }

    public static bool EqualsNormalized(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?:п(?:ункт)?\.?\s*)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();
}

namespace Zashita.Core.Ppe;

public sealed record IssueStandard(
    string Id,
    string Name,
    string Organization,
    string Profession,
    List<string> Items,
    DateTime CreatedAt,
    List<string>? Workers = null,
    string? Category = null)
{
    public string StandardCategory =>
        string.IsNullOrWhiteSpace(Category) ? "СИЗ" : Category.Trim();

    public string ListDisplayName => $"{Name} [{StandardCategory}]";

    public static bool IncludesSiz(string? category)
    {
        var c = NormalizeCategoryToken(category);
        return c is "" or "СИЗ" or "СИЗ+ДСИЗ";
    }

    public static bool IncludesDsiz(string? category)
    {
        var c = NormalizeCategoryToken(category);
        return c is "ДСИЗ" or "СИЗ+ДСИЗ";
    }

    public static string EncodeCategory(bool siz, bool dsiz)
    {
        if (siz && dsiz) return "СИЗ+ДСИЗ";
        if (dsiz) return "ДСИЗ";
        return "СИЗ";
    }

    private static string NormalizeCategoryToken(string? category)
    {
        var s = (category ?? string.Empty).Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(s)) return "СИЗ";
        if (s.Equals("СИЗ+ДСИЗ", StringComparison.OrdinalIgnoreCase)) return "СИЗ+ДСИЗ";
        if (s.Equals("ДСИЗ", StringComparison.OrdinalIgnoreCase)) return "ДСИЗ";
        if (s.Equals("СИЗ", StringComparison.OrdinalIgnoreCase)) return "СИЗ";
        return s;
    }
}

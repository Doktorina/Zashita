namespace Zashita.Core.Employees;

/// <summary>Сопоставление ФИО из 1С, медкарт и списков контингента.</summary>
public static class EmployeeNameMatcher
{
    public static string Normalize(string? name) =>
        string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool Matches(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var (aLast, aFirst, aPat) = EmployeeFio.Split(a);
        var (bLast, bFirst, bPat) = EmployeeFio.Split(b);
        if (string.IsNullOrWhiteSpace(aLast) || string.IsNullOrWhiteSpace(bLast))
        {
            return false;
        }

        if (!string.Equals(aLast, bLast, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsLastNameOnly(a) || IsLastNameOnly(b))
        {
            return true;
        }

        if (!FirstTokenMatches(aFirst, bFirst))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(aPat) || string.IsNullOrWhiteSpace(bPat))
        {
            return true;
        }

        return FirstTokenMatches(aPat, bPat);
    }

    public static bool IsPlausiblePersonName(string? name)
    {
        var normalized = Normalize(name);
        if (normalized.Length < 3)
        {
            return false;
        }

        if (normalized.Contains("не указан", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 || normalized.Length >= 5;
    }

    private static bool IsLastNameOnly(string normalized) =>
        !normalized.Contains(' ', StringComparison.Ordinal);

    private static bool FirstTokenMatches(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftShort = left.Length == 1 || left.EndsWith('.');
        var rightShort = right.Length == 1 || right.EndsWith('.');
        if (leftShort || rightShort)
        {
            return char.ToUpperInvariant(left[0]) == char.ToUpperInvariant(right[0]);
        }

        return false;
    }
}

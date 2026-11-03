namespace Zashita.Core.Employees;

public static class EmployeeContextValidator
{
    public static bool IsPlausibleDepartment(string? value)
    {
        var text = Normalize(value);
        return text.Length >= 2 && !IsGarbageToken(text);
    }

    public static bool IsPlausibleProfession(string? value)
    {
        var text = ProfessionMatcher.Normalize(value);
        return text.Length >= 2 && !IsGarbageToken(text);
    }

    public static string NormalizeDepartment(string? value)
    {
        var text = Normalize(value);
        return IsPlausibleDepartment(text) ? text : string.Empty;
    }

    public static string NormalizeProfession(string? value)
    {
        var text = ProfessionMatcher.Normalize(value);
        return IsPlausibleProfession(text) ? text : string.Empty;
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim();

    private static bool IsGarbageToken(string text)
    {
        if (text.Length == 0)
        {
            return true;
        }

        if (text.Length == 1)
        {
            return true;
        }

        return text.Equals("—", StringComparison.Ordinal)
               || text.Equals("-", StringComparison.Ordinal);
    }
}

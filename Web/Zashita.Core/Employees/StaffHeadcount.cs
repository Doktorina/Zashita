namespace Zashita.Core.Employees;

/// <summary>
/// Единый счётчик «людей» для отчётов: карточка штата человек×организация.
/// Одно ФИО в двух ЮЛ = 2. Не считать строки осмотров/обучений/дерева рисков.
/// </summary>
public static class StaffHeadcount
{
    public static int CountPersonOrgCards(
        IEnumerable<(string Organization, OrganizationEmployee Employee)> pairs)
    {
        return pairs
            .Where(p => !string.IsNullOrWhiteSpace(p.Employee?.Name))
            .Select(PersonOrgKey)
            .Where(k => k.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public static int CountPersonOrgCards(
        IEnumerable<(string Organization, string Name, string Profession, string Department)> pairs)
    {
        return pairs
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => PersonOrgKey(p.Organization, p.Name))
            .Where(k => k.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public static string PersonOrgKey(string? organization, string? name)
    {
        var org = (organization ?? string.Empty).Trim();
        var person = (name ?? string.Empty).Trim();
        if (org.Length == 0 || person.Length == 0)
        {
            return string.Empty;
        }

        return org + "|" + person;
    }

    private static string PersonOrgKey((string Organization, OrganizationEmployee Employee) pair) =>
        PersonOrgKey(pair.Organization, pair.Employee.Name);
}

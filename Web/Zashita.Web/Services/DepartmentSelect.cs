namespace Zashita.Web.Services;

/// <summary>Списки подразделений для select (можно оставить пустым).</summary>
public static class DepartmentSelect
{
    public static IReadOnlyList<string> Merge(params IEnumerable<string>?[] sources)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var raw in source)
            {
                var value = (raw ?? string.Empty).Trim();
                if (value.Length > 0)
                {
                    set.Add(value);
                }
            }
        }

        return set.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> FromEmployees(OrganizationEmployeesStore employees, string organization) =>
        Merge(employees.Get(organization).Select(e => e.Department));
}

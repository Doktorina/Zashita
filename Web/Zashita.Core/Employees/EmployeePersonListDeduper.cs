namespace Zashita.Core.Employees;

/// <summary>Убирает повторы одного человека в списках (по ФИО, с учётом опечаток).</summary>
public static class EmployeePersonListDeduper
{
    public static List<T> DedupeByPersonName<T>(
        IEnumerable<T> items,
        Func<T, string> nameSelector,
        Func<T, int>? scoreSelector = null)
    {
        scoreSelector ??= _ => 0;
        var result = new List<T>();
        foreach (var item in items.OrderByDescending(scoreSelector))
        {
            var name = nameSelector(item);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (result.Any(existing => EmployeeNameMatcher.Matches(nameSelector(existing), name)))
            {
                continue;
            }

            result.Add(item);
        }

        return result;
    }

    public static int PersonNameScore(string? name, string? profession = null, string? department = null) =>
        (EmployeeNameMatcher.IsPlausiblePersonName(name) ? 10 : 0)
        + (string.IsNullOrWhiteSpace(profession) ? 0 : 5)
        + (string.IsNullOrWhiteSpace(department) ? 0 : 3);
}

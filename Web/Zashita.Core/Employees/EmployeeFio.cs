namespace Zashita.Core.Employees;

public static class EmployeeFio
{
    public static (string LastName, string FirstName, string Patronymic) Split(string? fullName)
    {
        var parts = (fullName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (
            parts.ElementAtOrDefault(0) ?? string.Empty,
            parts.ElementAtOrDefault(1) ?? string.Empty,
            parts.ElementAtOrDefault(2) ?? string.Empty);
    }

    public static void ApplyNameParts(OrganizationEmployee employee)
    {
        var (last, first, patronymic) = Split(employee.Name);
        employee.LastName = last;
        employee.FirstName = first;
        employee.Patronymic = patronymic;
    }
}

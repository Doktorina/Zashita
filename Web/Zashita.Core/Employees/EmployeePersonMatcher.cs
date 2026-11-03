namespace Zashita.Core.Employees;

/// <summary>Сравнение людей и отсечение пустых «оболочек» из старых импортов СИЗ/Excel.</summary>
public static class EmployeePersonMatcher
{
    public static bool SamePerson(OrganizationEmployee left, OrganizationEmployee right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var leftSnils = OneCEmployeeListParser.NormalizeSnils(left.Snils);
        var rightSnils = OneCEmployeeListParser.NormalizeSnils(right.Snils);
        if (!string.IsNullOrWhiteSpace(leftSnils)
            && !string.IsNullOrWhiteSpace(rightSnils))
        {
            return string.Equals(leftSnils, rightSnils, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(leftSnils) || !string.IsNullOrWhiteSpace(rightSnils))
        {
            return EmployeeNameMatcher.Matches(left.Name, right.Name);
        }

        return EmployeeNameMatcher.Matches(left.Name, right.Name);
    }

    public static string PersonKey(OrganizationEmployee employee)
    {
        var snils = OneCEmployeeListParser.NormalizeSnils(employee.Snils);
        return !string.IsNullOrWhiteSpace(snils)
            ? "snils:" + snils
            : "name:" + (employee.Name ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Пустая карточка: только ФИО/должность из СИЗ без СНИЛС, дат и адреса.
    /// </summary>
    public static bool IsShellRecord(OrganizationEmployee employee) =>
        string.IsNullOrWhiteSpace(OneCEmployeeListParser.NormalizeSnils(employee.Snils))
        && employee.BirthDate is null
        && employee.EmploymentDate is null
        && employee.TransferDate is null
        && employee.DismissalDate is null
        && string.IsNullOrWhiteSpace(employee.EmploymentType)
        && string.IsNullOrWhiteSpace(employee.HomeAddress)
        && string.IsNullOrWhiteSpace(employee.Department);

    public static int CompletenessScore(OrganizationEmployee employee) =>
        (string.IsNullOrWhiteSpace(OneCEmployeeListParser.NormalizeSnils(employee.Snils)) ? 0 : 40)
        + (employee.BirthDate is not null ? 10 : 0)
        + (employee.EmploymentDate is not null ? 10 : 0)
        + (employee.TransferDate is not null ? 5 : 0)
        + (!string.IsNullOrWhiteSpace(employee.EmploymentType) ? 5 : 0)
        + (!string.IsNullOrWhiteSpace(employee.Department) ? 5 : 0)
        + (!string.IsNullOrWhiteSpace(employee.HomeAddress) ? 10 : 0)
        + (!string.IsNullOrWhiteSpace(employee.HrStatus) ? 5 : 0)
        + (employee.IsActive ? 10 : 0)
        + (employee.SyncedAt is not null ? 5 : 0);
}

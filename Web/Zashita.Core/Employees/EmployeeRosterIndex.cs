namespace Zashita.Core.Employees;

/// <summary>Индекс штата для сопоставления ФИО и канонических подразделений в отчётах.</summary>
public sealed class EmployeeRosterIndex
{
    private readonly Dictionary<string, List<OrganizationEmployee>> _byOrg =
        new(StringComparer.OrdinalIgnoreCase);

    public static EmployeeRosterIndex Build(
        IEnumerable<(string Organization, OrganizationEmployee Employee)> employees)
    {
        var index = new EmployeeRosterIndex();
        foreach (var (organization, employee) in employees)
        {
            if (string.IsNullOrWhiteSpace(organization) ||
                string.IsNullOrWhiteSpace(employee.Name) ||
                !EmployeeNameMatcher.IsPlausiblePersonName(employee.Name))
            {
                continue;
            }

            var org = organization.Trim();
            if (!index._byOrg.TryGetValue(org, out var list))
            {
                list = [];
                index._byOrg[org] = list;
            }

            list.Add(employee);
        }

        return index;
    }

    public bool TryResolve(string? organization, string? employeeName, out OrganizationEmployee employee)
    {
        employee = new OrganizationEmployee();
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(employeeName))
        {
            return false;
        }

        if (!_byOrg.TryGetValue(organization.Trim(), out var list) || list.Count == 0)
        {
            return false;
        }

        var matches = list
            .Where(e => EmployeeNameMatcher.Matches(e.Name, employeeName))
            .ToList();
        if (matches.Count == 0)
        {
            return false;
        }

        employee = matches
            .OrderByDescending(e => e.IsActive ? 1 : 0)
            .ThenByDescending(e => OneCEmployeeListParser.IsPrimaryEmployment(e.EmploymentType) ? 1 : 0)
            .ThenByDescending(e => !string.IsNullOrWhiteSpace(e.Department) ? 1 : 0)
            .ThenByDescending(e => Normalize(e.Name).Length)
            .ThenByDescending(e => e.SyncedAt ?? DateTime.MinValue)
            .First();
        return true;
    }

    public string BuildPersonOrgKey(string? organization, string? employeeName)
    {
        var org = (organization ?? string.Empty).Trim();
        var name = (employeeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(name))
        {
            return $"{org}|{name}";
        }

        if (TryResolve(org, name, out var employee))
        {
            var personKey = OneCEmployeeListParser.BuildPersonKey(
                org,
                employee.Snils,
                employee.ExternalId,
                employee.Name);
            return $"{org}|{personKey}";
        }

        return $"{org}|name:{Normalize(name).ToLowerInvariant()}";
    }

    public IReadOnlyList<EmployeeRosterDuplicate> FindAmbiguousMatches()
    {
        var issues = new List<EmployeeRosterDuplicate>();
        foreach (var (org, list) in _byOrg)
        {
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    var left = list[i];
                    var right = list[j];
                    if (!EmployeeNameMatcher.Matches(left.Name, right.Name))
                    {
                        continue;
                    }

                    var leftKey = OneCEmployeeListParser.BuildPersonKey(
                        org, left.Snils, left.ExternalId, left.Name);
                    var rightKey = OneCEmployeeListParser.BuildPersonKey(
                        org, right.Snils, right.ExternalId, right.Name);
                    if (string.Equals(leftKey, rightKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    issues.Add(new EmployeeRosterDuplicate(
                        org,
                        left.Name,
                        right.Name,
                        left.Department,
                        right.Department,
                        left.Profession,
                        right.Profession));
                }
            }
        }

        return issues;
    }

    private static string Normalize(string? name) => EmployeeNameMatcher.Normalize(name);
}

public sealed record EmployeeRosterDuplicate(
    string Organization,
    string NameA,
    string NameB,
    string DepartmentA,
    string DepartmentB,
    string ProfessionA,
    string ProfessionB);

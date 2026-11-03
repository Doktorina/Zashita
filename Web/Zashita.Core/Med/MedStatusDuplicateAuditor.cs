using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public sealed record MedStatusDuplicateIssue(
    string Organization,
    string EmployeeName,
    IReadOnlyList<string> Departments,
    IReadOnlyList<string> Professions,
    IReadOnlyList<string> NameVariants);

/// <summary>Проверка отчёта медосмотров на дубли одного человека в дереве.</summary>
public static class MedStatusDuplicateAuditor
{
    public static IReadOnlyList<MedStatusDuplicateIssue> FindTreeDuplicates(
        IReadOnlyList<MedStatusRow> rows,
        EmployeeRosterIndex? roster = null)
    {
        var grouped = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeName))
            .GroupBy(
                r => roster?.BuildPersonOrgKey(r.Organization, r.EmployeeName)
                     ?? PersonOrgKey(r),
                StringComparer.OrdinalIgnoreCase);

        var issues = new List<MedStatusDuplicateIssue>();
        foreach (var group in grouped)
        {
            var departments = group
                .Select(r => string.IsNullOrWhiteSpace(r.Department) ? "Без подразделения" : r.Department.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var professions = group
                .Select(r =>
                {
                    var prof = ProfessionMatcher.Normalize(r.Profession);
                    return string.IsNullOrWhiteSpace(prof) ? "Без профессии" : prof;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var names = group
                .Select(r => r.EmployeeName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (departments.Count <= 1 && professions.Count <= 1 && names.Count <= 1)
            {
                continue;
            }

            var sample = group.First();
            issues.Add(new MedStatusDuplicateIssue(
                sample.Organization,
                names.OrderByDescending(n => n.Length).First(),
                departments,
                professions,
                names));
        }

        return issues
            .OrderBy(i => i.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string PersonOrgKey(MedStatusRow row) =>
        $"{(row.Organization ?? string.Empty).Trim()}|{(row.EmployeeName ?? string.Empty).Trim()}";
}

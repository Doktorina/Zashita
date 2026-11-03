using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Схлопывает дублирующиеся папки подразделений в перечне рабочих мест СОУТ.</summary>
public static class SoutWorkplaceDepartmentConsolidator
{
    public static string NormalizeDepartmentName(string? name, string? department)
    {
        if (!string.IsNullOrWhiteSpace(department))
        {
            return department.Trim();
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return "Без подразделения";
    }

    public static int CollapseAllOrganizations(List<WorkplaceRecord> records)
    {
        if (records.Count == 0)
        {
            return 0;
        }

        var changed = 0;
        foreach (var organization in records
                     .Select(record => (record.Organization ?? string.Empty).Trim())
                     .Where(org => org.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            changed += CollapseOrganization(records, organization);
        }

        return changed;
    }

    public static int CollapseOrganization(List<WorkplaceRecord> records, string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return 0;
        }

        var roots = records
            .Where(record => record.IsGroup
                             && string.IsNullOrWhiteSpace(record.ParentId)
                             && SameOrganization(record.Organization, org))
            .ToList();

        var duplicateSets = roots
            .GroupBy(
                record => NormalizeDepartmentName(record.Name, record.Department),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicateSets.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        foreach (var set in duplicateSets)
        {
            var ordered = set
                .OrderByDescending(record => DirectChildCount(records, record.Id))
                .ThenBy(record => record.CreatedAt == default ? DateTime.MaxValue : record.CreatedAt)
                .ThenBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var canonical = ordered[0];
            var normalized = NormalizeDepartmentName(canonical.Name, canonical.Department);
            canonical.Name = normalized;
            canonical.Department = normalized;

            foreach (var duplicate in ordered.Skip(1))
            {
                foreach (var child in records.Where(record =>
                             string.Equals(record.ParentId, duplicate.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    child.ParentId = canonical.Id;
                    child.ModifiedAt = DateTime.UtcNow;
                }

                records.RemoveAll(record => string.Equals(record.Id, duplicate.Id, StringComparison.OrdinalIgnoreCase));
                removed++;
            }
        }

        return removed;
    }

    public static List<WorkplaceRecord> DistinctRootDepartmentGroups(IEnumerable<WorkplaceRecord> records) =>
        records
            .Where(record => record.IsGroup && string.IsNullOrWhiteSpace(record.ParentId))
            .GroupBy(
                record => NormalizeDepartmentName(record.Name, record.Department),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(record => records.Count(child =>
                    string.Equals(child.ParentId, record.Id, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(record => record.CreatedAt == default ? DateTime.MaxValue : record.CreatedAt)
                .First())
            .OrderBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static int DirectChildCount(IReadOnlyList<WorkplaceRecord> records, string parentId) =>
        records.Count(record => string.Equals(record.ParentId, parentId, StringComparison.OrdinalIgnoreCase));

    private static bool SameOrganization(string? left, string right) =>
        string.Equals((left ?? string.Empty).Trim(), right, StringComparison.OrdinalIgnoreCase)
        || OrganizationMatcher.Matches(left ?? string.Empty, right)
        || OrganizationMatcher.Matches(right, left ?? string.Empty);
}

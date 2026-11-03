using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public static class MedOrganizationFilter
{
    public static bool Matches(string? selectedOrganization, string? recordOrganization)
    {
        if (string.IsNullOrWhiteSpace(selectedOrganization))
        {
            return true;
        }

        var selected = selectedOrganization.Trim();
        if (string.IsNullOrWhiteSpace(recordOrganization))
        {
            return false;
        }

        var record = recordOrganization.Trim();
        return string.Equals(selected, record, StringComparison.OrdinalIgnoreCase)
               || OrganizationMatcher.Matches(selected, record)
               || OrganizationMatcher.Matches(record, selected);
    }

    public static int AlignOrganizations<T>(
        IList<T> records,
        Func<T, string> getOrg,
        Action<T, string> setOrg,
        IEnumerable<string> canonicalOrganizations)
    {
        var canonical = canonicalOrganizations
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        if (canonical.Count == 0 || records.Count == 0)
        {
            return 0;
        }

        var changed = 0;
        foreach (var record in records)
        {
            var org = (getOrg(record) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(org))
            {
                continue;
            }

            var resolved = OrganizationMatcher.ResolveOrganizationName(org, canonical);
            if (resolved is not null
                && !string.Equals(resolved, org, StringComparison.OrdinalIgnoreCase))
            {
                setOrg(record, resolved);
                changed++;
            }
        }

        return changed;
    }
}

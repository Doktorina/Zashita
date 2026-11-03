namespace Zashita.Core.Risks;

/// <summary>Выбор комиссии СОУТ для печати приказа.</summary>
public static class SoutCommissionResolver
{
    public static IReadOnlyList<SoutCommissionRecord> ListSelectable(
        string organization,
        IEnumerable<SoutCommissionRecord> records)
    {
        var org = (organization ?? string.Empty).Trim();
        return (records ?? [])
            .Where(r => !r.IsGroup && !r.IsInactive)
            .Where(r => string.IsNullOrWhiteSpace(org)
                        || string.Equals(r.Organization, org, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(IsSoutName)
            .ThenByDescending(r => r.OrderDate ?? DateTime.MinValue)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static SoutCommissionRecord? Resolve(
        string organization,
        IEnumerable<SoutCommissionRecord> records,
        string? commissionId = null)
    {
        var list = ListSelectable(organization, records);
        // ListSelectable отбрасывает неактивные — для явного id ищем среди всех не-групп.
        if (!string.IsNullOrWhiteSpace(commissionId))
        {
            var org = (organization ?? string.Empty).Trim();
            var byId = (records ?? [])
                .Where(r => !r.IsGroup)
                .Where(r => string.IsNullOrWhiteSpace(org)
                            || string.Equals(r.Organization, org, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(r =>
                    string.Equals(r.Id, commissionId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        return list.FirstOrDefault();
    }

    private static bool IsSoutName(SoutCommissionRecord item) =>
        item.Name.Contains("специальной оценки", StringComparison.OrdinalIgnoreCase)
        || item.Name.Contains("СОУТ", StringComparison.OrdinalIgnoreCase);
}

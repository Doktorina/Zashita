namespace Zashita.Core.Risks;

public enum SoutCommissionScopeKind
{
    Risks,
    Sout
}

public static class SoutCommissionScope
{
    public static HashSet<string> CollectRiskSubtreeIds(IReadOnlyList<SoutCommissionRecord> records)
    {
        var folderId = RiskCommissionResolver.FindRiskFolderId(records);
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return CollectSubtreeIds(records, folderId);
    }

    public static HashSet<string> CollectSubtreeIds(IReadOnlyList<SoutCommissionRecord> records, string rootId)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.ParentId))
                {
                    continue;
                }

                if (ids.Contains(record.ParentId) && ids.Add(record.Id))
                {
                    changed = true;
                }
            }
        }

        return ids;
    }

    public static List<SoutCommissionRecord> Filter(
        IReadOnlyList<SoutCommissionRecord> records,
        SoutCommissionScopeKind scope)
    {
        var riskIds = CollectRiskSubtreeIds(records);
        return scope switch
        {
            SoutCommissionScopeKind.Risks => records
                .Where(record => riskIds.Contains(record.Id))
                .ToList(),
            _ => records
                .Where(record => !riskIds.Contains(record.Id))
                .ToList()
        };
    }

    public static string? DefaultParentId(
        IReadOnlyList<SoutCommissionRecord> records,
        SoutCommissionScopeKind scope)
    {
        return scope switch
        {
            SoutCommissionScopeKind.Risks => RiskCommissionResolver.FindRiskFolderId(records),
            _ => records.FirstOrDefault(record =>
                    record.IsGroup
                    && record.Name.Contains("Постоянно действующие", StringComparison.OrdinalIgnoreCase))
                ?.Id
                ?? records.FirstOrDefault(record =>
                    record.IsGroup
                    && record.Name.Contains("специальной оценки", StringComparison.OrdinalIgnoreCase))
                ?.ParentId
        };
    }

    public static bool BelongsToScope(SoutCommissionRecord record, SoutCommissionScopeKind scope, IReadOnlyList<SoutCommissionRecord> all)
    {
        var riskIds = CollectRiskSubtreeIds(all);
        return scope switch
        {
            SoutCommissionScopeKind.Risks => riskIds.Contains(record.Id),
            _ => !riskIds.Contains(record.Id)
        };
    }

    public static SoutCommissionRecord CreateRiskFolder(string organization) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Organization = organization.Trim(),
        IsGroup = true,
        Name = RiskCommissionResolver.RiskFolderName
    };
}

public static class SoutCommissionCompliance
{
    public const string Compliant = "Соответствует";
    public const string NonCompliant = "Не соответствует";

    public static readonly IReadOnlyList<string> All = [Compliant, NonCompliant];

    public static string Suggest(SoutCommissionRecord record)
    {
        var total = (record.Members ?? []).Count(member => !string.IsNullOrWhiteSpace(member.Name));
        if (total <= 0)
        {
            return NonCompliant;
        }

        var certified = record.CertifiedInRostechnadzorCount ?? 0;
        return certified >= total ? Compliant : NonCompliant;
    }
}

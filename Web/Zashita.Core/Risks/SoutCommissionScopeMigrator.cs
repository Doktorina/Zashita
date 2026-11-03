namespace Zashita.Core.Risks;

/// <summary>Разделяет комиссии рисков и СОУТ по папкам в общем справочнике.</summary>
public static class SoutCommissionScopeMigrator
{
    public const string StandingFolderName = "Постоянно действующие комиссии";

    public static int MigrateOrganization(string organization, List<SoutCommissionRecord> records)
    {
        var org = (organization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org) || records.Count == 0)
        {
            return 0;
        }

        var changed = 0;
        var riskFolderId = EnsureRiskFolder(records, org);
        var standingFolderId = EnsureStandingFolder(records, org);

        foreach (var record in records.Where(r => SameOrg(r, org) && !r.IsGroup))
        {
            var inRiskTree = IsInSubtree(records, record.Id, riskFolderId);
            if (IsSoutCommission(record))
            {
                if (inRiskTree && standingFolderId is not null)
                {
                    record.ParentId = standingFolderId;
                    record.ModifiedAt = DateTime.UtcNow;
                    changed++;
                }

                continue;
            }

            if (RiskCommissionResolver.IsRiskCommission(record) && !inRiskTree)
            {
                record.ParentId = riskFolderId;
                record.ModifiedAt = DateTime.UtcNow;
                changed++;
            }
        }

        return changed;
    }

    public static void EnsureRecordInScope(
        SoutCommissionRecord record,
        IReadOnlyList<SoutCommissionRecord> all,
        SoutCommissionScopeKind scope,
        string organization)
    {
        if (record.IsGroup)
        {
            return;
        }

        var list = all as List<SoutCommissionRecord> ?? all.ToList();
        if (scope == SoutCommissionScopeKind.Risks)
        {
            var riskFolderId = EnsureRiskFolder(list, organization);
            // Новый Id ещё не в списке — IsInSubtree даст false; всё равно вешаем на папку рисков.
            if (string.IsNullOrWhiteSpace(record.ParentId)
                || !IsInSubtree(list.Append(record).ToList(), record.Id, riskFolderId))
            {
                record.ParentId = riskFolderId;
            }

            return;
        }

        var riskIds = SoutCommissionScope.CollectRiskSubtreeIds(list);
        if (!string.IsNullOrWhiteSpace(record.ParentId) && riskIds.Contains(record.ParentId))
        {
            record.ParentId = EnsureStandingFolder(list, organization);
        }
        else if (string.IsNullOrWhiteSpace(record.ParentId))
        {
            record.ParentId = EnsureStandingFolder(list, organization);
        }
    }

    private static string EnsureRiskFolder(List<SoutCommissionRecord> records, string organization)
    {
        var existingId = RiskCommissionResolver.FindRiskFolderId(records);
        if (!string.IsNullOrWhiteSpace(existingId))
        {
            return existingId;
        }

        var folder = SoutCommissionScope.CreateRiskFolder(organization);
        folder.CreatedAt = DateTime.UtcNow;
        folder.ModifiedAt = DateTime.UtcNow;
        records.Add(folder);
        return folder.Id;
    }

    private static string? EnsureStandingFolder(List<SoutCommissionRecord> records, string organization)
    {
        var existing = records.FirstOrDefault(record =>
            record.IsGroup
            && SameOrg(record, organization)
            && record.Name.Contains(StandingFolderName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing.Id;
        }

        var folder = new SoutCommissionRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Organization = organization,
            IsGroup = true,
            Name = StandingFolderName,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
        records.Add(folder);
        return folder.Id;
    }

    private static bool IsSoutCommission(SoutCommissionRecord record)
    {
        var name = (record.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return false;
        }

        return name.Contains("специальн", StringComparison.OrdinalIgnoreCase)
               && name.Contains("оценк", StringComparison.OrdinalIgnoreCase)
               || name.Contains("соут", StringComparison.OrdinalIgnoreCase)
               || name.Contains("условий труда", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInSubtree(IReadOnlyList<SoutCommissionRecord> records, string recordId, string rootId)
    {
        if (string.Equals(recordId, rootId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var record = records.FirstOrDefault(r => string.Equals(r.Id, recordId, StringComparison.OrdinalIgnoreCase));
        if (record is null || string.IsNullOrWhiteSpace(record.ParentId))
        {
            return false;
        }

        return IsInSubtree(records, record.ParentId, rootId);
    }

    private static bool SameOrg(SoutCommissionRecord record, string organization) =>
        string.Equals((record.Organization ?? string.Empty).Trim(), organization, StringComparison.OrdinalIgnoreCase);
}

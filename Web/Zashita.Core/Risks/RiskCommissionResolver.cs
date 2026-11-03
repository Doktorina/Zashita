namespace Zashita.Core.Risks;

/// <summary>Комиссии для документов оценки профессиональных рисков.</summary>
public static class RiskCommissionResolver
{
    public const string RiskFolderName = "Комиссии по рискам";

    public static bool IsRiskCommission(SoutCommissionRecord record)
    {
        if (record.IsGroup)
        {
            return false;
        }

        var name = (record.Name ?? string.Empty).Trim();
        return name.Contains("оценк", StringComparison.OrdinalIgnoreCase)
               && name.Contains("риск", StringComparison.OrdinalIgnoreCase)
               || name.Contains("ОПР", StringComparison.OrdinalIgnoreCase)
               || name.Contains("профессиональн", StringComparison.OrdinalIgnoreCase)
               && name.Contains("риск", StringComparison.OrdinalIgnoreCase);
    }

    public static string? FindRiskFolderId(IEnumerable<SoutCommissionRecord> records) =>
        records.FirstOrDefault(r =>
            r.IsGroup
            && string.Equals((r.Name ?? string.Empty).Trim(), RiskFolderName, StringComparison.OrdinalIgnoreCase))?.Id;

    public static IReadOnlyList<SoutCommissionRecord> ListSelectable(
        string organization,
        IEnumerable<SoutCommissionRecord> records)
    {
        var org = (organization ?? string.Empty).Trim();
        var list = (records ?? [])
            .Where(r => !r.IsGroup)
            .Where(r => SameOrg(r.Organization, org))
            .ToList();

        var riskFolderId = FindRiskFolderId(records ?? []);
        var riskScoped = list
            .Where(r =>
                IsRiskCommission(r)
                || (riskFolderId is not null
                    && string.Equals(r.ParentId, riskFolderId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.OrderDate ?? DateTime.MinValue)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (riskScoped.Count > 0)
        {
            return riskScoped;
        }

        return list
            .Where(r => !r.IsInactive)
            .OrderByDescending(r => r.OrderDate ?? DateTime.MinValue)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static SoutCommissionRecord? Resolve(
        string organization,
        IEnumerable<SoutCommissionRecord> records,
        string? commissionId)
    {
        var org = (organization ?? string.Empty).Trim();
        var list = (records ?? [])
            .Where(r => !r.IsGroup)
            .Where(r => SameOrg(r.Organization, org))
            .ToList();

        if (!string.IsNullOrWhiteSpace(commissionId))
        {
            var byId = list.FirstOrDefault(r =>
                string.Equals(r.Id, commissionId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        return ListSelectable(org, records ?? []).FirstOrDefault();
    }

    public static string FormatChairmanForOrder(SoutCommissionMember member)
    {
        var name = (member.Name ?? string.Empty).Trim();
        if (name.Length == 0 || RiskPrintChrome.LooksLikeOrganizationName(name))
        {
            return string.Empty;
        }

        var position = (member.Position ?? string.Empty).Trim();
        if (position.Length == 0 || RiskPrintChrome.LooksLikeOrganizationName(position))
        {
            return $"Директор {name}";
        }

        if (position.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return RiskPrintChrome.LooksLikeOrganizationName(position) ? $"Директор {name}" : position;
        }

        return $"{position} {name}";
    }

    public static IReadOnlyList<string> FormatMembersForOrder(IEnumerable<SoutCommissionMember> members)
    {
        return (members ?? [])
            .Where(m => !SoutCommissionMemberHelpers.IsChairmanStatus(m.Status))
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select(m =>
            {
                var name = m.Name.Trim();
                var pos = (m.Position ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(pos) ? name : $"{name}, {pos}";
            })
            .ToList();
    }

    public static void EnsureMembers(SoutCommissionRecord commission)
    {
        commission.Members ??= [];
        if (commission.Members.Count == 0 && !string.IsNullOrWhiteSpace(commission.Composition))
        {
            commission.Members = SoutCommissionMemberHelpers.MigrateFromCompositionText(commission.Composition);
        }
    }

    private static bool SameOrg(string? left, string? right) =>
        string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}

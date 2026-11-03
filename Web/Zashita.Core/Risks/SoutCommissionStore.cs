using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

public sealed class SoutCommissionStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SoutCommissionStore(RiskDataPaths paths) => _paths = paths;

    public List<SoutCommissionRecord> Load(string organization) =>
        LoadInternal(organization, SoutCommissionDefaults.Create);

    public void Save(string organization, IEnumerable<SoutCommissionRecord> records) =>
        SaveInternal(_paths.CommissionsFile, organization, records);

    public SoutCommissionRecord? Find(string organization, string id) =>
        Load(organization).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public void Upsert(string organization, SoutCommissionRecord record)
    {
        var list = Load(organization);
        var index = list.FindIndex(item => string.Equals(item.Id, record.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            list[index] = record;
        }
        else
        {
            list.Add(record);
        }

        Save(organization, list);
    }

    /// <summary>Гарантирует папку «Комиссии по рискам» в справочнике (создаёт при отсутствии).</summary>
    public string EnsureRiskFolder(string organization)
    {
        var org = organization.Trim();
        var all = Load(org);
        var existingId = RiskCommissionResolver.FindRiskFolderId(all);
        if (!string.IsNullOrWhiteSpace(existingId))
        {
            return existingId!;
        }

        var folder = SoutCommissionScope.CreateRiskFolder(org);
        folder.CreatedAt = DateTime.UtcNow;
        folder.ModifiedAt = DateTime.UtcNow;
        Upsert(org, folder);
        return folder.Id;
    }

    public int MigrateScopeSeparation(string organization)
    {
        var all = Load(organization);
        var countBefore = all.Count;
        var changed = SoutCommissionScopeMigrator.MigrateOrganization(organization, all);
        if (changed > 0 || all.Count != countBefore)
        {
            Save(organization, all);
        }

        return changed;
    }

    public void SaveScope(
        string organization,
        IEnumerable<SoutCommissionRecord> scopeRecords,
        SoutCommissionScopeKind scope)
    {
        var org = organization.Trim();
        var all = Load(org);
        var riskIds = SoutCommissionScope.CollectRiskSubtreeIds(all);
        var scopeList = scopeRecords.ToList();

        List<SoutCommissionRecord> merged = scope switch
        {
            SoutCommissionScopeKind.Risks => all
                .Where(record => !riskIds.Contains(record.Id))
                .Concat(scopeList)
                .ToList(),
            _ => all
                .Where(record => riskIds.Contains(record.Id))
                .Concat(scopeList)
                .ToList()
        };

        Save(org, merged);
    }

    private List<SoutCommissionRecord> LoadInternal(
        string organization,
        Func<string, List<SoutCommissionRecord>> defaultsFactory)
    {
        try
        {
            if (!File.Exists(_paths.CommissionsFile))
            {
                var seeded = defaultsFactory(organization);
                File.WriteAllText(_paths.CommissionsFile, JsonSerializer.Serialize(seeded, JsonOpts));
                return seeded;
            }

            var json = File.ReadAllText(_paths.CommissionsFile);
            var list = JsonSerializer.Deserialize<List<SoutCommissionRecord>>(json, JsonOpts) ?? [];
            if (list.Count == 0)
            {
                list = defaultsFactory(organization);
                File.WriteAllText(_paths.CommissionsFile, JsonSerializer.Serialize(list, JsonOpts));
            }

            foreach (var item in list)
            {
                item.Members ??= [];
                if (item.Members.Count == 0 && !string.IsNullOrWhiteSpace(item.Composition))
                {
                    item.Members = SoutCommissionMemberHelpers.MigrateFromCompositionText(item.Composition);
                }
            }

            return list
                .Where(item => string.IsNullOrWhiteSpace(organization) ||
                               string.Equals(item.Organization, organization, StringComparison.OrdinalIgnoreCase) ||
                               OrganizationMatcher.Matches(organization, item.Organization) ||
                               OrganizationMatcher.Matches(item.Organization, organization))
                .ToList();
        }
        catch
        {
            return defaultsFactory(organization);
        }
    }

    private static void SaveInternal(string filePath, string organization, IEnumerable<SoutCommissionRecord> records)
    {
        var org = organization.Trim();
        var list = new List<SoutCommissionRecord>();
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            list = JsonSerializer.Deserialize<List<SoutCommissionRecord>>(json, JsonOpts) ?? [];
            list.RemoveAll(item => string.Equals(item.Organization, org, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var record in records)
        {
            record.Organization = org;
            record.ModifiedAt = DateTime.UtcNow;
            if (record.CreatedAt == default)
            {
                record.CreatedAt = DateTime.UtcNow;
            }

            list.Add(record);
        }

        File.WriteAllText(filePath, JsonSerializer.Serialize(list, JsonOpts));
    }
}

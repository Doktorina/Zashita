using System.Text.Json;

namespace Zashita.Core.Risks;

/// <summary>
/// Справочники оборудования и материалов — общие для всех юридических лиц.
/// </summary>
public sealed class SoutCatalogStore
{
    public const string SharedOrganizationKey = "*";

    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SoutCatalogStore(RiskDataPaths paths) => _paths = paths;

    public List<SoutCatalogRecord> LoadEquipment(string organization) =>
        LoadShared(_paths.EquipmentFile, SoutCatalogDefaults.CreateEquipment);

    public List<SoutCatalogRecord> LoadMaterials(string organization) =>
        LoadShared(_paths.MaterialsFile, SoutCatalogDefaults.CreateMaterials);

    public void SaveEquipment(string organization, IEnumerable<SoutCatalogRecord> records) =>
        SaveShared(_paths.EquipmentFile, records);

    public void SaveMaterials(string organization, IEnumerable<SoutCatalogRecord> records) =>
        SaveShared(_paths.MaterialsFile, records);

    public SoutCatalogRecord? FindEquipment(string organization, string id) =>
        LoadEquipment(organization).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public SoutCatalogRecord? FindMaterials(string organization, string id) =>
        LoadMaterials(organization).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public void UpsertEquipment(string organization, SoutCatalogRecord record)
    {
        var list = LoadEquipment(organization);
        var index = list.FindIndex(item => string.Equals(item.Id, record.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            list[index] = record;
        }
        else
        {
            list.Add(record);
        }

        SaveEquipment(organization, list);
    }

    public void UpsertMaterials(string organization, SoutCatalogRecord record)
    {
        var list = LoadMaterials(organization);
        var index = list.FindIndex(item => string.Equals(item.Id, record.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            list[index] = record;
        }
        else
        {
            list.Add(record);
        }

        SaveMaterials(organization, list);
    }

    public string SuggestNextCode(IEnumerable<SoutCatalogRecord> records)
    {
        var max = 0;
        foreach (var record in records)
        {
            if (int.TryParse(record.Code, out var code) && code > max)
            {
                max = code;
            }
        }

        return (max + 1).ToString();
    }

    private List<SoutCatalogRecord> LoadShared(
        string filePath,
        Func<string, List<SoutCatalogRecord>> defaultsFactory)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                var seeded = defaultsFactory(SharedOrganizationKey);
                foreach (var item in seeded)
                {
                    item.Organization = SharedOrganizationKey;
                    item.WorkplaceIds ??= [];
                }

                File.WriteAllText(filePath, JsonSerializer.Serialize(seeded, JsonOpts));
                return seeded;
            }

            var json = File.ReadAllText(filePath);
            var list = JsonSerializer.Deserialize<List<SoutCatalogRecord>>(json, JsonOpts) ?? [];
            if (list.Count == 0)
            {
                list = defaultsFactory(SharedOrganizationKey);
                foreach (var item in list)
                {
                    item.Organization = SharedOrganizationKey;
                }

                File.WriteAllText(filePath, JsonSerializer.Serialize(list, JsonOpts));
            }

            // Объединяем бывшие записи по разным ЮЛ в один общий список (без дублей по Id).
            var byId = new Dictionary<string, SoutCatalogRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                item.WorkplaceIds ??= [];
                item.Organization = SharedOrganizationKey;
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    continue;
                }

                if (!byId.ContainsKey(item.Id))
                {
                    byId[item.Id] = item;
                }
                else
                {
                    // Сливаем WorkplaceIds при дублях Id из разных орг.
                    var existing = byId[item.Id];
                    existing.WorkplaceIds ??= [];
                    foreach (var wid in item.WorkplaceIds)
                    {
                        if (!existing.WorkplaceIds.Contains(wid, StringComparer.OrdinalIgnoreCase))
                        {
                            existing.WorkplaceIds.Add(wid);
                        }
                    }
                }
            }

            return byId.Values
                .OrderBy(x => x.IsGroup ? 0 : 1)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return defaultsFactory(SharedOrganizationKey);
        }
    }

    private void SaveShared(string filePath, IEnumerable<SoutCatalogRecord> records)
    {
        var list = new List<SoutCatalogRecord>();
        foreach (var record in records)
        {
            record.Organization = SharedOrganizationKey;
            record.WorkplaceIds ??= [];
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

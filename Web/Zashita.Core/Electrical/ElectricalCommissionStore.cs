using System.Text.Json;

namespace Zashita.Core.Electrical;

public sealed class ElectricalCommissionStore
{
    private readonly ElectricalDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _gate = new();

    public ElectricalCommissionStore(ElectricalDataPaths paths) => _paths = paths;

    public List<ElectricalCommissionRecord> Load(string organization)
    {
        lock (_gate)
        {
            var all = LoadAllUnlocked();
            var org = (organization ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(org))
            {
                return all;
            }

            return all
                .Where(r => string.Equals(r.Organization, org, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public ElectricalCommissionRecord? Find(string organization, string id) =>
        Load(organization).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    public void Save(string organization, IEnumerable<ElectricalCommissionRecord> records)
    {
        lock (_gate)
        {
            var org = (organization ?? string.Empty).Trim();
            var incoming = records.Select(r =>
            {
                r.Organization = org;
                r.ModifiedAt = DateTime.UtcNow;
                ElectricalCommissionMemberHelpers.SyncComposition(r);
                return r;
            }).ToList();

            var others = LoadAllUnlocked()
                .Where(r => !string.Equals(r.Organization, org, StringComparison.OrdinalIgnoreCase))
                .ToList();
            others.AddRange(incoming);
            WriteAllUnlocked(others);
        }
    }

    public void Upsert(string organization, ElectricalCommissionRecord record)
    {
        var list = Load(organization);
        var index = list.FindIndex(item => string.Equals(item.Id, record.Id, StringComparison.Ordinal));
        record.Organization = organization.Trim();
        record.ModifiedAt = DateTime.UtcNow;
        ElectricalCommissionMemberHelpers.SyncComposition(record);
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

    public void RenameOrganization(string oldName, string newName)
    {
        var from = (oldName ?? string.Empty).Trim();
        var to = (newName ?? string.Empty).Trim();
        if (from.Length == 0 || to.Length == 0
            || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            var list = LoadAllUnlocked();
            var changed = false;
            foreach (var record in list)
            {
                if (!string.Equals(record.Organization, from, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                record.Organization = to;
                changed = true;
            }

            if (changed)
            {
                WriteAllUnlocked(list);
            }
        }
    }

    private List<ElectricalCommissionRecord> LoadAllUnlocked()
    {
        try
        {
            if (!File.Exists(_paths.CommissionsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.CommissionsFile);
            var list = JsonSerializer.Deserialize<List<ElectricalCommissionRecord>>(json, JsonOpts) ?? [];
            foreach (var item in list)
            {
                item.Members ??= [];
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private void WriteAllUnlocked(List<ElectricalCommissionRecord> records)
    {
        var dir = Path.GetDirectoryName(_paths.CommissionsFile);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_paths.CommissionsFile, JsonSerializer.Serialize(records, JsonOpts));
    }
}

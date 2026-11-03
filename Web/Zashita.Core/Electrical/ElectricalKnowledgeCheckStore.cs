using System.Text.Json;

namespace Zashita.Core.Electrical;

public sealed class ElectricalKnowledgeCheckStore
{
    private readonly ElectricalDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly JsonFileListCache<ElectricalKnowledgeCheckRecord> _cache = new();

    public ElectricalKnowledgeCheckStore(ElectricalDataPaths paths) => _paths = paths;

    public List<ElectricalKnowledgeCheckRecord> Load(string organization)
    {
        lock (_gate)
        {
            var all = LoadAllUnlocked();
            var org = (organization ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(org))
            {
                return all.ToList();
            }

            return all
                .Where(r => string.Equals(r.Organization, org, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public List<ElectricalKnowledgeCheckRecord> LoadAll()
    {
        lock (_gate)
        {
            return LoadAllUnlocked().ToList();
        }
    }

    public void Save(string organization, IEnumerable<ElectricalKnowledgeCheckRecord> records)
    {
        lock (_gate)
        {
            var org = (organization ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(org))
            {
                // Пустая организация в ключе Save недопустима — иначе затрём Organization у всех строк.
                return;
            }

            var incoming = records
                .Select(r =>
                {
                    r.Organization = org;
                    r.ModifiedAt = DateTime.UtcNow;
                    return r;
                })
                .ToList();

            var others = LoadAllUnlocked()
                .Where(r => !string.Equals(r.Organization, org, StringComparison.OrdinalIgnoreCase))
                .ToList();
            others.AddRange(incoming);
            WriteAllUnlocked(others);
        }
    }

    /// <summary>
    /// Заменяет записи только для указанных организаций; остальные организации не трогает.
    /// </summary>
    public void SaveForOrganizations(
        IReadOnlyCollection<string> organizations,
        IEnumerable<ElectricalKnowledgeCheckRecord> records)
    {
        lock (_gate)
        {
            var orgSet = organizations
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (orgSet.Count == 0)
            {
                return;
            }

            var incoming = records
                .Where(r => !string.IsNullOrWhiteSpace(r.Organization)
                            && orgSet.Contains(r.Organization.Trim()))
                .Select(r =>
                {
                    r.Organization = r.Organization.Trim();
                    r.ModifiedAt = DateTime.UtcNow;
                    return r;
                })
                .ToList();

            var others = LoadAllUnlocked()
                .Where(r => !orgSet.Contains((r.Organization ?? string.Empty).Trim()))
                .ToList();
            others.AddRange(incoming);
            WriteAllUnlocked(others);
        }
    }

    /// <summary>
    /// Слияние: обновляет/добавляет строки из формы, удаляет только явно помеченные Id.
    /// Строки, которых не было в POST (фильтр/ошибка UI), не затираются.
    /// </summary>
    public void MergeUpsert(
        IEnumerable<ElectricalKnowledgeCheckRecord> upserts,
        IEnumerable<string>? deletedIds = null)
    {
        lock (_gate)
        {
            var list = LoadAllUnlocked();
            var delete = (deletedIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (delete.Count > 0)
            {
                list.RemoveAll(r => delete.Contains(r.Id));
            }

            var byId = list
                .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var row in upserts ?? [])
            {
                if (string.IsNullOrWhiteSpace(row.Id))
                {
                    row.Id = Guid.NewGuid().ToString("N");
                }

                row.Organization = (row.Organization ?? string.Empty).Trim();
                row.ModifiedAt = DateTime.UtcNow;
                if (byId.TryGetValue(row.Id, out var existing))
                {
                    var idx = list.IndexOf(existing);
                    if (idx >= 0)
                    {
                        list[idx] = row;
                    }

                    byId[row.Id] = row;
                }
                else
                {
                    list.Add(row);
                    byId[row.Id] = row;
                }
            }

            WriteAllUnlocked(list);
        }
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
                record.ModifiedAt = DateTime.UtcNow;
                changed = true;
            }

            if (changed)
            {
                WriteAllUnlocked(list);
            }
        }
    }

    /// <summary>
    /// Атомарно меняет весь список записей (для кросс-орг. синхронизации аттестации).
    /// </summary>
    public bool MutateAll(Func<List<ElectricalKnowledgeCheckRecord>, bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        lock (_gate)
        {
            var list = LoadAllUnlocked();
            if (!mutate(list))
            {
                return false;
            }

            WriteAllUnlocked(list);
            return true;
        }
    }

    private List<ElectricalKnowledgeCheckRecord> LoadAllUnlocked() =>
        _cache.GetOrLoad(_paths.KnowledgeChecksFile, LoadFromDisk);

    private List<ElectricalKnowledgeCheckRecord> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_paths.KnowledgeChecksFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.KnowledgeChecksFile);
            return JsonSerializer.Deserialize<List<ElectricalKnowledgeCheckRecord>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteAllUnlocked(List<ElectricalKnowledgeCheckRecord> records)
    {
        var dir = Path.GetDirectoryName(_paths.KnowledgeChecksFile);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_paths.KnowledgeChecksFile, JsonSerializer.Serialize(records, JsonOpts));
        _cache.Replace(records, _paths.KnowledgeChecksFile);
    }
}

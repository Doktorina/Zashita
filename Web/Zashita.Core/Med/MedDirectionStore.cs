using System.Text.Json;

namespace Zashita.Core.Med;

public sealed class MedDirectionStore
{
    private readonly MedDataPaths _paths;
    private readonly MedJsonListCache<MedDirectionRecord> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedDirectionStore(MedDataPaths paths) => _paths = paths;

    public List<MedDirectionRecord> LoadAll() =>
        _cache.GetOrLoad(_paths.DirectionsFile, LoadFromDisk);

    private List<MedDirectionRecord> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_paths.DirectionsFile))
            {
                return [];
            }

            var list = JsonSerializer.Deserialize<List<MedDirectionRecord>>(
                File.ReadAllText(_paths.DirectionsFile), JsonOpts) ?? [];
            foreach (var item in list)
            {
                item.Factors ??= [];
                item.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public List<MedDirectionRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(x => MedOrganizationFilter.Matches(organization, x.Organization))
            .OrderByDescending(x => x.Date)
            .ThenBy(x => x.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public int AlignOrganizationNames(IEnumerable<string> canonicalOrganizations)
    {
        var list = LoadAll();
        var changed = MedOrganizationFilter.AlignOrganizations(
            list,
            record => record.Organization,
            (record, name) => record.Organization = name,
            canonicalOrganizations);
        if (changed > 0)
        {
            Persist(list);
        }

        return changed;
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

        var list = LoadAll();
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
            Persist(list);
        }
    }

    private void Persist(List<MedDirectionRecord> list)
    {
        File.WriteAllText(_paths.DirectionsFile, JsonSerializer.Serialize(list, JsonOpts));
        _cache.Replace(list, _paths.DirectionsFile);
    }

    public MedDirectionRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void Upsert(MedDirectionRecord record)
    {
        record.Factors ??= [];
        record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = record;
        else list.Add(record);
        File.WriteAllText(_paths.DirectionsFile, JsonSerializer.Serialize(list, JsonOpts));
        _cache.Replace(list, _paths.DirectionsFile);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        File.WriteAllText(_paths.DirectionsFile, JsonSerializer.Serialize(list, JsonOpts));
        _cache.Replace(list, _paths.DirectionsFile);
    }
}

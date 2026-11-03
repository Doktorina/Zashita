using System.Text.Json;

namespace Zashita.Core.Ppe;

public sealed record PpeInternalOrderRecord(
    string Id,
    string Organization,
    string Number,
    DateTime Date,
    string Department,
    string Status,
    string Comment,
    DateTime UpdatedAt);

public sealed class PpeInternalOrdersStore
{
    private readonly PpeDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PpeInternalOrdersStore(PpeDataPaths paths) => _paths = paths;

    public List<PpeInternalOrderRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.InternalOrdersFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.InternalOrdersFile);
            return JsonSerializer.Deserialize<List<PpeInternalOrderRecord>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public List<PpeInternalOrderRecord> LoadForOrganization(string organization) =>
        LoadAll()
            .Where(r => string.Equals(r.Organization.Trim(), organization.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.UpdatedAt)
            .ToList();

    public PpeInternalOrderRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    public void Upsert(PpeInternalOrderRecord record)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            list[idx] = record;
        }
        else
        {
            list.Add(record);
        }

        File.WriteAllText(_paths.InternalOrdersFile, JsonSerializer.Serialize(list, JsonOpts));
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        File.WriteAllText(_paths.InternalOrdersFile, JsonSerializer.Serialize(list, JsonOpts));
    }
}

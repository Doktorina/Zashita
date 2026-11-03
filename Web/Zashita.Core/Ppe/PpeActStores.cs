using System.Text.Json;

namespace Zashita.Core.Ppe;

public sealed class PpeChangesStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PpeChangesStore(PpeDataPaths paths) => _filePath = paths.ChangesFile;

    public List<PpeActRecord> LoadAll() => LoadAllInternal(_filePath);

    public List<PpeActRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(x => string.Equals(x.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.ModifiedAt)
            .ToList();

    public PpeActRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void Upsert(PpeActRecord record) => UpsertInternal(_filePath, record);

    public void Delete(string id) => DeleteInternal(_filePath, id);

    public string SuggestNextNumber(string organization) =>
        SuggestNextNumberInternal(LoadByOrganization(organization), "ИЗМ");

    private static List<PpeActRecord> LoadAllInternal(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<PpeActRecord>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void UpsertInternal(string filePath, PpeActRecord record)
    {
        var now = DateTime.UtcNow;
        var list = LoadAllInternal(filePath);
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        record.ModifiedAt = now;
        if (idx >= 0)
        {
            record.CreatedAt = list[idx].CreatedAt;
            list[idx] = record;
        }
        else
        {
            record.CreatedAt = now;
            list.Add(record);
        }

        File.WriteAllText(filePath, JsonSerializer.Serialize(
            list.OrderByDescending(x => x.Date).ThenByDescending(x => x.ModifiedAt),
            JsonOpts));
    }

    private static void DeleteInternal(string filePath, string id)
    {
        var list = LoadAllInternal(filePath);
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        File.WriteAllText(filePath, JsonSerializer.Serialize(list, JsonOpts));
    }

    private static string SuggestNextNumberInternal(IEnumerable<PpeActRecord> rows, string prefix)
    {
        var max = rows
            .Select(x =>
            {
                var n = (x.Number ?? string.Empty).Trim();
                if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                var tail = n[prefix.Length..].TrimStart('-', ' ');
                return int.TryParse(tail, out var parsed) ? parsed : 0;
            })
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}-{(max + 1):D6}";
    }
}

public sealed class PpeWriteoffStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PpeWriteoffStore(PpeDataPaths paths) => _filePath = paths.WriteoffFile;

    public List<PpeActRecord> LoadAll() => LoadAllInternal(_filePath);

    public List<PpeActRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(x => string.Equals(x.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.ModifiedAt)
            .ToList();

    public PpeActRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void Upsert(PpeActRecord record) => UpsertInternal(_filePath, record);

    public void Delete(string id) => DeleteInternal(_filePath, id);

    public string SuggestNextNumber(string organization) =>
        SuggestNextNumberInternal(LoadByOrganization(organization), "СПС");

    private static List<PpeActRecord> LoadAllInternal(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<PpeActRecord>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void UpsertInternal(string filePath, PpeActRecord record)
    {
        var now = DateTime.UtcNow;
        var list = LoadAllInternal(filePath);
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        record.ModifiedAt = now;
        if (idx >= 0)
        {
            record.CreatedAt = list[idx].CreatedAt;
            list[idx] = record;
        }
        else
        {
            record.CreatedAt = now;
            list.Add(record);
        }

        File.WriteAllText(filePath, JsonSerializer.Serialize(
            list.OrderByDescending(x => x.Date).ThenByDescending(x => x.ModifiedAt),
            JsonOpts));
    }

    private static void DeleteInternal(string filePath, string id)
    {
        var list = LoadAllInternal(filePath);
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        File.WriteAllText(filePath, JsonSerializer.Serialize(list, JsonOpts));
    }

    private static string SuggestNextNumberInternal(IEnumerable<PpeActRecord> rows, string prefix)
    {
        var max = rows
            .Select(x =>
            {
                var n = (x.Number ?? string.Empty).Trim();
                if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                var tail = n[prefix.Length..].TrimStart('-', ' ');
                return int.TryParse(tail, out var parsed) ? parsed : 0;
            })
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}-{(max + 1):D6}";
    }
}

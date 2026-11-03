using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zashita.Core.Ppe;

public sealed class SavedSiosCardsStore
{
    private readonly PpeDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SavedSiosCardsStore(PpeDataPaths paths) => _paths = paths;

    public List<SavedSiosCardRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.SavedCardsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.SavedCardsFile);
            return JsonSerializer.Deserialize<List<SavedSiosCardRecord>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public SavedSiosCardRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void SaveAll(IEnumerable<SavedSiosCardRecord> records)
    {
        var json = JsonSerializer.Serialize(
            records.OrderByDescending(x => x.UpdatedAt),
            JsonOpts);
        File.WriteAllText(_paths.SavedCardsFile, json);
    }

    public void Upsert(SavedSiosCardRecord record)
    {
        var now = DateTime.UtcNow;
        record.UpdatedAt = now;
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            if (list[idx].CreatedAt != default) record.CreatedAt = list[idx].CreatedAt;
            else record.CreatedAt = now;
            list[idx] = record;
        }
        else
        {
            record.CreatedAt = now;
            list.Add(record);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        var json = JsonSerializer.Serialize(list.OrderByDescending(x => x.UpdatedAt), JsonOpts);
        File.WriteAllText(_paths.SavedCardsFile, json);
    }

    public int SuggestNextCardNumber(string organization, string documentName)
    {
        var org = organization.Trim();
        var doc = documentName.Trim();
        var max = LoadAll()
            .Where(r => string.Equals(r.Organization.Trim(), org, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.DocumentName.Trim(), doc, StringComparison.OrdinalIgnoreCase))
            .Select(r => int.TryParse(r.CardNumber.Trim(), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return max + 1;
    }
}

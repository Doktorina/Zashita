using System.Text.Json;

namespace Zashita.Core.Ppe;

public sealed class PpeAcceptanceDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string Warehouse { get; set; } = "Склад спецодежды";
    public string Department { get; set; } = string.Empty;
    public bool IsPosted { get; set; }
    public List<PpeAcceptanceLine> Lines { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PpeAcceptanceStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PpeAcceptanceStore(PpeDataPaths paths) => _filePath = paths.AcceptanceFile;

    public List<PpeAcceptanceDocument> LoadAll()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<PpeAcceptanceDocument>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public List<PpeAcceptanceDocument> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(x => string.Equals(x.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.ModifiedAt)
            .ToList();

    public PpeAcceptanceDocument? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void Upsert(PpeAcceptanceDocument doc)
    {
        var now = DateTime.UtcNow;
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, doc.Id, StringComparison.Ordinal));
        doc.ModifiedAt = now;
        if (idx >= 0)
        {
            doc.CreatedAt = list[idx].CreatedAt;
            list[idx] = doc;
        }
        else
        {
            doc.CreatedAt = now;
            list.Add(doc);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(
            list.OrderByDescending(x => x.Date).ThenByDescending(x => x.ModifiedAt),
            JsonOpts));
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        File.WriteAllText(_filePath, JsonSerializer.Serialize(list, JsonOpts));
    }

    public string SuggestNextNumber(string organization) =>
        SuggestNextNumberInternal(LoadByOrganization(organization), "ПРМ");

    private static string SuggestNextNumberInternal(IEnumerable<PpeAcceptanceDocument> rows, string prefix)
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


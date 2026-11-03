using System.Text.Json;

namespace Zashita.Core.Ppe;

public sealed class IssueStandardsStore
{
    private readonly PpeDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public IssueStandardsStore(PpeDataPaths paths) => _paths = paths;

    public List<IssueStandard> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.IssueStandardsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.IssueStandardsFile);
            return ParseStandardsJson(json);
        }
        catch
        {
            return [];
        }
    }

    private static List<IssueStandard> ParseStandardsJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<IssueStandard>();
        CollectStandards(doc.RootElement, result);
        return result
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private static void CollectStandards(JsonElement element, List<IssueStandard> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStandards(item, result);
                }

                break;
            case JsonValueKind.Object when element.TryGetProperty("value", out var wrapped):
                CollectStandards(wrapped, result);
                break;
            case JsonValueKind.Object:
            {
                var standard = JsonSerializer.Deserialize<IssueStandard>(element.GetRawText(), JsonOpts);
                if (IsValidStandard(standard))
                {
                    result.Add(standard!);
                }

                break;
            }
        }
    }

    private static bool IsValidStandard(IssueStandard? standard) =>
        standard is not null &&
        !string.IsNullOrWhiteSpace(standard.Id) &&
        !string.IsNullOrWhiteSpace(standard.Name);

    public void SaveAll(IEnumerable<IssueStandard> standards)
    {
        var data = standards
            .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        File.WriteAllText(_paths.IssueStandardsFile, JsonSerializer.Serialize(data, JsonOpts));
    }

    public void Upsert(IssueStandard standard)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, standard.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = standard;
        else list.Add(standard);
        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }
}

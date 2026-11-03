using System.Text.Json;

namespace Zashita.Core.Suot;

public sealed class SuotIndicatorStore
{
    private readonly SuotDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SuotIndicatorStore(SuotDataPaths paths) => _paths = paths;

    public List<SuotIndicator> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.IndicatorsFile))
            {
                var seed = SuotIndicatorDefaults.CreateSeed();
                SaveAll(seed);
                return seed;
            }

            var json = File.ReadAllText(_paths.IndicatorsFile);
            var data = JsonSerializer.Deserialize<List<SuotIndicator>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : SuotIndicatorDefaults.CreateSeed();
        }
        catch
        {
            return SuotIndicatorDefaults.CreateSeed();
        }
    }

    public void SaveAll(IEnumerable<SuotIndicator> indicators)
    {
        var data = indicators
            .Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Name))
            .ToList();
        File.WriteAllText(_paths.IndicatorsFile, JsonSerializer.Serialize(data, JsonOpts));
    }

    public SuotIndicator? GetById(string id) =>
        LoadAll().FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    public void Upsert(SuotIndicator indicator)
    {
        var list = LoadAll();
        if (string.IsNullOrWhiteSpace(indicator.Code))
        {
            indicator.Code = NextCode(list);
        }

        var idx = list.FindIndex(i => string.Equals(i.Id, indicator.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            list[idx] = indicator;
        }
        else
        {
            list.Add(indicator);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        list.RemoveAll(i => string.Equals(i.ParentId, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    public void BulkUpdate(IEnumerable<string> ids, Action<SuotIndicator> update)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        var list = LoadAll();
        foreach (var item in list)
        {
            if (set.Contains(item.Id))
            {
                update(item);
            }
        }

        SaveAll(list);
    }

    private static string NextCode(IReadOnlyList<SuotIndicator> list)
    {
        var max = list
            .Select(i => int.TryParse(i.Code, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString("D9");
    }
}

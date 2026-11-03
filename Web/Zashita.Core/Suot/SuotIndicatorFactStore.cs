using System.Text.Json;

namespace Zashita.Core.Suot;

public sealed class SuotIndicatorFactStore
{
    private readonly SuotDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SuotIndicatorFactStore(SuotDataPaths paths) => _paths = paths;

    public List<SuotIndicatorFact> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.FactsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.FactsFile);
            return JsonSerializer.Deserialize<List<SuotIndicatorFact>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public List<SuotIndicatorFact> LoadFiltered(string? organization, DateTime? from, DateTime? to)
    {
        return LoadAll()
            .Where(f =>
                (string.IsNullOrWhiteSpace(organization) ||
                 string.Equals(f.Organization, organization, StringComparison.OrdinalIgnoreCase)) &&
                (!from.HasValue || f.PeriodEnd.Date >= from.Value.Date) &&
                (!to.HasValue || f.PeriodStart.Date <= to.Value.Date))
            .OrderByDescending(f => f.PeriodEnd)
            .ThenBy(f => f.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void SaveAll(IEnumerable<SuotIndicatorFact> facts)
    {
        var data = facts.Where(f => !string.IsNullOrWhiteSpace(f.Id)).ToList();
        File.WriteAllText(_paths.FactsFile, JsonSerializer.Serialize(data, JsonOpts));
    }

    public void Upsert(SuotIndicatorFact fact)
    {
        var list = LoadAll();
        var idx = list.FindIndex(f => string.Equals(f.Id, fact.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            list[idx] = fact;
        }
        else
        {
            list.Add(fact);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(f => string.Equals(f.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }
}

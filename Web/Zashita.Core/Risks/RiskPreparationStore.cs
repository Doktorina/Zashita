using System.Text.Json;
using Zashita.Core.Incidents;

namespace Zashita.Core.Risks;

public sealed class RiskPreparationStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RiskPreparationStore(RiskDataPaths paths) => _paths = paths;

    public List<RiskPreparationDocument> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.RiskPreparationFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.RiskPreparationFile);
            return JsonSerializer.Deserialize<List<RiskPreparationDocument>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public List<RiskPreparationDocument> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(doc => string.IsNullOrWhiteSpace(organization) ||
                          string.Equals(doc.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(doc => doc.Date)
            .ThenByDescending(doc => doc.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public RiskPreparationDocument? GetById(string id) =>
        LoadAll().FirstOrDefault(doc => string.Equals(doc.Id, id, StringComparison.Ordinal));

    public void SaveAll(IEnumerable<RiskPreparationDocument> records) =>
        File.WriteAllText(_paths.RiskPreparationFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void Upsert(RiskPreparationDocument record)
    {
        var list = LoadAll();
        record.ModifiedAt = DateTime.UtcNow;
        var idx = list.FindIndex(doc => string.Equals(doc.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            record.CreatedAt = list[idx].CreatedAt;
            list[idx] = record;
        }
        else
        {
            record.CreatedAt = DateTime.UtcNow;
            list.Add(record);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(doc => string.Equals(doc.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    public string SuggestNextNumber() =>
        IncidentNumberHelper.NextNumber("0000-", LoadAll().Select(doc => doc.Number), pad: 6);
}

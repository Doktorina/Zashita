using System.Text.Json;
using Zashita.Core.Incidents;

namespace Zashita.Core.Risks;

public sealed class SoutPreparationStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SoutPreparationStore(RiskDataPaths paths) => _paths = paths;

    public List<SoutPreparationDocument> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.PreparationFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.PreparationFile);
            return JsonSerializer.Deserialize<List<SoutPreparationDocument>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public List<SoutPreparationDocument> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(doc => string.IsNullOrWhiteSpace(organization) ||
                          string.Equals(doc.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(doc => doc.Date)
            .ThenByDescending(doc => doc.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public SoutPreparationDocument? GetById(string id) =>
        LoadAll().FirstOrDefault(doc => string.Equals(doc.Id, id, StringComparison.Ordinal));

    public void SaveAll(IEnumerable<SoutPreparationDocument> records) =>
        File.WriteAllText(_paths.PreparationFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void Upsert(SoutPreparationDocument record)
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
        IncidentNumberHelper.NextNumber("ОТ-", LoadAll().Select(doc => doc.Number), pad: 8);
}

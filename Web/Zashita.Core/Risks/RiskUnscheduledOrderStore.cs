using System.Text.Json;
using Zashita.Core.Incidents;

namespace Zashita.Core.Risks;

public sealed class RiskUnscheduledOrderStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RiskUnscheduledOrderStore(RiskDataPaths paths) => _paths = paths;

    public List<RiskUnscheduledOrderDocument> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.UnscheduledOrdersFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.UnscheduledOrdersFile);
            return JsonSerializer.Deserialize<List<RiskUnscheduledOrderDocument>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public List<RiskUnscheduledOrderDocument> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(doc => string.IsNullOrWhiteSpace(organization) ||
                          string.Equals(doc.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(doc => doc.Date)
            .ThenByDescending(doc => doc.CreatedAt)
            .ThenByDescending(doc => doc.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public RiskUnscheduledOrderDocument? GetById(string id) =>
        LoadAll().FirstOrDefault(doc => string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase));

    public void SaveAll(IEnumerable<RiskUnscheduledOrderDocument> records) =>
        File.WriteAllText(_paths.UnscheduledOrdersFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void Upsert(RiskUnscheduledOrderDocument record)
    {
        var list = LoadAll();
        record.ModifiedAt = DateTime.UtcNow;
        var idx = list.FindIndex(doc => string.Equals(doc.Id, record.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            record.CreatedAt = list[idx].CreatedAt;
            list[idx] = record;
        }
        else
        {
            record.CreatedAt = DateTime.UtcNow;
            list.Insert(0, record);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(doc => string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase));
        SaveAll(list);
    }

    public string SuggestNextNumber() =>
        IncidentNumberHelper.NextNumber("0000-", LoadAll().Select(doc => doc.Number), pad: 6);
}

using System.Text.Json;

namespace Zashita.Core.Risks;

public sealed class RiskHazardAssessmentStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RiskHazardAssessmentStore(RiskDataPaths paths) => _paths = paths;

    public List<RiskHazardAssessmentRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.HazardAssessmentsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.HazardAssessmentsFile);
            var doc = JsonSerializer.Deserialize<RiskHazardAssessmentDocument>(json, JsonOpts);
            return doc?.Items ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveAll(IEnumerable<RiskHazardAssessmentRecord> records)
    {
        var doc = new RiskHazardAssessmentDocument { Items = records.ToList() };
        File.WriteAllText(_paths.HazardAssessmentsFile, JsonSerializer.Serialize(doc, JsonOpts));
    }

    public void UpsertMany(IEnumerable<RiskHazardAssessmentRecord> records)
    {
        var list = LoadAll();
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Id))
            {
                record.Id = Guid.NewGuid().ToString("N");
            }

            var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                list[idx] = record;
            }
            else
            {
                list.Add(record);
            }
        }

        SaveAll(list);
    }
}

using System.Text.Json;

namespace Zashita.Core.Training;

public sealed class TrainingAnnualPlanStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingAnnualPlanStore(TrainingDataPaths paths) => _paths = paths;

    public TrainingAnnualPlanRecord? Get(string organization, int year)
    {
        var org = (organization ?? string.Empty).Trim();
        return LoadAll().FirstOrDefault(x =>
            x.Year == year &&
            string.Equals(x.Organization, org, StringComparison.OrdinalIgnoreCase));
    }

    public void Upsert(TrainingAnnualPlanRecord record)
    {
        var list = LoadAll();
        var org = record.Organization.Trim();
        list.RemoveAll(x =>
            x.Year == record.Year &&
            string.Equals(x.Organization, org, StringComparison.OrdinalIgnoreCase));
        record.Organization = org;
        record.ModifiedAt = DateTime.UtcNow;
        list.Add(record);
        SaveAll(list);
    }

    public void Delete(string organization, int year)
    {
        var org = organization.Trim();
        var list = LoadAll();
        list.RemoveAll(x =>
            x.Year == year &&
            string.Equals(x.Organization, org, StringComparison.OrdinalIgnoreCase));
        SaveAll(list);
    }

    private List<TrainingAnnualPlanRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.AnnualPlansFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.AnnualPlansFile);
            return JsonSerializer.Deserialize<List<TrainingAnnualPlanRecord>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveAll(IEnumerable<TrainingAnnualPlanRecord> records)
    {
        var data = records
            .OrderByDescending(x => x.Year)
            .ThenBy(x => x.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        File.WriteAllText(_paths.AnnualPlansFile, JsonSerializer.Serialize(data, JsonOpts));
    }
}

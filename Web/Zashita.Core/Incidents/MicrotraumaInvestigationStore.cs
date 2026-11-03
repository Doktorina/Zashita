using System.Text.Json;

namespace Zashita.Core.Incidents;

public sealed class MicrotraumaInvestigationStore
{
    private readonly IncidentDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MicrotraumaInvestigationStore(IncidentDataPaths paths) => _paths = paths;

    public List<MicrotraumaInvestigationRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.MicrotraumaInvestigationsFile))
            {
                var seed = CreateSeed();
                SaveAll(seed);
                return seed;
            }

            var json = File.ReadAllText(_paths.MicrotraumaInvestigationsFile);
            var data = JsonSerializer.Deserialize<List<MicrotraumaInvestigationRecord>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateSeed();
        }
        catch
        {
            return CreateSeed();
        }
    }

    public List<MicrotraumaInvestigationRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(r => string.IsNullOrWhiteSpace(organization) ||
                        string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.DocumentDate)
            .ToList();

    public void SaveAll(IEnumerable<MicrotraumaInvestigationRecord> records) =>
        File.WriteAllText(_paths.MicrotraumaInvestigationsFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void Upsert(MicrotraumaInvestigationRecord record)
    {
        var list = LoadAll();
        record.ModifiedAt = DateTime.UtcNow;
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = record;
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
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    public string SuggestNextNumber() =>
        IncidentNumberHelper.NextNumber(string.Empty, LoadAll().Select(r => r.Number), pad: 9);

    private static List<MicrotraumaInvestigationRecord> CreateSeed() =>
    [
        new()
        {
            Id = "mti1", Posted = true, DocumentDate = new DateTime(2025, 3, 6), Number = "000000101",
            SourceMicrotraumaId = "mt1", SourceMicrotraumaNumber = "МТ-00000001",
            EventDateTime = new DateTime(2025, 3, 5, 11, 15, 0), Victims = "Соколов А.В.",
            Department = "Склад", Organization = "ООО «ИНВИКТУС»",
            Circumstances = "Ушиб при перемещении тары без СИЗ рук.",
            Conclusion = "Провести внеплановый инструктаж."
        }
    ];
}

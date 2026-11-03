using System.Text.Json;

namespace Zashita.Core.Incidents;

public sealed class MicrotraumaStore
{
    private readonly IncidentDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MicrotraumaStore(IncidentDataPaths paths) => _paths = paths;

    public List<MicrotraumaRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.MicrotraumasFile))
            {
                var seed = CreateSeed();
                SaveAll(seed);
                return seed;
            }

            var json = File.ReadAllText(_paths.MicrotraumasFile);
            var data = JsonSerializer.Deserialize<List<MicrotraumaRecord>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateSeed();
        }
        catch
        {
            return CreateSeed();
        }
    }

    public List<MicrotraumaRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(r => string.IsNullOrWhiteSpace(organization) ||
                        string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.DocumentDate)
            .ToList();

    public void SaveAll(IEnumerable<MicrotraumaRecord> records) =>
        File.WriteAllText(_paths.MicrotraumasFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void Upsert(MicrotraumaRecord record)
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
        IncidentNumberHelper.NextNumber("МТ-", LoadAll().Select(r => r.Number));

    private static List<MicrotraumaRecord> CreateSeed() =>
    [
        new()
        {
            Id = "mt1", Posted = true, DocumentDate = new DateTime(2025, 3, 5), Number = "МТ-00000001",
            Victims = "Смирнов А.П.", Department = "Склад", Organization = "ООО «Демо Строй»",
            Place = "Склад №1", Description = "Ушиб пальца при погрузке.",
            EventDateTime = new DateTime(2025, 3, 5, 11, 15, 0)
        },
        new()
        {
            Id = "mt2", Posted = true, DocumentDate = new DateTime(2025, 4, 12), Number = "МТ-00000002",
            Victims = "Козлова М.С.", Department = "Офис", Organization = "ООО «Северные Технологии»",
            Description = "Порез при работе с канцелярией.",
            EventDateTime = new DateTime(2025, 4, 12, 15, 40, 0)
        }
    ];
}

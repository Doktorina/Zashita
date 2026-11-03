using System.Text.Json;

namespace Zashita.Core.Incidents;

public sealed class WorkplaceAccidentStore
{
    private readonly IncidentDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public WorkplaceAccidentStore(IncidentDataPaths paths) => _paths = paths;

    public List<WorkplaceAccidentRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.WorkplaceAccidentsFile))
            {
                var seed = CreateSeed();
                SaveAll(seed);
                return seed;
            }

            var json = File.ReadAllText(_paths.WorkplaceAccidentsFile);
            var data = JsonSerializer.Deserialize<List<WorkplaceAccidentRecord>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateSeed();
        }
        catch
        {
            return CreateSeed();
        }
    }

    public List<WorkplaceAccidentRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(r => string.IsNullOrWhiteSpace(organization) ||
                        string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.DocumentDate)
            .ToList();

    public void SaveAll(IEnumerable<WorkplaceAccidentRecord> records) =>
        File.WriteAllText(_paths.WorkplaceAccidentsFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void Upsert(WorkplaceAccidentRecord record)
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
        IncidentNumberHelper.NextNumber("ОТ-", LoadAll().Select(r => r.Number));

    private static List<WorkplaceAccidentRecord> CreateSeed() =>
    [
        new()
        {
            Id = "wa1", Posted = true, DocumentDate = new DateTime(2025, 1, 20), Number = "ОТ-00000008",
            IncidentType = "Несчастный случай", Victims = "Иванов И.И.",
            Place = "г. Москва, ул. Ленина, д. 1", Department = "Администрация",
            Organization = "ООО «ИНВИКТУС»", IncidentDateTime = new DateTime(2025, 1, 20, 10, 30, 0)
        },
        new()
        {
            Id = "wa2", Posted = true, DocumentDate = new DateTime(2025, 12, 24), Number = "ОТ-00000009",
            IncidentType = "Несчастный случай", Victims = "Петров П.П.",
            Place = "г. Москва, ул. Ленина, д. 1", Department = "Администрация",
            Organization = "ООО «Компания Строй Каркас»"
        },
        new()
        {
            Id = "wa3", Posted = true, DocumentDate = new DateTime(2025, 12, 24), Number = "ОТ-00000010",
            IncidentType = "Падение с высоты", Victims = "Сидоров С.С.",
            Place = "г. Москва, ул. Ленина, д. 1", Department = "Администрация",
            Organization = "ИП Алешкевич А.Д."
        }
    ];
}

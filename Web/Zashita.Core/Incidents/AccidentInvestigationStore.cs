using System.Text.Json;

namespace Zashita.Core.Incidents;

public sealed class AccidentInvestigationStore
{
    private readonly IncidentDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AccidentInvestigationStore(IncidentDataPaths paths) => _paths = paths;

    public List<AccidentInvestigationRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.InvestigationsFile))
            {
                var seed = CreateSeed();
                SaveAll(seed);
                return seed;
            }

            var json = File.ReadAllText(_paths.InvestigationsFile);
            var data = JsonSerializer.Deserialize<List<AccidentInvestigationRecord>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateSeed();
        }
        catch
        {
            return CreateSeed();
        }
    }

    public List<AccidentInvestigationRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(r => string.IsNullOrWhiteSpace(organization) ||
                        string.Equals(r.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.DocumentDate)
            .ToList();

    public void SaveAll(IEnumerable<AccidentInvestigationRecord> records) =>
        File.WriteAllText(_paths.InvestigationsFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void Upsert(AccidentInvestigationRecord record)
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

    private static List<AccidentInvestigationRecord> CreateSeed() =>
    [
        new()
        {
            Id = "inv1", Posted = true, DocumentDate = new DateTime(2024, 7, 20), Number = "000000031",
            SourceAccidentNumber = "ОТ-00000035", AccidentDateTime = new DateTime(2024, 7, 20, 14, 10, 25),
            Victims = "Крылов П.А.", Department = "Участок упаковки", Organization = "ООО «ИНВИКТУС»",
            Circumstances = "Травма при работе на линии упаковки."
        },
        new()
        {
            Id = "inv2", Posted = true, DocumentDate = new DateTime(2025, 2, 10), Number = "000000032",
            SourceAccidentNumber = "ОТ-00000012", AccidentDateTime = new DateTime(2025, 2, 10, 11, 20, 0),
            Victims = "Иванов И.И.; Петров П.П.", Department = "ТБ1", Organization = "ИП Алешкевич А.Д.",
            Circumstances = "Групповой случай при проведении работ на высоте."
        }
    ];
}

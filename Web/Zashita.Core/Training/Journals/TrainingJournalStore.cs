using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zashita.Core.Training.Journals;

public sealed class TrainingJournalSavedData
{
    public TrainingJournalParams Params { get; set; } = new(
        DateTime.Today.AddMonths(-1),
        DateTime.Today,
        null,
        null,
        null,
        false,
        true);

    public List<TrainingJournalEntry> Entries { get; set; } = [];

    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TrainingJournalStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TrainingJournalStore(TrainingDataPaths paths)
    {
        _paths = paths;
        Directory.CreateDirectory(_paths.JournalsDirectory);
    }

    public TrainingJournalSavedData Load(TrainingJournalKind kind)
    {
        try
        {
            var path = _paths.JournalFile(kind);
            if (!File.Exists(path))
            {
                return new TrainingJournalSavedData();
            }

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<TrainingJournalSavedData>(json, JsonOpts)
                       ?? new TrainingJournalSavedData();
            data.PrintOverrides = new Dictionary<string, string>(
                data.PrintOverrides ?? [],
                StringComparer.OrdinalIgnoreCase);
            return data;
        }
        catch
        {
            return new TrainingJournalSavedData();
        }
    }

    public void Save(
        TrainingJournalKind kind,
        TrainingJournalParams parameters,
        IReadOnlyList<TrainingJournalEntry> entries,
        IReadOnlyDictionary<string, string>? printOverrides = null)
    {
        Directory.CreateDirectory(_paths.JournalsDirectory);
        var existing = Load(kind);
        var data = new TrainingJournalSavedData
        {
            Params = parameters,
            Entries = entries.ToList(),
            PrintOverrides = printOverrides is null
                ? existing.PrintOverrides
                : new Dictionary<string, string>(printOverrides, StringComparer.OrdinalIgnoreCase)
        };
        var json = JsonSerializer.Serialize(data, JsonOpts);
        File.WriteAllText(_paths.JournalFile(kind), json);
    }

    public void SavePrintOverrides(TrainingJournalKind kind, IReadOnlyDictionary<string, string> overrides)
    {
        var data = Load(kind);
        data.PrintOverrides = new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Serialize(data, JsonOpts);
        File.WriteAllText(_paths.JournalFile(kind), json);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zashita.Core.Training.Journals;

public sealed class TrainingJournalHistoryEntry
{
    public int Version { get; set; }
    public DateTime SavedAt { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string? BriefingType { get; set; }
    public string? DefaultInstructor { get; set; }
    public int EntryCount { get; set; }
    public string? SnapshotFile { get; set; }
}

public sealed class TrainingJournalHistoryStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TrainingJournalHistoryStore(TrainingDataPaths paths)
    {
        _paths = paths;
        Directory.CreateDirectory(_paths.JournalsDirectory);
        Directory.CreateDirectory(HistorySnapshotsDirectory);
    }

    private string HistorySnapshotsDirectory =>
        Path.Combine(_paths.JournalsDirectory, "history");

    public IReadOnlyList<TrainingJournalHistoryEntry> Get(TrainingJournalKind kind)
    {
        var map = LoadMap();
        return map.TryGetValue(kind.ToString(), out var entries)
            ? entries.OrderByDescending(e => e.Version).ToList()
            : [];
    }

    public void Append(
        TrainingJournalKind kind,
        TrainingJournalParams parameters,
        IReadOnlyList<TrainingJournalEntry> entries,
        string action)
    {
        var map = LoadMap();
        var key = kind.ToString();
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        var next = list.Count == 0 ? 1 : list.Max(e => e.Version) + 1;
        var type = string.IsNullOrWhiteSpace(parameters.BriefingType) ? "все виды" : parameters.BriefingType!.Trim();
        var snapshotName = $"{kind}-v{next}.json";
        var snapshotPath = Path.Combine(HistorySnapshotsDirectory, snapshotName);
        Directory.CreateDirectory(HistorySnapshotsDirectory);
        File.WriteAllText(
            snapshotPath,
            JsonSerializer.Serialize(
                new TrainingJournalSavedData
                {
                    Params = parameters,
                    Entries = entries.ToList(),
                    PrintOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                },
                JsonOpts));

        list.Add(new TrainingJournalHistoryEntry
        {
            Version = next,
            SavedAt = DateTime.Now,
            Summary = $"{action}: {entries.Count} стр., вид «{type}»",
            DateFrom = parameters.DateFrom.Date,
            DateTo = parameters.DateTo.Date,
            BriefingType = parameters.BriefingType,
            DefaultInstructor = parameters.DefaultInstructor,
            EntryCount = entries.Count,
            SnapshotFile = snapshotName
        });

        if (list.Count > 50)
        {
            var keep = list.OrderByDescending(e => e.Version).Take(50).ToList();
            var remove = list.Except(keep).ToList();
            foreach (var old in remove.Where(x => !string.IsNullOrWhiteSpace(x.SnapshotFile)))
            {
                var path = Path.Combine(HistorySnapshotsDirectory, old.SnapshotFile!);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            map[key] = keep;
        }

        SaveMap(map);
    }

    public bool TryRestore(TrainingJournalKind kind, int version, out TrainingJournalSavedData? snapshot)
    {
        snapshot = null;
        var entry = Get(kind).FirstOrDefault(e => e.Version == version);
        if (entry is null || string.IsNullOrWhiteSpace(entry.SnapshotFile))
        {
            return false;
        }

        var path = Path.Combine(HistorySnapshotsDirectory, entry.SnapshotFile);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            snapshot = JsonSerializer.Deserialize<TrainingJournalSavedData>(File.ReadAllText(path), JsonOpts);
            if (snapshot is null)
            {
                return false;
            }

            snapshot.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            snapshot.Entries ??= [];
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Delete(TrainingJournalKind kind, int version)
    {
        var map = LoadMap();
        var key = kind.ToString();
        if (!map.TryGetValue(key, out var list) || list.Count == 0)
        {
            return false;
        }

        var target = list.FirstOrDefault(e => e.Version == version);
        var removed = list.RemoveAll(e => e.Version == version);
        if (removed == 0)
        {
            return false;
        }

        if (target?.SnapshotFile is { Length: > 0 } file)
        {
            var path = Path.Combine(HistorySnapshotsDirectory, file);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (list.Count == 0)
        {
            map.Remove(key);
        }
        else
        {
            map[key] = list;
        }

        SaveMap(map);
        return true;
    }

    private Dictionary<string, List<TrainingJournalHistoryEntry>> LoadMap()
    {
        try
        {
            var path = _paths.JournalsHistoryFile;
            if (!File.Exists(path))
            {
                return new Dictionary<string, List<TrainingJournalHistoryEntry>>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, List<TrainingJournalHistoryEntry>>>(json, JsonOpts)
                   ?? new Dictionary<string, List<TrainingJournalHistoryEntry>>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, List<TrainingJournalHistoryEntry>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveMap(Dictionary<string, List<TrainingJournalHistoryEntry>> map) =>
        File.WriteAllText(_paths.JournalsHistoryFile, JsonSerializer.Serialize(map, JsonOpts));
}

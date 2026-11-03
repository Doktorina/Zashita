using System.Text.Json;

namespace Zashita.Core.Training;

public sealed class TrainingStandardHistoryEntry
{
    public int Version { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime SavedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PersonnelCategory { get; set; } = string.Empty;
    public string TrainingMethod { get; set; } = string.Empty;
    public List<string> Professions { get; set; } = [];
    public List<string> Programs { get; set; } = [];
    public Dictionary<string, string> ProgramPeriodicities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TrainingStandardHistoryStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingStandardHistoryStore(TrainingDataPaths paths) => _paths = paths;

    public IReadOnlyList<TrainingStandardHistoryEntry> Get(string standardId)
    {
        if (string.IsNullOrWhiteSpace(standardId))
        {
            return [];
        }

        var map = LoadMap();
        return map.TryGetValue(standardId, out var entries)
            ? entries.OrderByDescending(e => e.Version).ToList()
            : [];
    }

    public void Append(string standardId, TrainingStandard standard)
    {
        if (string.IsNullOrWhiteSpace(standardId))
        {
            return;
        }

        var map = LoadMap();
        if (!map.TryGetValue(standardId, out var entries))
        {
            entries = [];
            map[standardId] = entries;
        }

        entries.Add(new TrainingStandardHistoryEntry
        {
            Version = standard.Version > 0 ? standard.Version : 1,
            EffectiveFrom = (standard.EffectiveFrom ?? standard.CreatedAt).ToLocalTime().Date,
            SavedAt = DateTime.UtcNow,
            Name = standard.Name,
            PersonnelCategory = standard.PersonnelCategory ?? string.Empty,
            TrainingMethod = standard.TrainingMethod ?? string.Empty,
            Professions = standard.AllProfessions.ToList(),
            Programs = standard.Items.ToList(),
            ProgramPeriodicities = TrainingProgramCatalog.CopyPeriodicitiesIgnoreCase(standard.ProgramPeriodicities)
        });

        SaveMap(map);
    }

    public void Remove(string standardId)
    {
        if (string.IsNullOrWhiteSpace(standardId))
        {
            return;
        }

        var map = LoadMap();
        if (map.Remove(standardId))
        {
            SaveMap(map);
        }
    }

    private Dictionary<string, List<TrainingStandardHistoryEntry>> LoadMap()
    {
        try
        {
            if (!File.Exists(_paths.StandardsHistoryFile))
            {
                return new Dictionary<string, List<TrainingStandardHistoryEntry>>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_paths.StandardsHistoryFile);
            return JsonSerializer.Deserialize<Dictionary<string, List<TrainingStandardHistoryEntry>>>(json, JsonOpts)
                ?? new Dictionary<string, List<TrainingStandardHistoryEntry>>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, List<TrainingStandardHistoryEntry>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveMap(Dictionary<string, List<TrainingStandardHistoryEntry>> map) =>
        File.WriteAllText(_paths.StandardsHistoryFile, JsonSerializer.Serialize(map, JsonOpts));
}

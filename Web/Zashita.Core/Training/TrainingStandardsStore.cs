using System.Text.Json;

namespace Zashita.Core.Training;

public sealed class TrainingStandardsStore
{
    private readonly TrainingDataPaths _paths;
    private readonly JsonFileListCache<TrainingStandard> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingStandardsStore(TrainingDataPaths paths) => _paths = paths;

    public List<TrainingStandard> LoadAll() =>
        _cache.GetOrLoad(_paths.StandardsFile, LoadFromDisk);

    private List<TrainingStandard> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_paths.StandardsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.StandardsFile);
            return JsonSerializer.Deserialize<List<TrainingStandard>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveAll(IEnumerable<TrainingStandard> standards)
    {
        var data = standards
            .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        File.WriteAllText(_paths.StandardsFile, JsonSerializer.Serialize(data, JsonOpts));
        _cache.Replace(data, _paths.StandardsFile);
    }

    public void Upsert(TrainingStandard standard)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, standard.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = standard;
        else list.Add(standard);
        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    public string IeStandardSuppressFilePath => _paths.IeStandardSuppressFile;

    /// <summary>Удаляет программы из всех стандартов (Items + периодичность) по именам видов обучения.</summary>
    public int RemoveProgramsByNames(IEnumerable<string> programNames)
    {
        var names = programNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return 0;
        }

        var list = LoadAll();
        var changed = 0;
        var next = new List<TrainingStandard>(list.Count);
        foreach (var standard in list)
        {
            var items = (standard.Items ?? [])
                .Where(item => !names.Contains((item ?? string.Empty).Trim()))
                .ToList();
            Dictionary<string, string>? periods = standard.ProgramPeriodicities;
            if (periods is { Count: > 0 })
            {
                periods = TrainingProgramCatalog.CopyPeriodicitiesIgnoreCase(
                    periods.Where(kv => !names.Contains(kv.Key.Trim())));
            }

            if (items.Count != (standard.Items?.Count ?? 0)
                || (periods?.Count ?? 0) != (standard.ProgramPeriodicities?.Count ?? 0))
            {
                changed++;
                next.Add(standard with
                {
                    Items = items,
                    ProgramPeriodicities = periods
                });
            }
            else
            {
                next.Add(standard);
            }
        }

        if (changed > 0)
        {
            SaveAll(next);
        }

        return changed;
    }

    /// <summary>Оставляет в стандартах только программы из переданного набора имён.</summary>
    public int RetainOnlyProgramNames(IEnumerable<string> allowedProgramNames)
    {
        var allowed = allowedProgramNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var list = LoadAll();
        var changed = 0;
        var next = new List<TrainingStandard>(list.Count);
        foreach (var standard in list)
        {
            var items = (standard.Items ?? [])
                .Where(item => allowed.Contains((item ?? string.Empty).Trim()))
                .ToList();
            Dictionary<string, string>? periods = standard.ProgramPeriodicities;
            if (periods is { Count: > 0 })
            {
                periods = TrainingProgramCatalog.CopyPeriodicitiesIgnoreCase(
                    periods.Where(kv => allowed.Contains(kv.Key.Trim())));
            }

            if (items.Count != (standard.Items?.Count ?? 0)
                || (periods?.Count ?? 0) != (standard.ProgramPeriodicities?.Count ?? 0))
            {
                changed++;
                next.Add(standard with
                {
                    Items = items,
                    ProgramPeriodicities = periods
                });
            }
            else
            {
                next.Add(standard);
            }
        }

        if (changed > 0)
        {
            SaveAll(next);
        }

        return changed;
    }
}

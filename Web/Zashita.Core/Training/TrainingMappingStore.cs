using System.Text.Json;

namespace Zashita.Core.Training;

public enum TrainingMappingKind
{
    WorkTypes,
    Kinds
}

public sealed class TrainingMappingStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingMappingStore(TrainingDataPaths paths) => _paths = paths;

    public List<TrainingProgramMappingLine> Load(TrainingMappingKind kind)
    {
        var seed = kind switch
        {
            TrainingMappingKind.WorkTypes => TrainingWorkTypeMappingDefaults.CreateSeed(),
            TrainingMappingKind.Kinds => TrainingKindMappingDefaults.CreateSeed(),
            _ => new List<TrainingProgramMappingLine>()
        };

        var file = _paths.MappingFile(kind);
        try
        {
            if (!File.Exists(file))
            {
                return seed;
            }

            var json = File.ReadAllText(file);
            var data = JsonSerializer.Deserialize<List<TrainingProgramMappingLine>>(json, JsonOpts);
            return data ?? seed;
        }
        catch
        {
            return seed;
        }
    }

    public void Save(TrainingMappingKind kind, List<TrainingProgramMappingLine> lines)
    {
        File.WriteAllText(_paths.MappingFile(kind), JsonSerializer.Serialize(lines, JsonOpts));
    }
}

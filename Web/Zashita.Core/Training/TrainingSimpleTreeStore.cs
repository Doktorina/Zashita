using System.Text.Json;

namespace Zashita.Core.Training;

public sealed class TrainingSimpleTreeStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingSimpleTreeStore(TrainingDataPaths paths) => _paths = paths;

    public List<TrainingSimpleTreeNode> Load(TrainingSimpleTreeKind kind)
    {
        var path = _paths.SimpleTreeFile(kind);
        try
        {
            if (!File.Exists(path))
            {
                return TrainingSimpleTreeDefaults.CreateSeed(kind);
            }

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<List<TrainingSimpleTreeNode>>(json, JsonOpts);
            return data ?? TrainingSimpleTreeDefaults.CreateSeed(kind);
        }
        catch
        {
            return TrainingSimpleTreeDefaults.CreateSeed(kind);
        }
    }

    public void Save(TrainingSimpleTreeKind kind, List<TrainingSimpleTreeNode> nodes)
    {
        File.WriteAllText(_paths.SimpleTreeFile(kind), JsonSerializer.Serialize(nodes, JsonOpts));
    }
}

using System.Text.Json;

namespace Zashita.Core.Training;

public sealed class TrainingKindStore
{
    private readonly TrainingDataPaths _paths;
    private readonly JsonFileListCache<TrainingKindNode> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingKindStore(TrainingDataPaths paths) => _paths = paths;

    public List<TrainingKindNode> Load() =>
        _cache.GetOrLoad(_paths.KindsFile, LoadFromDisk);

    private List<TrainingKindNode> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_paths.KindsFile))
            {
                var seeded = TrainingKindCatalogDefaults.CreateSeed();
                Save(seeded);
                return seeded;
            }

            var json = File.ReadAllText(_paths.KindsFile);
            var data = JsonSerializer.Deserialize<List<TrainingKindNode>>(json, JsonOpts)
                       ?? TrainingKindCatalogDefaults.CreateSeed();
            if (TrainingKindCatalogDefaults.TryNormalizeSections(data)
                | TrainingKindCatalogDefaults.SyncSectionsFromParentGroups(data))
            {
                Save(data);
            }

            return data;
        }
        catch
        {
            return TrainingKindCatalogDefaults.CreateSeed();
        }
    }

    public void Save(List<TrainingKindNode> nodes)
    {
        File.WriteAllText(_paths.KindsFile, JsonSerializer.Serialize(nodes, JsonOpts));
        _cache.Replace(nodes, _paths.KindsFile);
    }

    public TrainingKindNode? Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Load().FirstOrDefault(node =>
            string.Equals(node.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string SuggestNextCode(IReadOnlyList<TrainingKindNode> nodes)
    {
        var max = 0;
        foreach (var node in nodes)
        {
            if (int.TryParse(node.Code, out var number) && number > max)
            {
                max = number;
            }
        }

        return (max + 1).ToString("000000");
    }

    public void Upsert(TrainingKindNode node)
    {
        var list = Load();
        var index = list.FindIndex(item =>
            string.Equals(item.Id, node.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            list[index] = node;
        }
        else
        {
            list.Add(node);
        }

        Save(list);
    }
}

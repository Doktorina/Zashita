using System.Text.Json;

namespace Zashita.Core.Training;

public sealed class TrainingPlanTypeStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingPlanTypeStore(TrainingDataPaths paths) => _paths = paths;

    public List<TrainingPlanTypeNode> Load()
    {
        try
        {
            if (!File.Exists(_paths.PlanTypesFile))
            {
                return TrainingPlanTypeDefaults.CreateSeed();
            }

            var json = File.ReadAllText(_paths.PlanTypesFile);
            var data = JsonSerializer.Deserialize<List<TrainingPlanTypeNode>>(json, JsonOpts);
            return data ?? TrainingPlanTypeDefaults.CreateSeed();
        }
        catch
        {
            return TrainingPlanTypeDefaults.CreateSeed();
        }
    }

    public void Save(List<TrainingPlanTypeNode> nodes)
    {
        File.WriteAllText(_paths.PlanTypesFile, JsonSerializer.Serialize(nodes, JsonOpts));
    }

    public TrainingPlanTypeNode? GetById(string id) =>
        Load().FirstOrDefault(node => node.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

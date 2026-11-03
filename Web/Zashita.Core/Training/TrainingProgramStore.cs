using System.Text.Json;

namespace Zashita.Core.Training;

public sealed class TrainingProgramStore
{
    private readonly TrainingDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public TrainingProgramStore(TrainingDataPaths paths) => _paths = paths;

    public List<TrainingProgramCourseNode> Load()
    {
        var nodes = LoadRaw();
        var withoutDemo = nodes
            .Where(node => !TrainingProgramCoursesDefaults.IsDemoSeedId(node.Id))
            .ToList();
        if (withoutDemo.Count != nodes.Count)
        {
            Save(withoutDemo);
        }

        return withoutDemo;
    }

    public void Save(List<TrainingProgramCourseNode> nodes)
    {
        File.WriteAllText(_paths.ProgramsFile, JsonSerializer.Serialize(nodes, JsonOpts));
    }

    public TrainingProgramCourseNode? GetById(string id) =>
        Load().FirstOrDefault(node => node.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Удаляет курсы по наименованию (для очистки после удаления видов обучения).</summary>
    public int RemoveByNames(IEnumerable<string> programNames)
    {
        var names = programNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return 0;
        }

        var list = Load();
        var next = list
            .Where(node => node.IsGroup || !names.Contains(node.Name.Trim()))
            .ToList();
        if (next.Count == list.Count)
        {
            return 0;
        }

        Save(next);
        return list.Count - next.Count;
    }

    private List<TrainingProgramCourseNode> LoadRaw()
    {
        try
        {
            if (!File.Exists(_paths.ProgramsFile))
            {
                // Пустой справочник при первом запуске — программы добавляет пользователь.
                var empty = new List<TrainingProgramCourseNode>();
                Save(empty);
                return empty;
            }

            var json = File.ReadAllText(_paths.ProgramsFile);
            return JsonSerializer.Deserialize<List<TrainingProgramCourseNode>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

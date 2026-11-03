using System.Text.Json;

namespace Zashita.Core.Med;

public sealed class MedWorkTypeStore
{
    private readonly MedDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedWorkTypeStore(MedDataPaths paths) => _paths = paths;

    public List<MedWorkTypeNode> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.WorkTypesFile))
            {
                var defaults = CreateDefaults();
                SaveAll(defaults);
                return defaults;
            }

            return JsonSerializer.Deserialize<List<MedWorkTypeNode>>(File.ReadAllText(_paths.WorkTypesFile), JsonOpts)
                   ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public MedWorkTypeNode? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void SaveAll(IEnumerable<MedWorkTypeNode> nodes) =>
        File.WriteAllText(_paths.WorkTypesFile, JsonSerializer.Serialize(nodes.ToList(), JsonOpts));

    public void Upsert(MedWorkTypeNode node)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, node.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = node;
        else list.Add(node);
        SaveAll(list);
    }

    public void UpsertAll(IEnumerable<MedWorkTypeNode> nodes) => SaveAll(nodes);

    public string SuggestNextCode()
    {
        var max = LoadAll()
            .Select(n => int.TryParse(n.Code, out var code) ? code : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString();
    }

    private static List<MedWorkTypeNode> CreateDefaults()
    {
        var g1 = G("Акты-допуски");
        var g2 = G("Виды работ (Приказ Минтруда России от 29.10.2021 N 776н)");
        var g3 = G("Виды работ для хронометража");
        var g4 = G("Наряды-допуски");
        var g5 = G("ОКПБ");
        var g6 = G("Работы с повышенной опасностью");
        var g7 = G("Электрооборудование ОРУ");

        return
        [
            g1,
            g2,
            g3,
            g4,
            g5,
            g6,
            g7,
            W(g7.Id, "Архивирование документов"),
            W(g7.Id, "Входной контроль"),
            W(g7.Id, "Выполнение работ на высоте"),
            W(g7.Id, "Заточка инструмента"),
            W(g7.Id, "Малярные работы"),
            W(g7.Id, "Обработка деталей")
        ];
    }

    private static MedWorkTypeNode G(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        IsGroup = true
    };

    private static MedWorkTypeNode W(string parentId, string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ParentId = parentId,
        Name = name
    };
}

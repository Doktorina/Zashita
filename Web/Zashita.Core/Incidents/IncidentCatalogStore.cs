using System.Text.Json;

namespace Zashita.Core.Incidents;

public sealed class IncidentCatalogStore
{
    private readonly IncidentDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public IncidentCatalogStore(IncidentDataPaths paths) => _paths = paths;

    public List<IncidentCatalogItem> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.CatalogFile))
            {
                var seed = CreateDefaults();
                SaveAll(seed);
                return seed;
            }

            var json = File.ReadAllText(_paths.CatalogFile);
            var data = JsonSerializer.Deserialize<List<IncidentCatalogItem>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public List<IncidentCatalogItem> LoadByKind(IncidentCatalogKind kind) =>
        LoadAll()
            .Where(i => string.Equals(i.Kind, KindKey(kind), StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Code)
            .ToList();

    public void SaveAll(IEnumerable<IncidentCatalogItem> items) =>
        File.WriteAllText(_paths.CatalogFile, JsonSerializer.Serialize(items.ToList(), JsonOpts));

    public void Upsert(IncidentCatalogItem item)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, item.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = item;
        else list.Add(item);
        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    public static string KindKey(IncidentCatalogKind kind) => kind.ToString();

    public static string KindTitle(IncidentCatalogKind kind) => kind switch
    {
        IncidentCatalogKind.IncidentTypes => "Виды происшествий",
        IncidentCatalogKind.OccupationGroups => "Группы занятий",
        IncidentCatalogKind.InjuryTypes => "Классификатор видов травм",
        IncidentCatalogKind.BodyParts => "Классификатор частей тела",
        IncidentCatalogKind.AccidentConsequences => "Последствия несчастного случая",
        IncidentCatalogKind.IncidentCauses => "Причины происшествий",
        IncidentCatalogKind.IncidentNature => "Характер происшествий",
        _ => "Справочник"
    };

    private static List<IncidentCatalogItem> CreateDefaults()
    {
        var items = new List<IncidentCatalogItem>();
        Add(items, IncidentCatalogKind.IncidentTypes,
            ["Падение с высоты", "Поражение электрическим током", "Наезд транспортного средства"]);
        Add(items, IncidentCatalogKind.OccupationGroups, ["Руководители", "Специалисты", "Рабочие"]);
        Add(items, IncidentCatalogKind.InjuryTypes, ["Ушиб", "Перелом", "Рана", "Ожог"]);
        Add(items, IncidentCatalogKind.BodyParts, ["Голова", "Руки", "Туловище", "Ноги"]);
        Add(items, IncidentCatalogKind.AccidentConsequences, ["Легкие", "Тяжелые", "Смерть"]);
        Add(items, IncidentCatalogKind.IncidentCauses, ["Организационные", "Технические", "Личные"]);
        Add(items, IncidentCatalogKind.IncidentNature, ["Падение", "Защемление", "Поражение током", "ДТП"]);
        return items;
    }

    private static void Add(List<IncidentCatalogItem> items, IncidentCatalogKind kind, string[] names)
    {
        var key = KindKey(kind);
        for (var i = 0; i < names.Length; i++)
        {
            items.Add(new IncidentCatalogItem(Guid.NewGuid().ToString("N"), key, (i + 1).ToString("00"), names[i]));
        }
    }
}

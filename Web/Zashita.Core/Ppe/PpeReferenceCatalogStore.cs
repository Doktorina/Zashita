using System.Text.Json;

namespace Zashita.Core.Ppe;

public sealed record PpeReferenceCatalogItem(
    string Id,
    string Kind,
    string Code,
    string Name,
    string? Note = null);

public enum PpeReferenceCatalogKind
{
    NomenclatureTypes,
    ProtectiveProperties,
    PropertySets,
    Warehouses,
    EtnProfessions,
    EtnWorkTypes,
    ClimateZones,
    EtnHazards,
    TestPeriodicity,
    TestPlan,
    TestExecution
}

public static class PpeReferenceCatalogKinds
{
    public static string Key(PpeReferenceCatalogKind kind) => kind.ToString();

    public static PpeReferenceCatalogKind Parse(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return PpeReferenceCatalogKind.ProtectiveProperties;
        }

        return Enum.TryParse<PpeReferenceCatalogKind>(kind, true, out var parsed)
            ? parsed
            : PpeReferenceCatalogKind.ProtectiveProperties;
    }

    public static string Title(PpeReferenceCatalogKind kind) => kind switch
    {
        PpeReferenceCatalogKind.NomenclatureTypes => "Виды номенклатуры",
        PpeReferenceCatalogKind.ProtectiveProperties => "Защитные свойства СИЗ",
        PpeReferenceCatalogKind.PropertySets => "Наборы защитных свойств",
        PpeReferenceCatalogKind.Warehouses => "Склады (места хранения)",
        PpeReferenceCatalogKind.EtnProfessions => "Профессии ЕТН",
        PpeReferenceCatalogKind.EtnWorkTypes => "Виды работ ЕТН",
        PpeReferenceCatalogKind.ClimateZones => "Климатические пояса",
        PpeReferenceCatalogKind.EtnHazards => "Опасности ЕТН",
        PpeReferenceCatalogKind.TestPeriodicity => "Установка периодичности испытаний и проверок СИЗ",
        PpeReferenceCatalogKind.TestPlan => "План испытаний и проверок",
        PpeReferenceCatalogKind.TestExecution => "Испытание, проверка СИЗ",
        _ => "Справочник"
    };

    public static string? ResolveMenuTitle(string title)
    {
        var t = title.Trim();
        return t switch
        {
            "Защитные свойства СИЗ" => Key(PpeReferenceCatalogKind.ProtectiveProperties),
            "Наборы защитных свойств" => Key(PpeReferenceCatalogKind.PropertySets),
            "Склады (места хранения)" => Key(PpeReferenceCatalogKind.Warehouses),
            "Профессии ЕТН" => Key(PpeReferenceCatalogKind.EtnProfessions),
            "Виды работ ЕТН" => Key(PpeReferenceCatalogKind.EtnWorkTypes),
            "Климатические пояса" => Key(PpeReferenceCatalogKind.ClimateZones),
            "Опасности ЕТН" => Key(PpeReferenceCatalogKind.EtnHazards),
            "Установка периодичности испытаний и проверок СИЗ" => Key(PpeReferenceCatalogKind.TestPeriodicity),
            "План испытаний и проверок" => Key(PpeReferenceCatalogKind.TestPlan),
            "Испытание, проверка СИЗ" => Key(PpeReferenceCatalogKind.TestExecution),
            _ => null
        };
    }
}

public sealed class PpeReferenceCatalogStore
{
    private readonly PpeDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PpeReferenceCatalogStore(PpeDataPaths paths) => _paths = paths;

    public List<PpeReferenceCatalogItem> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.ReferenceCatalogFile))
            {
                var seed = CreateDefaults();
                SaveAll(seed);
                return seed;
            }

            var json = File.ReadAllText(_paths.ReferenceCatalogFile);
            var data = JsonSerializer.Deserialize<List<PpeReferenceCatalogItem>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public List<PpeReferenceCatalogItem> LoadByKind(PpeReferenceCatalogKind kind) =>
        LoadAll()
            .Where(i => string.Equals(i.Kind, PpeReferenceCatalogKinds.Key(kind), StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Code)
            .ThenBy(i => i.Name)
            .ToList();

    public void SaveAll(IEnumerable<PpeReferenceCatalogItem> items) =>
        File.WriteAllText(_paths.ReferenceCatalogFile, JsonSerializer.Serialize(items.ToList(), JsonOpts));

    public void Upsert(PpeReferenceCatalogItem item)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, item.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            list[idx] = item;
        }
        else
        {
            list.Add(item);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    private static List<PpeReferenceCatalogItem> CreateDefaults()
    {
        var items = new List<PpeReferenceCatalogItem>();
        Add(items, PpeReferenceCatalogKind.NomenclatureTypes,
            ["Спецодежда", "Спецобувь", "СИЗ рук", "СИЗ головы", "СИЗ органов дыхания"]);
        Add(items, PpeReferenceCatalogKind.ProtectiveProperties,
            ["От механических воздействий", "От пониженных температур", "От химических факторов", "От электрической дуги"]);
        Add(items, PpeReferenceCatalogKind.PropertySets,
            ["Комплект для сварщика", "Комплект для электромонтера", "Комплект для работ на высоте"]);
        Add(items, PpeReferenceCatalogKind.Warehouses,
            ["Склад СИЗ", "Склад спецодежды", "Дежурный склад"]);
        Add(items, PpeReferenceCatalogKind.EtnProfessions,
            ["Сварщик", "Электромонтер", "Слесарь", "Водитель"]);
        Add(items, PpeReferenceCatalogKind.EtnWorkTypes,
            ["Работы на высоте", "Работы с повышенной опасностью", "Работы в ограниченных пространствах"]);
        Add(items, PpeReferenceCatalogKind.ClimateZones,
            ["I", "II", "III", "IV (особый)"]);
        Add(items, PpeReferenceCatalogKind.EtnHazards,
            ["Механические опасности", "Химические опасности", "Термические опасности", "Электрические опасности"]);
        Add(items, PpeReferenceCatalogKind.TestPeriodicity,
            ["Страховочные системы — 1 раз в 6 мес.", "Противогазы — 1 раз в год", "Респираторы — 1 раз в год"]);
        Add(items, PpeReferenceCatalogKind.TestPlan,
            ["План на I квартал", "План на II квартал", "План на III квартал", "План на IV квартал"]);
        Add(items, PpeReferenceCatalogKind.TestExecution,
            ["Журнал испытаний", "Акт проверки СИЗ"]);
        return items;
    }

    private static void Add(List<PpeReferenceCatalogItem> items, PpeReferenceCatalogKind kind, IEnumerable<string> names)
    {
        var key = PpeReferenceCatalogKinds.Key(kind);
        var index = 1;
        foreach (var name in names)
        {
            items.Add(new PpeReferenceCatalogItem(
                Guid.NewGuid().ToString("N"),
                key,
                index.ToString("000"),
                name));
            index++;
        }
    }
}

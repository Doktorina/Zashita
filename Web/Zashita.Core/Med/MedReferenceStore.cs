using System.Text.Json;

namespace Zashita.Core.Med;

public sealed class MedReferenceStore
{
    private readonly MedDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedReferenceStore(MedDataPaths paths) => _paths = paths;

    public List<MedReferenceEntry> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.ReferencesFile))
            {
                return CreateDefaults();
            }

            var json = File.ReadAllText(_paths.ReferencesFile);
            var data = JsonSerializer.Deserialize<List<MedReferenceEntry>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public List<MedReferenceEntry> LoadByCategory(string category) =>
        LoadAll()
            .Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name)
            .ToList();

    public void SaveAll(IEnumerable<MedReferenceEntry> entries)
    {
        var data = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.Name))
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Name)
            .ToList();
        File.WriteAllText(_paths.ReferencesFile, JsonSerializer.Serialize(data, JsonOpts));
    }

    public void Upsert(MedReferenceEntry entry)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, entry.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = entry;
        else list.Add(entry);
        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    private static List<MedReferenceEntry> CreateDefaults() =>
    [
        new(Guid.NewGuid().ToString("N"), "factors", "Приказ 29н", "Базовый перечень факторов"),
        new(Guid.NewGuid().ToString("N"), "factors", "Работы на высоте", null),
        new(Guid.NewGuid().ToString("N"), "factors", "Физические факторы", null),
        new(Guid.NewGuid().ToString("N"), "shift-types", "Предсменный", "Перед началом смены"),
        new(Guid.NewGuid().ToString("N"), "shift-types", "Послесменный", "После окончания смены"),
        new(Guid.NewGuid().ToString("N"), "shift-legend", "Д", "Допуск"),
        new(Guid.NewGuid().ToString("N"), "shift-legend", "НД", "Нет допуска"),
        new(Guid.NewGuid().ToString("N"), "contraindications", "Беременность", null),
        new(Guid.NewGuid().ToString("N"), "contraindications", "Инвалидность I группы", null)
    ];
}

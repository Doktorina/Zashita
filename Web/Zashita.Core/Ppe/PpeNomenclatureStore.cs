using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zashita.Core.Ppe;

public sealed class PpeNomenclatureStore
{
    private readonly PpeDataPaths _paths;
    private readonly Appendix1NormsStore? _appendix1;
    private readonly EtnNomenclatureStore? _etn;
    private readonly PollutantsEtnStore? _pollutants;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PpeNomenclatureStore(
        PpeDataPaths paths,
        Appendix1NormsStore? appendix1 = null,
        EtnNomenclatureStore? etn = null,
        PollutantsEtnStore? pollutants = null)
    {
        _paths = paths;
        _appendix1 = appendix1;
        _etn = etn;
        _pollutants = pollutants;
    }

    public List<PpeNomenclatureItem> LoadForEditor(IReadOnlyList<IssueStandard>? standards = null)
    {
        var fromFile = LoadFromFile();
        // Приоритет: Номенклатура ЕТН + загрязнители (ДСИЗ) → сохранённый файл → Приложение 1 → демо.
        List<PpeNomenclatureItem> baseItems;
        var etnItems = _etn?.LoadAll();
        if (etnItems is { Count: > 0 })
        {
            baseItems = etnItems.Select(CloneItem).ToList();
        }
        else
        {
            var needsAppendix =
                fromFile.Count == 0 ||
                fromFile.Count < 20 ||
                fromFile.All(i => string.IsNullOrWhiteSpace(i.ProtectionType));

            if (needsAppendix && _appendix1 is { IsAvailable: true })
            {
                baseItems = _appendix1.GetNomenclatureItems().ToList();
                if (baseItems.Count > 0)
                {
                    SaveAll(baseItems);
                }
            }
            else if (fromFile.Count > 0)
            {
                baseItems = fromFile;
            }
            else
            {
                baseItems = BuildBaseline();
            }
        }

        baseItems = MergePollutants(baseItems);

        return standards is { Count: > 0 }
            ? MergeWithStandards(baseItems, standards)
            : EnrichItems(baseItems);
    }

    private List<PpeNomenclatureItem> MergePollutants(List<PpeNomenclatureItem> baseItems)
    {
        var pollutants = _pollutants?.LoadAll();
        if (pollutants is not { Count: > 0 })
        {
            return baseItems;
        }

        var byName = new Dictionary<string, PpeNomenclatureItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in baseItems)
        {
            var name = item.Name?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(name))
            {
                byName[name] = CloneItem(item);
            }
        }

        foreach (var item in pollutants)
        {
            var name = item.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            byName[name] = CloneItem(item);
        }

        return byName.Values
            .OrderBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.DisplayProtectionType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private List<PpeNomenclatureItem> EnrichItems(IEnumerable<PpeNomenclatureItem> items)
    {
        var lookup = BuildAppendixLookup();
        var list = items.Select(CloneItem).ToList();
        foreach (var item in list)
        {
            PpeProtectionTypeResolver.Enrich(item, lookup);
        }

        return list;
    }

    private Dictionary<string, Appendix1NormEntry> BuildAppendixLookup()
    {
        var lookup = new Dictionary<string, Appendix1NormEntry>(StringComparer.OrdinalIgnoreCase);
        if (_appendix1 is not { IsAvailable: true })
        {
            return lookup;
        }

        foreach (var entry in _appendix1.LoadAll())
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            lookup.TryAdd(entry.Name, entry);
        }

        return lookup;
    }

    private static PpeNomenclatureItem CloneItem(PpeNomenclatureItem source) => new()
    {
        Category = source.Category,
        ProtectionType = source.ProtectionType,
        Name = source.Name,
        Norm = source.Norm,
        NormParagraph = source.NormParagraph
    };

    public IReadOnlyList<string> GetProtectionTypes(IReadOnlyList<IssueStandard>? standards = null) =>
        LoadForEditor(standards)
            .Select(i => i.DisplayProtectionType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public List<PpeNomenclatureItem> MergeWithStandards(
        IEnumerable<PpeNomenclatureItem> baseItems,
        IEnumerable<IssueStandard> standards)
    {
        var lookup = BuildAppendixLookup();
        var byName = new Dictionary<string, PpeNomenclatureItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in EnrichItems(baseItems))
        {
            var name = item.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            byName[name] = item;
        }

        foreach (var standard in standards)
        {
            var category = ResolveItemCategory(standard.StandardCategory);
            foreach (var rawName in standard.Items)
            {
                var name = rawName?.Trim() ?? string.Empty;
                if (!IsValidNomenclatureName(name) || byName.ContainsKey(name))
                {
                    continue;
                }

                var resolvedCategory = category == "СИЗ+ДСИЗ" ? GuessCategory(name) : category;
                var protectionType = PpeProtectionTypeResolver.Resolve(name, resolvedCategory, lookup);
                byName[name] = new PpeNomenclatureItem
                {
                    Category = resolvedCategory,
                    ProtectionType = protectionType,
                    Name = name
                };
            }
        }

        return byName.Values
            .Select(item =>
            {
                var clone = CloneItem(item);
                PpeProtectionTypeResolver.Enrich(clone, lookup);
                return clone;
            })
            .OrderBy(item => item.DisplayProtectionType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ResolveItemCategory(string? category)
    {
        var normalized = (category ?? string.Empty).Trim().Replace(" ", "");
        return normalized switch
        {
            "ДСИЗ" => "ДСИЗ",
            "СИЗ+ДСИЗ" => "СИЗ+ДСИЗ",
            _ => "СИЗ"
        };
    }

    private static string GuessCategory(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("мыл") ||
            lower.Contains("крем") ||
            lower.Contains("очищ") ||
            lower.Contains("смыва") ||
            lower.Contains("дерматолог") ||
            lower.Contains("регенерир") ||
            lower.Contains("дозатор") ||
            lower.Contains("биологическ") ||
            lower.Contains("комбинированного"))
        {
            return "ДСИЗ";
        }

        return "СИЗ";
    }

    internal static bool IsValidNomenclatureName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var value = name.Trim();
        if (value.Length < 5)
        {
            return false;
        }

        string[] exactJunk =
        [
            "Одежда специальная защитная",
            "Защитного типа",
            "Очищающего типа",
            "Регенерирующего (восстанавливающего) типа"
        ];
        if (exactJunk.Any(junk => value.Equals(junk, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (value.StartsWith("Категория эффективности", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.StartsWith("Средства защиты ", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("орган", StringComparison.OrdinalIgnoreCase) &&
            value.Split(' ').Length <= 4)
        {
            // Заголовок раздела, не позиция.
            return false;
        }

        return true;
    }

    public List<PpeNomenclatureItem> LoadForEditor()
        => LoadForEditor(null);

    public List<PpeNomenclatureItem> EnrichForEditor(IEnumerable<PpeNomenclatureItem> items) =>
        EnrichItems(items);

    public void SaveAll(IEnumerable<PpeNomenclatureItem> items)
    {
        var root = new { items = items.ToList() };
        File.WriteAllText(_paths.NomenclatureFile, JsonSerializer.Serialize(root, JsonOpts));
    }

    /// <summary>Принудительно пересобрать номенклатуру из Приложения 1.</summary>
    public List<PpeNomenclatureItem> ImportFromAppendix1()
    {
        if (_appendix1 is not { IsAvailable: true })
        {
            return LoadForEditor();
        }

        var items = _appendix1.GetNomenclatureItems().ToList();
        SaveAll(items);
        return items;
    }

    private List<PpeNomenclatureItem> LoadFromFile()
    {
        try
        {
            if (!File.Exists(_paths.NomenclatureFile)) return [];
            var json = File.ReadAllText(_paths.NomenclatureFile);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var arr)) return [];
            return JsonSerializer.Deserialize<List<PpeNomenclatureItem>>(arr.GetRawText(), JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static List<PpeNomenclatureItem> BuildBaseline() =>
    [
        new() { Category = "СИЗ", ProtectionType = "Одежда специальная защитная", Name = "Костюм защитный" },
        new() { Category = "СИЗ", ProtectionType = "Одежда специальная защитная", Name = "Куртка" },
        new() { Category = "СИЗ", ProtectionType = "Средства защиты ног", Name = "Ботинки" },
        new() { Category = "СИЗ", ProtectionType = "Средства защиты рук", Name = "Перчатки" },
        new() { Category = "СИЗ", ProtectionType = "Средства защиты головы", Name = "Каска" },
        new() { Category = "ДСИЗ", ProtectionType = "Средства очищающего типа", Name = "Мыло" },
        new() { Category = "ДСИЗ", ProtectionType = "Средства защитного типа", Name = "Крем защитный" }
    ];
}

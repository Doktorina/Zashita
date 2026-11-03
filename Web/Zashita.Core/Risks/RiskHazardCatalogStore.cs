using System.Text.Json;

namespace Zashita.Core.Risks;

public sealed class RiskHazardCatalogStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public RiskHazardCatalogStore(RiskDataPaths paths) => _paths = paths;

    public List<RiskHazardCatalogItem> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.HazardCatalogFile))
            {
                var seeded = CreateDefaults();
                SaveAll(seeded);
                return seeded;
            }

            var json = File.ReadAllText(_paths.HazardCatalogFile);
            var doc = JsonSerializer.Deserialize<RiskHazardCatalogDocument>(json, JsonOpts);
            var items = doc?.Items ?? [];
            if (items.Count == 0)
            {
                items = CreateDefaults();
                SaveAll(items);
            }

            return RiskHazardNumberComparer.OrderByNumber(items);
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public List<RiskHazardCatalogItem> LoadActive() =>
        RiskHazardNumberComparer.OrderByNumber(
            LoadAll().Where(i => i.IsActive && !string.IsNullOrWhiteSpace(i.Title)));

    public void SaveAll(IEnumerable<RiskHazardCatalogItem> items)
    {
        var doc = new RiskHazardCatalogDocument
        {
            Items = RiskHazardNumberComparer.OrderByNumber(
                items
                    .Where(i => !string.IsNullOrWhiteSpace(i.Title))
                    .Select(i =>
                    {
                        if (string.IsNullOrWhiteSpace(i.Id))
                        {
                            i.Id = Guid.NewGuid().ToString("N");
                        }

                        i.Title = i.Title.Trim();
                        return i;
                    }))
        };
        File.WriteAllText(_paths.HazardCatalogFile, JsonSerializer.Serialize(doc, JsonOpts));
    }

    public static List<RiskHazardCatalogItem> CreateDefaults() =>
    [
        new()
        {
            Title = "Падение с высоты",
            DefaultWhoMaySuffer = "работник, работники рядом",
            DefaultMeasures = "Ограждения, СИЗ от падения, наряд-допуск",
            DefaultInitialRisk = "Высокий",
            DefaultResidualRisk = "Средний",
            ProfessionHint = "арматур"
        },
        new()
        {
            Title = "Поражение электрическим током (сварка)",
            DefaultWhoMaySuffer = "работник",
            DefaultMeasures = "СИЗ, заземление, допуск",
            DefaultInitialRisk = "Высокий",
            DefaultResidualRisk = "Средний",
            ProfessionHint = "арматур,сварщ,электрогаз"
        },
        new()
        {
            Title = "Падение груза",
            DefaultWhoMaySuffer = "работник, работники рядом",
            DefaultMeasures = "Стропы, сигнализация, зоны",
            DefaultInitialRisk = "Высокий",
            DefaultResidualRisk = "Средний",
            ProfessionHint = "стропаль"
        },
        new()
        {
            Title = "Воздействие пыли (силикоз)",
            DefaultWhoMaySuffer = "работник",
            DefaultMeasures = "Респиратор, увлажнение, вентиляция",
            DefaultInitialRisk = "Средний",
            DefaultResidualRisk = "Низкий",
            ProfessionHint = "каменщ"
        },
        new()
        {
            Title = "Поражение электрическим током",
            DefaultWhoMaySuffer = "работник",
            DefaultMeasures = "СИЗ, заземление",
            DefaultInitialRisk = "Высокий",
            DefaultResidualRisk = "Средний",
            ProfessionHint = "сварщ,электрогаз"
        },
        new()
        {
            Title = "Ожоги / УФ-излучение",
            DefaultWhoMaySuffer = "работник, работники рядом",
            DefaultMeasures = "Щиток, спецодежда, экраны",
            DefaultInitialRisk = "Средний",
            DefaultResidualRisk = "Низкий",
            ProfessionHint = "сварщ,электрогаз"
        },
        new()
        {
            Title = "ДТП / наезд",
            DefaultWhoMaySuffer = "работник, работники рядом, третьи лица",
            DefaultMeasures = "ПДД, осмотр ТС, инструктаж",
            DefaultInitialRisk = "Высокий",
            DefaultResidualRisk = "Средний",
            ProfessionHint = "водител,тракторист"
        },
        new()
        {
            Title = "Механические травмы",
            DefaultWhoMaySuffer = "работник",
            DefaultMeasures = "СИЗ, инструктаж, ограждения",
            DefaultInitialRisk = "Средний",
            DefaultResidualRisk = "Низкий"
        },
        new()
        {
            Title = "Падение на ровной поверхности",
            DefaultWhoMaySuffer = "работник",
            DefaultMeasures = "Уборка, нескользящая обувь",
            DefaultInitialRisk = "Средний",
            DefaultResidualRisk = "Низкий"
        }
    ];
}

using System.Text.Json;

namespace Zashita.Core.Risks;

public sealed class SoutImportStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SoutImportStore(RiskDataPaths paths) => _paths = paths;

    public SoutImportSettings Load(string organization)
    {
        var org = organization.Trim();
        try
        {
            if (!File.Exists(_paths.ImportSettingsFile))
            {
                return CreateDefault(org);
            }

            var json = File.ReadAllText(_paths.ImportSettingsFile);
            var all = JsonSerializer.Deserialize<List<SoutImportSettings>>(json, JsonOpts) ?? [];
            return all.FirstOrDefault(item => string.Equals(item.Organization, org, StringComparison.OrdinalIgnoreCase))
                   ?? CreateDefault(org);
        }
        catch
        {
            return CreateDefault(org);
        }
    }

    public void Save(string organization, SoutImportSettings settings)
    {
        var org = organization.Trim();
        settings.Organization = org;
        var all = new List<SoutImportSettings>();
        if (File.Exists(_paths.ImportSettingsFile))
        {
            var json = File.ReadAllText(_paths.ImportSettingsFile);
            all = JsonSerializer.Deserialize<List<SoutImportSettings>>(json, JsonOpts) ?? [];
            all.RemoveAll(item => string.Equals(item.Organization, org, StringComparison.OrdinalIgnoreCase));
        }

        all.Add(settings);
        File.WriteAllText(_paths.ImportSettingsFile, JsonSerializer.Serialize(all, JsonOpts));
    }

    public static SoutImportSettings CreateDefault(string organization) => new()
    {
        Organization = organization,
        AssessmentType = "Плановая",
        FirstDataRow = 2,
        CreateWorkplaces = true,
        MergeWorkplaceAnalogs = true,
        CreateAssessmentCard = true,
        CreateResultsDocument = true,
        ColumnMappings = DefaultColumnMappings()
    };

    public static List<SoutImportColumnMapping> DefaultColumnMappings() =>
    [
        new() { ColumnNumber = 6, AttributeName = "Шум" },
        new() { ColumnNumber = 7, AttributeName = "Инфразвук" },
        new() { ColumnNumber = 8, AttributeName = "УльтразвукВоздушный" },
        new() { ColumnNumber = 9, AttributeName = "ВибрацияОбщая" },
        new() { ColumnNumber = 10, AttributeName = "ВибрацияЛокальная" },
        new() { ColumnNumber = 11, AttributeName = "НеионизирующиеИзлучения" },
        new() { ColumnNumber = 12, AttributeName = "ИонизирующиеИзлучения" },
        new() { ColumnNumber = 13, AttributeName = "Микроклимат" },
        new() { ColumnNumber = 14, AttributeName = "СветоваяСреда" },
        new() { ColumnNumber = 15, AttributeName = "ТяжестьТруда" },
        new() { ColumnNumber = 16, AttributeName = "НапряженностьТруда" },
        new() { ColumnNumber = 17, AttributeName = "ОбщийКлассУсловийТруда" }
    ];
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zashita.Core.Compliance;

public sealed class InspectionActStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public InspectionActStore(ComplianceDataPaths paths) => _filePath = paths.InspectionActsFile;

    public List<InspectionActRecord> LoadAll() => LoadAllInternal();

    public List<InspectionActRecord> LoadByOrganization(string organization) =>
        LoadAllInternal()
            .Where(x => string.Equals(x.Organization, organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.ModifiedAt)
            .ToList();

    public InspectionActRecord? GetById(string id) =>
        LoadAllInternal().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void Upsert(InspectionActRecord record)
    {
        var now = DateTime.UtcNow;
        var list = LoadAllInternal();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        record.ModifiedAt = now;
        if (idx >= 0)
        {
            record.CreatedAt = list[idx].CreatedAt;
            list[idx] = record;
        }
        else
        {
            record.CreatedAt = now;
            list.Add(record);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(
            list.OrderByDescending(x => x.Date).ThenByDescending(x => x.ModifiedAt),
            JsonOpts));
    }

    public void Delete(string id)
    {
        var list = LoadAllInternal();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        File.WriteAllText(_filePath, JsonSerializer.Serialize(list, JsonOpts));
    }

    public string SuggestNextNumber(string organization)
    {
        var rows = LoadByOrganization(organization);
        var max = rows
            .Select(x => ParseNumberTail(x.Number))
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
    }

    private List<InspectionActRecord> LoadAllInternal()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                var seed = CreateSeed();
                File.WriteAllText(_filePath, JsonSerializer.Serialize(seed, JsonOpts));
                return seed;
            }

            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<List<InspectionActRecord>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateSeed();
        }
        catch
        {
            return CreateSeed();
        }
    }

    private static int ParseNumberTail(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return 0;
        }

        var digits = Regex.Replace(number, @"\D", string.Empty);
        return int.TryParse(digits, out var parsed) ? parsed : 0;
    }

    private static List<InspectionActRecord> CreateSeed() =>
    [
        new()
        {
            Id = "seed-inspection-act-003",
            IsPosted = true,
            Organization = "ООО «ВИКТОРИЯ СТРОЙ»",
            Number = "00003",
            Date = new DateTime(2026, 7, 24),
            ConstructionObject = "ЖК Орбита по адресу: г. Екатеринбург, ул. Монтерская, 10",
            Contractor = "ООО \"Отдел Строй\"",
            RepresentativeTitle = "Мастер СМР ООО 'Отдел Строй'",
            RepresentativeName = "Затопляев Е.И.",
            InspectorTitle = "Специалист службы охраны труда",
            InspectorName = "Чудинова А.",
            PhotoCount = 5,
            Violations =
            [
                new InspectionActViolationLine
                {
                    Description =
                        "1. Отсутствие ограждения на высотных перепадах 1,8 м и более.\n" +
                        "2. Работники не используют средства индивидуальной защиты (спецодежда, спецобувь, каски, очки, средства защиты органов дыхания).\n" +
                        "3. Открытое отверстие в перекрытии.\n" +
                        "4. Строительный мусор на рабочих местах.",
                    Regulations =
                        "Приказ Минтруда России от 29.10.2021 N 766н \"Об утверждении Правил по охране труда при работах на высоте\", " +
                        "ст. 214 Трудового кодекса Российской Федерации, Правила по охране труда при работах на высоте, " +
                        "Правила по охране труда в строительстве.",
                    Location = "ЖК Орбита",
                    Deadline = new DateTime(2026, 7, 27, 17, 38, 0)
                }
            ]
        }
    ];
}

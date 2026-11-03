using System.Text.Json;

namespace Zashita.Core.Med;

public sealed class MedMoSoutMappingStore
{
    private readonly MedDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedMoSoutMappingStore(MedDataPaths paths) => _paths = paths;

    public List<MedMoSoutMappingRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.MoSoutMappingsFile))
            {
                var defaults = CreateDefaults();
                SaveAll(defaults);
                return defaults;
            }

            return EnsureDescriptions(JsonSerializer.Deserialize<List<MedMoSoutMappingRecord>>(
                       File.ReadAllText(_paths.MoSoutMappingsFile), JsonOpts)
                   ?? CreateDefaults());
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public MedMoSoutMappingRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void SaveAll(IEnumerable<MedMoSoutMappingRecord> records) =>
        File.WriteAllText(_paths.MoSoutMappingsFile, JsonSerializer.Serialize(records.ToList(), JsonOpts));

    public void Upsert(MedMoSoutMappingRecord record)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = record;
        else list.Add(record);
        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    public int FillFromFactors(IEnumerable<MedFactorNode> factors)
    {
        var list = LoadAll();
        var existing = new HashSet<string>(
            list.Select(x => x.MoElementName),
            StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var factor in factors.Where(f => !f.IsGroup && !string.IsNullOrWhiteSpace(f.Name)))
        {
            if (!existing.Add(factor.Name))
            {
                continue;
            }

            list.Add(new MedMoSoutMappingRecord
            {
                MoElementName = factor.Name,
                MoElementDescription = string.IsNullOrWhiteSpace(factor.Description) ? factor.Name : factor.Description,
                OrderNumber = factor.OrderNumber,
                PointNumber = string.IsNullOrWhiteSpace(factor.PointNumber) ? factor.ItemNumber : factor.PointNumber,
                Periodicity = factor.Periodicity
            });
            added++;
        }

        if (added > 0)
        {
            SaveAll(list);
        }

        return added;
    }

    private List<MedMoSoutMappingRecord> EnsureDescriptions(List<MedMoSoutMappingRecord> list)
    {
        var changed = false;
        foreach (var record in list)
        {
            if (ApplyKnownDescriptions(record))
            {
                changed = true;
            }
        }

        if (changed)
        {
            SaveAll(list);
        }

        return list;
    }

    private static bool ApplyKnownDescriptions(MedMoSoutMappingRecord record)
    {
        if (record.MoElementName.Contains("Пониженная температура", StringComparison.OrdinalIgnoreCase)
            && !record.MoElementDescription.Contains("производственных помещениях", StringComparison.OrdinalIgnoreCase))
        {
            record.MoElementDescription =
                "Пониженная температура воздуха в производственных помещениях и на открытой территории " +
                "(при отнесении условий труда по данному фактору по результатам аттестации рабочих мест " +
                "по условиям труда к вредным условиям)";
            if (string.IsNullOrWhiteSpace(record.SoutElementDescription))
            {
                record.SoutElementDescription = "Температура воздуха";
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(record.MoElementDescription))
        {
            record.MoElementDescription = record.MoElementName;
            return true;
        }

        if (string.IsNullOrWhiteSpace(record.SoutElementDescription))
        {
            record.SoutElementDescription = record.SoutElementName;
            return true;
        }

        return false;
    }

    private static List<MedMoSoutMappingRecord> CreateDefaults() =>
    [
        new()
        {
            PointNumber = "Прил. 1 п. 3.8.",
            MoElementName = "Пониженная температура воздуха (воздействие холода)",
            MoElementDescription =
                "Пониженная температура воздуха в производственных помещениях и на открытой территории " +
                "(при отнесении условий труда по данному фактору по результатам аттестации рабочих мест " +
                "по условиям труда к вредным условиям)",
            Periodicity = "1 раз в 2 года",
            SoutElementType = "Вредный и (или) опасный производственный фактор",
            SoutElementName = "Температура воздуха",
            SoutElementDescription = "Температура воздуха"
        },
        new()
        {
            PointNumber = "п. 1.1",
            MoElementName = "Азота неорганические соединения (включая аммиак)",
            MoElementDescription = "Азота неорганические соединения (включая аммиак)",
            OrderNumber = "29н",
            Periodicity = "1 раз в год",
            SoutElementType = "Вредное вещество",
            SoutElementName = "Азотная кислота+",
            SoutElementDescription = "Азотная кислота+",
            AggregateState = "а",
            BodyEffect = "А"
        },
        new()
        {
            PointNumber = "п. 1.2",
            MoElementName = "Гексаметилентетрамин (уротропин)",
            MoElementDescription = "Гексаметилентетрамин (уротропин)",
            OrderNumber = "29н",
            Periodicity = "1 раз в год",
            SoutElementType = "Вредное вещество",
            SoutElementName = "Гексаметилентетрамин (уротропин)",
            SoutElementDescription = "Гексаметилентетрамин (уротропин)",
            AggregateState = "а"
        },
        new()
        {
            PointNumber = "п. 1.3",
            MoElementName = "Антибиотики группы цефалоспоринов",
            MoElementDescription = "Антибиотики группы цефалоспоринов",
            OrderNumber = "29н",
            Periodicity = "1 раз в год",
            SoutElementType = "Вредное вещество",
            SoutElementName = "Антибиотики группы цефалоспоринов",
            SoutElementDescription = "Антибиотики группы цефалоспоринов",
            AggregateState = "п"
        },
        new()
        {
            PointNumber = "п. 1.4",
            MoElementName = "1,1'-Бифенил-3-оксобутановая кислота",
            MoElementDescription = "1,1'-Бифенил-3-оксобутановая кислота",
            OrderNumber = "29н",
            Periodicity = "1 раз в год",
            SoutElementType = "Вредное вещество",
            SoutElementName = "Фенатрен",
            SoutElementDescription = "Фенатрен",
            AggregateState = "а",
            BodyEffect = "А"
        }
    ];
}

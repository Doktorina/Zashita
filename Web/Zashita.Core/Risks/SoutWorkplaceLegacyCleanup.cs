namespace Zashita.Core.Risks;

/// <summary>
/// Удаляет демо-перечень рабочих мест, который раньше подставлялся во все организации.
/// </summary>
public static class SoutWorkplaceLegacyCleanup
{
    private static readonly DateTime DemoBatchStartUtc = new(2026, 7, 10, 8, 46, 0, DateTimeKind.Utc);
    private static readonly DateTime DemoBatchEndUtc = new(2026, 7, 18, 6, 56, 0, DateTimeKind.Utc);

    private static readonly HashSet<string> DemoGroupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Администрация",
        "Бригада по обработке поверхностей и художественной росписи",
        "Бухгалтерия",
        "Диспетчерская",
        "Инженеры",
        "Лифтёры",
        "МТО",
        "Производство",
        "Разнорабочие",
        "Участки",
        "Участок №1",
        "Бригада ремонта Ижевск"
    };

    private static readonly HashSet<string> DemoWorkplaceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Директор",
        "Секретарь",
        "Маляр",
        "каменщик",
        "маляр",
        "маляр 6А",
        "плотник"
    };

    public static bool Purge(List<WorkplaceRecord> records)
    {
        if (records.Count == 0)
        {
            return false;
        }

        var removeIds = records
            .Where(IsLegacyDemoRecord)
            .Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (removeIds.Count == 0)
        {
            return false;
        }

        var before = records.Count;
        records.RemoveAll(r => removeIds.Contains(r.Id));

        // Пустые папки после удаления демо-строк.
        var changed = records.Count != before;
        while (RemoveEmptyLegacyGroups(records))
        {
            changed = true;
        }

        return changed;
    }

    private static bool RemoveEmptyLegacyGroups(List<WorkplaceRecord> records)
    {
        var childCounts = records
            .Where(r => !string.IsNullOrWhiteSpace(r.ParentId))
            .GroupBy(r => r.ParentId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var removed = records.RemoveAll(r =>
            r.IsGroup
            && DemoGroupNames.Contains(r.Name.Trim())
            && (!childCounts.TryGetValue(r.Id, out var count) || count == 0));

        return removed > 0;
    }

    private static bool IsLegacyDemoRecord(WorkplaceRecord record)
    {
        if (!IsDemoBatchTimestamp(record.CreatedAt))
        {
            return false;
        }

        var name = (record.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (record.IsGroup)
        {
            return DemoGroupNames.Contains(name);
        }

        if (DemoWorkplaceNames.Contains(name))
        {
            return true;
        }

        var profession = (record.Profession ?? string.Empty).Trim();
        return DemoWorkplaceNames.Contains(profession)
               && string.Equals(name, profession, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDemoBatchTimestamp(DateTime createdAt)
    {
        if (createdAt == default)
        {
            return false;
        }

        var utc = createdAt.Kind switch
        {
            DateTimeKind.Utc => createdAt,
            DateTimeKind.Local => createdAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
        };

        return utc >= DemoBatchStartUtc && utc <= DemoBatchEndUtc;
    }
}

namespace Zashita.Core.Risks;

/// <summary>
/// При импорте карт СОУТ: позиции из текста Word сверяются со справочниками
/// «Оборудование» / «Материалы и сырье», отсутствующие добавляются, все привязываются к РМ.
/// </summary>
public sealed class SoutCatalogImportBatch
{
    public const string ImportFolderName = "Импорт из карт СОУТ";

    private readonly SoutCatalogStore _store;
    private readonly string _organization;
    private readonly List<SoutCatalogRecord> _equipment;
    private readonly List<SoutCatalogRecord> _materials;
    private bool _equipmentChanged;
    private bool _materialsChanged;

    public int EquipmentAdded { get; private set; }
    public int MaterialsAdded { get; private set; }
    public int EquipmentLinked { get; private set; }
    public int MaterialsLinked { get; private set; }

    public SoutCatalogImportBatch(SoutCatalogStore store, string organization)
    {
        _store = store;
        _organization = organization?.Trim() ?? string.Empty;
        _equipment = store.LoadEquipment(_organization);
        _materials = store.LoadMaterials(_organization);
    }

    /// <summary>
    /// Сверяет текст оборудования/материалов со справочником, добавляет недостающее,
    /// привязывает к РМ и записывает отображаемые имена в карту.
    /// При mergePreserveExisting уже заполненные поля оборудования/материалов не затираются.
    /// </summary>
    public void ApplyToWorkplace(
        WorkplaceRecord workplace,
        string? equipmentText,
        string? materialsText,
        bool mergePreserveExisting = true)
    {
        workplace.SoutCard ??= new SoutCardDetails();

        var eqNames = SyncKind(
            _equipment,
            workplace.Id,
            equipmentText,
            ref _equipmentChanged,
            added => EquipmentAdded += added,
            linked => EquipmentLinked += linked);
        var matNames = SyncKind(
            _materials,
            workplace.Id,
            materialsText,
            ref _materialsChanged,
            added => MaterialsAdded += added,
            linked => MaterialsLinked += linked);

        if (eqNames.Count > 0
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.SoutCard.Equipment)))
        {
            workplace.SoutCard.Equipment = string.Join(Environment.NewLine, eqNames);
        }

        if (matNames.Count > 0
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.SoutCard.Materials)))
        {
            workplace.SoutCard.Materials = string.Join(Environment.NewLine, matNames);
        }
    }

    public void Save()
    {
        if (_equipmentChanged)
        {
            _store.SaveEquipment(_organization, _equipment);
        }

        if (_materialsChanged)
        {
            _store.SaveMaterials(_organization, _materials);
        }
    }

    public string SummaryMessage()
    {
        var parts = new List<string>();
        if (EquipmentAdded > 0)
        {
            parts.Add($"оборудование +{EquipmentAdded}");
        }

        if (MaterialsAdded > 0)
        {
            parts.Add($"материалы +{MaterialsAdded}");
        }

        if (EquipmentLinked > 0 || MaterialsLinked > 0)
        {
            parts.Add($"привязок к РМ: {EquipmentLinked + MaterialsLinked}");
        }

        return parts.Count == 0 ? string.Empty : "Справочники: " + string.Join(", ", parts) + ".";
    }

    private List<string> SyncKind(
        List<SoutCatalogRecord> catalog,
        string workplaceId,
        string? rawText,
        ref bool changedFlag,
        Action<int> onAdded,
        Action<int> onLinked)
    {
        var names = SplitNames(rawText);
        if (names.Count == 0)
        {
            return [];
        }

        var folderId = EnsureImportFolder(catalog, ref changedFlag);
        var display = new List<string>();
        var added = 0;
        var linked = 0;

        foreach (var name in names)
        {
            var item = FindByName(catalog, name);
            if (item is null)
            {
                item = new SoutCatalogRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Organization = SoutCatalogStore.SharedOrganizationKey,
                    ParentId = folderId,
                    IsGroup = false,
                    Code = _store.SuggestNextCode(catalog.Where(x => !x.IsGroup)),
                    Name = name,
                    FullName = name,
                    WorkplaceIds = [workplaceId],
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                };
                catalog.Add(item);
                changedFlag = true;
                added++;
                linked++;
            }
            else
            {
                item.WorkplaceIds ??= [];
                if (!item.WorkplaceIds.Any(id =>
                        string.Equals(id, workplaceId, StringComparison.OrdinalIgnoreCase)))
                {
                    item.WorkplaceIds.Add(workplaceId);
                    item.ModifiedAt = DateTime.UtcNow;
                    changedFlag = true;
                    linked++;
                }
            }

            display.Add(DisplayName(item));
        }

        if (added > 0)
        {
            onAdded(added);
        }

        if (linked > 0)
        {
            onLinked(linked);
        }

        return display
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string? EnsureImportFolder(List<SoutCatalogRecord> catalog, ref bool changedFlag)
    {
        var existing = catalog.FirstOrDefault(x =>
            x.IsGroup
            && string.Equals(x.Name?.Trim(), ImportFolderName, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(x.ParentId));
        if (existing is not null)
        {
            return existing.Id;
        }

        var id = Guid.NewGuid().ToString("N");
        catalog.Add(new SoutCatalogRecord
        {
            Id = id,
            Organization = SoutCatalogStore.SharedOrganizationKey,
            ParentId = null,
            IsGroup = true,
            Name = ImportFolderName,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        });
        changedFlag = true;
        return id;
    }

    private static SoutCatalogRecord? FindByName(IEnumerable<SoutCatalogRecord> catalog, string name)
    {
        var key = NormalizeKey(name);
        if (key.Length == 0)
        {
            return null;
        }

        return catalog
            .Where(x => !x.IsGroup)
            .FirstOrDefault(x =>
                string.Equals(NormalizeKey(x.Name), key, StringComparison.Ordinal) ||
                string.Equals(NormalizeKey(x.FullName), key, StringComparison.Ordinal) ||
                string.Equals(NormalizeKey(DisplayName(x)), key, StringComparison.Ordinal));
    }

    /// <summary>Разбивает текст Word на отдельные позиции справочника.</summary>
    public static List<string> SplitNames(string? rawText)
    {
        var text = (rawText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var lower = text.ToLowerInvariant().Replace('ё', 'е');
        if (lower is "отсутствует" or "отсутствуют" or "-" or "—" or "–" or "нет" or "не используется")
        {
            return [];
        }

        var parts = text
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitCommaAware)
            .Select(CleanName)
            .Where(n => n.Length > 0)
            .Where(n =>
            {
                var l = n.ToLowerInvariant().Replace('ё', 'е');
                return l is not ("отсутствует" or "отсутствуют" or "-" or "—" or "нет");
            })
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return parts;
    }

    /// <summary>
    /// Запятая — разделитель списка, но не режем короткие фрагменты внутри скобок.
    /// </summary>
    private static IEnumerable<string> SplitCommaAware(string part)
    {
        if (!part.Contains(','))
        {
            yield return part;
            yield break;
        }

        // Если запятых много и куски выглядят как отдельные наименования — режем.
        var chunks = part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (chunks.Length >= 2 && chunks.All(c => c.Length >= 3 && c.Length <= 120))
        {
            foreach (var c in chunks)
            {
                yield return c;
            }

            yield break;
        }

        yield return part;
    }

    private static string CleanName(string value)
    {
        var t = (value ?? string.Empty)
            .Replace('\u00a0', ' ')
            .Trim(' ', '.', ';', ':', '—', '-', '•', '·');
        while (t.Contains("  ", StringComparison.Ordinal))
        {
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        }

        return t;
    }

    private static string NormalizeKey(string? value) =>
        CleanName(value ?? string.Empty)
            .ToLowerInvariant()
            .Replace('ё', 'е');

    private static string DisplayName(SoutCatalogRecord item) =>
        !string.IsNullOrWhiteSpace(item.FullName) ? item.FullName.Trim()
        : !string.IsNullOrWhiteSpace(item.Name) ? item.Name.Trim()
        : item.Code?.Trim() ?? string.Empty;
}

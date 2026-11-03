using System.Text.Json;

namespace Zashita.Core.Med;

public sealed class MedFactorStore
{
    private readonly MedDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly object Sync = new();

    public static readonly (string ItemNumber, string Name)[] CanonicalGroups =
    [
        ("I", "I. Химические факторы"),
        ("II", "II. Биологические факторы"),
        ("IV", "IV. Физические факторы"),
        ("V", "V. Факторы трудового процесса"),
        ("VI", "VI. Выполняемые работы")
    ];

    public MedFactorStore(MedDataPaths paths) => _paths = paths;

    public List<MedFactorNode> LoadAll()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(_paths.FactorsFile))
                {
                    var defaults = CreateDefaults();
                    SaveAllLocked(defaults);
                    return defaults;
                }

                var list = JsonSerializer.Deserialize<List<MedFactorNode>>(
                    File.ReadAllText(_paths.FactorsFile), JsonOpts) ?? CreateDefaults();
                if (EnsureCanonicalGroups(list))
                {
                    SaveAllLocked(list);
                }

                return list;
            }
            catch
            {
                return CreateDefaults();
            }
        }
    }

    public void SaveAll(IEnumerable<MedFactorNode> nodes)
    {
        lock (Sync)
        {
            SaveAllLocked(nodes.ToList());
        }
    }

    public void UpsertAll(IEnumerable<MedFactorNode> nodes) => SaveAll(nodes);

    public MedFactorNode? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void Upsert(MedFactorNode node)
    {
        lock (Sync)
        {
            var list = LoadAllUnlocked();
            var idx = list.FindIndex(x => string.Equals(x.Id, node.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                list[idx] = node;
            }
            else
            {
                list.Add(node);
            }

            SaveAllLocked(list);
        }
    }

    public void Delete(string id)
    {
        lock (Sync)
        {
            var list = LoadAllUnlocked();
            var remove = new HashSet<string>(StringComparer.Ordinal) { id };
            CollectDescendants(id, list, remove);
            list.RemoveAll(n => remove.Contains(n.Id));
            SaveAllLocked(list);
        }
    }

    public string SuggestNextCode()
    {
        var max = LoadAll()
            .Select(n => int.TryParse(n.Code, out var code) ? code : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString();
    }

    /// <summary>
    /// Приводит справочник к группам приказа 29н: I, II, IV, V, VI.
    /// </summary>
    public static bool EnsureCanonicalGroups(List<MedFactorNode> list)
    {
        var changed = false;
        string[] obsoleteGroupNames =
        [
            "Приказ 302н",
            "Постановление 377",
            "Приказ 29н",
            "Аэрозоли преимущественно фиброгенного действия"
        ];

        var obsoleteIds = list
            .Where(n => n.IsGroup && obsoleteGroupNames.Any(o =>
                string.Equals(n.Name, o, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.ItemNumber, o.Replace("Приказ ", "").Replace("Постановление ", ""), StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        // Также убираем старые группы без римских цифр в названии, если совпадает короткое имя
        foreach (var node in list.Where(n => n.IsGroup).ToList())
        {
            foreach (var (itemNumber, fullName) in CanonicalGroups)
            {
                var shortName = fullName[(fullName.IndexOf('.') + 1)..].Trim();
                if (string.Equals(node.Name, shortName, StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(node.ItemNumber, itemNumber, StringComparison.OrdinalIgnoreCase) &&
                     !node.Name.StartsWith(itemNumber + ".", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.Equals(node.Name, fullName, StringComparison.Ordinal) ||
                        !string.Equals(node.ItemNumber, itemNumber, StringComparison.Ordinal) ||
                        !string.IsNullOrWhiteSpace(node.ParentId))
                    {
                        node.Name = fullName;
                        node.ItemNumber = itemNumber;
                        node.ParentId = null;
                        changed = true;
                    }
                }
            }
        }

        var canonicalByNumber = new Dictionary<string, MedFactorNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var (itemNumber, fullName) in CanonicalGroups)
        {
            var existing = list.FirstOrDefault(n =>
                n.IsGroup &&
                (string.Equals(n.ItemNumber, itemNumber, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(n.Name, fullName, StringComparison.OrdinalIgnoreCase)));
            if (existing is null)
            {
                existing = new MedFactorNode
                {
                    Id = Guid.NewGuid().ToString("N"),
                    IsGroup = true,
                    ItemNumber = itemNumber,
                    Name = fullName,
                    Periodicity = "Никогда",
                    IsActive = true,
                    ParentId = null
                };
                list.Add(existing);
                changed = true;
            }
            else
            {
                if (!string.Equals(existing.Name, fullName, StringComparison.Ordinal) ||
                    !string.Equals(existing.ItemNumber, itemNumber, StringComparison.Ordinal) ||
                    !string.IsNullOrWhiteSpace(existing.ParentId))
                {
                    existing.Name = fullName;
                    existing.ItemNumber = itemNumber;
                    existing.ParentId = null;
                    existing.IsGroup = true;
                    changed = true;
                }
            }

            canonicalByNumber[itemNumber] = existing;
            obsoleteIds.Remove(existing.Id);
        }

        if (obsoleteIds.Count > 0)
        {
            var fallbackParent = canonicalByNumber["I"].Id;
            foreach (var child in list.Where(n => !string.IsNullOrWhiteSpace(n.ParentId) && obsoleteIds.Contains(n.ParentId!)))
            {
                // Бывшие дети приказа 29н / аэрозолей — в подходящую каноническую группу
                var oldParent = list.FirstOrDefault(n => n.Id == child.ParentId);
                var mapped = MapToCanonicalParent(oldParent?.Name, oldParent?.ItemNumber, canonicalByNumber) ?? fallbackParent;
                child.ParentId = mapped;
                changed = true;
            }

            list.RemoveAll(n => obsoleteIds.Contains(n.Id));
            changed = true;
        }

        // Листья без группы и не «Без вредностей» — в VI
        foreach (var leaf in list.Where(n => !n.IsGroup && string.IsNullOrWhiteSpace(n.ParentId)))
        {
            if (leaf.Name.Contains("без вредност", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            leaf.ParentId = canonicalByNumber["VI"].Id;
            changed = true;
        }

        return changed;
    }

    private static string? MapToCanonicalParent(
        string? oldName,
        string? oldNumber,
        IReadOnlyDictionary<string, MedFactorNode> canonical)
    {
        var text = $"{oldNumber} {oldName}";
        if (text.Contains("хим", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(oldNumber, "I", StringComparison.OrdinalIgnoreCase))
        {
            return canonical["I"].Id;
        }

        if (text.Contains("биол", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(oldNumber, "II", StringComparison.OrdinalIgnoreCase))
        {
            return canonical["II"].Id;
        }

        if (text.Contains("физич", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(oldNumber, "IV", StringComparison.OrdinalIgnoreCase))
        {
            return canonical["IV"].Id;
        }

        if (text.Contains("трудов", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(oldNumber, "V", StringComparison.OrdinalIgnoreCase))
        {
            return canonical["V"].Id;
        }

        if (text.Contains("работ", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("аэрозол", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(oldNumber, "VI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(oldNumber, "III", StringComparison.OrdinalIgnoreCase))
        {
            return canonical["VI"].Id;
        }

        if (text.Contains("29н", StringComparison.OrdinalIgnoreCase))
        {
            return canonical["VI"].Id;
        }

        return null;
    }

    private List<MedFactorNode> LoadAllUnlocked()
    {
        if (!File.Exists(_paths.FactorsFile))
        {
            return CreateDefaults();
        }

        return JsonSerializer.Deserialize<List<MedFactorNode>>(
                   File.ReadAllText(_paths.FactorsFile), JsonOpts)
               ?? CreateDefaults();
    }

    private void SaveAllLocked(List<MedFactorNode> nodes) =>
        File.WriteAllText(_paths.FactorsFile, JsonSerializer.Serialize(nodes, JsonOpts));

    private static void CollectDescendants(string parentId, List<MedFactorNode> all, HashSet<string> collected)
    {
        foreach (var child in all.Where(n => string.Equals(n.ParentId, parentId, StringComparison.Ordinal)))
        {
            if (collected.Add(child.Id))
            {
                CollectDescendants(child.Id, all, collected);
            }
        }
    }

    private static List<MedFactorNode> CreateDefaults()
    {
        var groups = CanonicalGroups.ToDictionary(
            g => g.ItemNumber,
            g => new MedFactorNode
            {
                Id = Guid.NewGuid().ToString("N"),
                IsGroup = true,
                ItemNumber = g.ItemNumber,
                Name = g.Name,
                Periodicity = "Никогда",
                IsActive = true
            },
            StringComparer.OrdinalIgnoreCase);

        return
        [
            new MedFactorNode { Name = "Без вредностей", Periodicity = "Никогда", IsActive = true },
            ..groups.Values,
            new MedFactorNode
            {
                ParentId = groups["I"].Id,
                ItemNumber = "1.1",
                Name = "Азота неорганические соединения",
                Periodicity = "1 раз в год",
                IsActive = true
            },
            new MedFactorNode
            {
                ParentId = groups["IV"].Id,
                ItemNumber = "4.1",
                Name = "Шум",
                Periodicity = "1 раз в год",
                IsActive = true
            },
            new MedFactorNode
            {
                ParentId = groups["VI"].Id,
                ItemNumber = "27",
                Name = "Работы в медицинских организациях",
                Periodicity = "1 раз в год",
                IsActive = true
            },
            new MedFactorNode
            {
                ParentId = groups["VI"].Id,
                ItemNumber = "4.7",
                Name = "Строитель",
                Periodicity = "1 раз в год",
                IsActive = true
            }
        ];
    }
}

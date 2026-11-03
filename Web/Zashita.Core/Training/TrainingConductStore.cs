using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Training;

public sealed class TrainingConductStore
{
    private readonly TrainingDataPaths _paths;
    private readonly JsonFileListCache<TrainingConductRecord> _cache = new();
    // Без отступов — сохранение отчёта (тысячи записей) заметно быстрее.
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public TrainingConductStore(TrainingDataPaths paths) => _paths = paths;

    public List<TrainingConductRecord> LoadForReports() =>
        LoadAll().Where(r => !r.IsArchivedSnapshot).ToList();

    public List<TrainingConductRecord> LoadArchivedSnapshots() =>
        LoadAll().Where(r => r.IsArchivedSnapshot).ToList();

    public List<TrainingConductRecord> LoadAll() =>
        _cache.GetOrLoad(_paths.ConductFile, LoadFromDisk);

    private List<TrainingConductRecord> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_paths.ConductFile))
            {
                return CreateSeed();
            }

            var json = File.ReadAllText(_paths.ConductFile);
            var data = JsonSerializer.Deserialize<List<TrainingConductRecord>>(json, JsonOpts);
            return data is { Count: > 0 } ? data : CreateSeed();
        }
        catch
        {
            return CreateSeed();
        }
    }

    public void SaveAll(IEnumerable<TrainingConductRecord> records)
    {
        var data = records
            .Where(r => !string.IsNullOrWhiteSpace(r.Person) && !string.IsNullOrWhiteSpace(r.Program))
            .OrderByDescending(r => r.TrainingDate ?? DateTime.MinValue)
            .ThenBy(r => r.Person, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        File.WriteAllText(_paths.ConductFile, JsonSerializer.Serialize(data, JsonOpts));
        _cache.Replace(data, _paths.ConductFile);
    }

    public void Upsert(TrainingConductRecord record)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0)
        {
            list[idx] = record;
        }
        else
        {
            list.Add(record);
        }

        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    /// <summary>Удаляет записи проведения/отчёта по именам программ (видов обучения).</summary>
    public int DeleteByPrograms(IEnumerable<string> programNames)
    {
        var names = programNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return 0;
        }

        var list = LoadAll();
        var before = list.Count;
        list.RemoveAll(r => names.Contains((r.Program ?? string.Empty).Trim()));
        var removed = before - list.Count;
        if (removed > 0)
        {
            SaveAll(list);
        }

        return removed;
    }

    public void RenameOrganization(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        var from = oldName.Trim();
        var to = newName.Trim();
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var list = LoadAll();
        var changed = false;
        foreach (var record in list)
        {
            if (string.Equals(record.Organization, from, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(from, record.Organization))
            {
                record.Organization = to;
                // Подразделение = старое имя юрлица — убираем, иначе в отчёте вторая ветка.
                if (string.Equals((record.Department ?? string.Empty).Trim(), from, StringComparison.OrdinalIgnoreCase)
                    || OrganizationMatcher.Matches(from, record.Department)
                    || string.Equals((record.Department ?? string.Empty).Trim(), to, StringComparison.OrdinalIgnoreCase))
                {
                    record.Department = string.Empty;
                }

                changed = true;
            }
        }

        if (changed)
        {
            SaveAll(list);
        }
    }

    /// <summary>
    /// Organization = «Строительство» (подразделение) вместо юрлица — чиним по каноническому имени.
    /// Только записи, где ложное имя уже встречается как подразделение этой компании
    /// или человек уже есть среди проведений этой компании.
    /// </summary>
    public int RepairMisfiledOrganizations(string canonicalOrganization)
    {
        var to = (canonicalOrganization ?? string.Empty).Trim();
        if (to.Length == 0 || !OrganizationMatcher.LooksLikeOrganization(to))
        {
            return 0;
        }

        var list = LoadAll();
        var underCanonical = list
            .Where(r => string.Equals((r.Organization ?? string.Empty).Trim(), to, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcher.Matches(to, r.Organization))
            .ToList();
        if (underCanonical.Count == 0)
        {
            return 0;
        }

        var knownDepartments = underCanonical
            .Select(r => (r.Department ?? string.Empty).Trim())
            .Where(d => d.Length > 0 && d != "—")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownPeople = underCanonical
            .Select(r => (r.Person ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = 0;
        foreach (var record in list)
        {
            var org = (record.Organization ?? string.Empty).Trim();
            if (org.Length == 0)
            {
                continue;
            }

            if (string.Equals(org, to, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(to, org))
            {
                continue;
            }

            // Уже нормальное другое юрлицо — не трогаем.
            if (OrganizationMatcher.LooksLikeOrganization(org))
            {
                continue;
            }

            var person = (record.Person ?? string.Empty).Trim();
            if (!knownDepartments.Contains(org) && !knownPeople.Contains(person))
            {
                continue;
            }

            // Ложная «организация» = подразделение — переносим под канон.
            var dept = (record.Department ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dept) || dept == "—")
            {
                record.Department = org;
            }

            record.Organization = to;
            changed++;
        }

        if (changed > 0)
        {
            SaveAll(list);
        }

        return changed;
    }

    private static List<TrainingConductRecord> CreateSeed()
    {
        var today = DateTime.Today;
        return
        [
            new()
            {
                Organization = "ИП Алешкевич А.Д.",
                Department = "Администрация",
                Profession = "Руководитель",
                Person = "Иванов И.И.",
                Program = "Вводный противопожарный инструктаж",
                TrainingType = "Инструктаж",
                Result = "Сдал",
                TrainingDate = today.AddMonths(-2),
                NextTrainingDate = today.AddYears(3),
                ProtocolNumber = "ПБ-В-001",
                CertificateNumber = "18452"
            },
            new()
            {
                Organization = "ИП Алешкевич А.Д.",
                Department = "Производство",
                Profession = "Слесарь",
                Person = "Петров П.П.",
                Program = "Противопожарный инструктаж на рабочем месте",
                TrainingType = "Инструктаж",
                Result = "Сдал",
                TrainingDate = today.AddMonths(-5),
                NextTrainingDate = today.AddMonths(7),
                ProtocolNumber = "ПБ-РМ-014",
                CertificateNumber = "29107"
            },
            new()
            {
                Organization = "ИП Алешкевич А.Д.",
                Department = "Производство",
                Profession = "Электромонтер",
                Person = "Сидоров С.С.",
                Program = "Обучение мерам пожарной безопасности",
                TrainingType = "Обучение",
                Result = "Сдал",
                TrainingDate = today.AddMonths(-8),
                NextTrainingDate = today.AddMonths(4),
                ProtocolNumber = "ПБ-О-008",
                CertificateNumber = "33761"
            }
        ];
    }
}

using System.Text.Json;

namespace Zashita.Core.Med;

public sealed class MedExamStore
{
    private readonly MedDataPaths _paths;
    private readonly MedJsonListCache<MedExamRecord> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedExamStore(MedDataPaths paths) => _paths = paths;

    public List<MedExamRecord> LoadAll() =>
        _cache.GetOrLoad(_paths.ExamsFile, LoadFromDisk);

    private List<MedExamRecord> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_paths.ExamsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.ExamsFile);
            var list = JsonSerializer.Deserialize<List<MedExamRecord>>(json, JsonOpts) ?? [];
            foreach (var row in list)
            {
                row.Periodicity = MedPeriodicity.Normalize(row.Periodicity);
                if (row.NextExamDate is null)
                {
                    row.RecalculateNextExamDate();
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public List<MedExamRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(r => MedOrganizationFilter.Matches(organization, r.Organization))
            .ToList();

    /// <summary>
    /// Переименовать «ИП Алешкевич А.Д.» → каноническое имя из списка организаций.
    /// </summary>
    public int AlignOrganizationNames(IEnumerable<string> canonicalOrganizations)
    {
        var list = LoadAll();
        var changed = MedOrganizationFilter.AlignOrganizations(
            list,
            record => record.Organization,
            (record, name) => record.Organization = name,
            canonicalOrganizations);
        if (changed > 0)
        {
            SaveAll(list);
        }

        return changed;
    }

    public void SaveAll(IEnumerable<MedExamRecord> records)
    {
        var data = records
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .Select(r =>
            {
                r.Periodicity = MedPeriodicity.Normalize(r.Periodicity);
                if (r.NextExamDate is null)
                {
                    r.RecalculateNextExamDate();
                }

                return r;
            })
            .OrderByDescending(r => r.ExamDate)
            .ToList();
        File.WriteAllText(_paths.ExamsFile, JsonSerializer.Serialize(data, JsonOpts));
        _cache.Replace(data, _paths.ExamsFile);
    }

    public void Upsert(MedExamRecord record)
    {
        record.Periodicity = MedPeriodicity.Normalize(record.Periodicity);
        record.RecalculateNextExamDate();
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = record;
        else list.Add(record);
        SaveAll(list);
    }

    public void UpsertMany(IEnumerable<MedExamRecord> records)
    {
        var list = LoadAll();
        var byId = list.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Id))
            {
                record.Id = Guid.NewGuid().ToString("N");
            }

            record.ExamType = MedStandardAssignmentBuilder.NormalizeExamType(record.ExamType);
            record.ItemNumber = MedItemNumber.Normalize(record.ItemNumber);
            record.Periodicity = MedPeriodicity.Normalize(record.Periodicity);
            if (record.NextExamDate is null)
            {
                record.RecalculateNextExamDate();
            }

            byId[record.Id] = record;
        }

        var collapsed = MedStandardAssignmentBuilder.CollapseDuplicateExams(byId.Values);
        SaveAll(collapsed);
    }

    /// <summary>Пересохраняет exams.json без дублей пунктов у одного человека.</summary>
    public int PurgeDuplicateExams()
    {
        var before = LoadAll();
        var after = MedStandardAssignmentBuilder.CollapseDuplicateExams(before);
        if (after.Count == before.Count)
        {
            return 0;
        }

        SaveAll(after);
        return before.Count - after.Count;
    }

    public void DeleteMany(IEnumerable<string> ids)
    {
        var remove = new HashSet<string>(
            ids.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        if (remove.Count == 0)
        {
            return;
        }

        var list = LoadAll();
        var before = list.Count;
        list.RemoveAll(x => remove.Contains(x.Id));
        if (list.Count != before)
        {
            SaveAll(list);
        }
    }

    public void Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        DeleteMany([id]);
    }

    public void EnsureSeeded(string organization, IReadOnlyDictionary<string, List<string>> employeesByProfession)
    {
        if (LoadAll().Count > 0)
        {
            return;
        }

        // Не автозаполняем из 1С/Excel — для новых сотрудников ФИО вводят вручную.
        SaveAll([]);
    }
}

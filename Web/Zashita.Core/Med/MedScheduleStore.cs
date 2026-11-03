using System.Text.Json;
using Zashita.Core.Incidents;

namespace Zashita.Core.Med;

public sealed class MedScheduleStore
{
    private readonly MedDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedScheduleStore(MedDataPaths paths) => _paths = paths;

    public List<MedScheduleDocument> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.SchedulesFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.SchedulesFile);
            var list = JsonSerializer.Deserialize<List<MedScheduleDocument>>(json, JsonOpts) ?? [];
            foreach (var record in list)
            {
                record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public MedScheduleDocument? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public List<MedScheduleDocument> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(r => MedOrganizationFilter.Matches(organization, r.Organization))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

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

    public void SaveAll(IEnumerable<MedScheduleDocument> records)
    {
        var data = records.Where(r => !string.IsNullOrWhiteSpace(r.Id)).ToList();
        File.WriteAllText(_paths.SchedulesFile, JsonSerializer.Serialize(data, JsonOpts));
    }

    public void Upsert(MedScheduleDocument record)
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

    public string SuggestNextNumber() =>
        IncidentNumberHelper.NextNumber("0000-", LoadAll().Select(r => r.Number), pad: 6);

    public void EnsureSeeded(string organization)
    {
        if (LoadByOrganization(organization).Count > 0)
        {
            return;
        }

        SaveAll(
        [
            new MedScheduleDocument
            {
                Date = new DateTime(2013, 8, 4), Number = "0000-000001", ExamType = "Периодический",
                Organization = organization, MedicalInstitution = "Гор. больница", IsPosted = true
            },
            new MedScheduleDocument
            {
                Date = new DateTime(2014, 2, 17), Number = "ОТ-00000001", ExamType = "Углубленный",
                Organization = organization, MedicalInstitution = "ООО \"Пятницкая Мед\"", IsPosted = true
            },
            new MedScheduleDocument
            {
                Date = new DateTime(2015, 3, 11), Number = "ОТ-00000002", ExamType = "Психиатрический",
                Organization = organization, MedicalInstitution = "Гор. больница", IsPosted = true
            },
            new MedScheduleDocument
            {
                Date = new DateTime(2016, 1, 30), Number = "ОТ-00000003", ExamType = "Предварительный",
                Organization = organization, MedicalInstitution = "Медцентр", IsPosted = true
            }
        ]);
    }
}

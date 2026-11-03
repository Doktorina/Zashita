using System.Text.Json;
using Zashita.Core.Incidents;

namespace Zashita.Core.Med;

public sealed class MedRestrictionStore
{
    private readonly MedDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedRestrictionStore(MedDataPaths paths) => _paths = paths;

    public List<MedRestrictionDocument> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.RestrictionsFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.RestrictionsFile);
            var list = JsonSerializer.Deserialize<List<MedRestrictionDocument>>(json, JsonOpts) ?? [];
            foreach (var record in list)
            {
                record.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                record.Lines ??= [];
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public MedRestrictionDocument? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public List<MedRestrictionDocument> LoadByOrganization(string organization) =>
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

    public void SaveAll(IEnumerable<MedRestrictionDocument> records)
    {
        var data = records
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Number, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        File.WriteAllText(_paths.RestrictionsFile, JsonSerializer.Serialize(data, JsonOpts));
    }

    public void Upsert(MedRestrictionDocument record)
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

    public string SuggestNextNumber() =>
        IncidentNumberHelper.NextNumber("ОТ-", LoadAll().Select(r => r.Number), pad: 8);

    public void EnsureSeeded(string organization)
    {
        if (LoadByOrganization(organization).Count > 0)
        {
            return;
        }

        SaveAll(
        [
            new MedRestrictionDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                Date = new DateTime(2024, 3, 4, 10, 0, 0),
                Number = "ОТ-00000008",
                EmployeeName = "Банке Элла Викторовна",
                Organization = organization,
                MedicalInstitution = "Городская поликлиника №1",
                Basis = "Заключение врачебной комиссии",
                IsPosted = true,
                Lines =
                [
                    new MedRestrictionLine
                    {
                        FactorOrWorkType = "Работы на высоте",
                        ItemNumber = "23.1",
                        IsPresent = true,
                        ReviewDate = new DateTime(2025, 3, 4)
                    }
                ]
            },
            new MedRestrictionDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                Date = new DateTime(2023, 11, 15, 9, 0, 0),
                Number = "МСОТ-000001",
                EmployeeName = "Петров Иван Иванович",
                Organization = organization,
                MedicalInstitution = "Медцентр",
                Basis = "Медицинское заключение",
                IsPosted = true,
                Lines =
                [
                    new MedRestrictionLine
                    {
                        FactorOrWorkType = "Подъём тяжестей",
                        ItemNumber = "12.4",
                        IsPresent = true,
                        ReviewDate = new DateTime(2024, 11, 15)
                    }
                ]
            }
        ]);
    }
}

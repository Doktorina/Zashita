using System.Text.Json;

namespace Zashita.Core.Med;

public sealed class MedicalOrganizationStore
{
    private readonly MedDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedicalOrganizationStore(MedDataPaths paths) => _paths = paths;

    private void EnsureStorage()
    {
        var dir = Path.GetDirectoryName(_paths.MedicalOrganizationsFile);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (!File.Exists(_paths.MedicalOrganizationsFile))
        {
            File.WriteAllText(_paths.MedicalOrganizationsFile, "[]");
        }
    }

    public List<MedicalOrganizationRecord> LoadAll()
    {
        try
        {
            EnsureStorage();
            if (!File.Exists(_paths.MedicalOrganizationsFile))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<MedicalOrganizationRecord>>(
                File.ReadAllText(_paths.MedicalOrganizationsFile), JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public List<MedicalOrganizationRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(x => x.IsDefault ? 1 : 0)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public MedicalOrganizationRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public MedicalOrganizationRecord? GetDefault(string organization)
    {
        var list = LoadByOrganization(organization);
        return list.FirstOrDefault(x => x.IsDefault) ?? list.FirstOrDefault();
    }

    public void Upsert(MedicalOrganizationRecord record)
    {
        EnsureStorage();
        var list = LoadAll();
        if (record.IsDefault)
        {
            foreach (var item in list)
            {
                item.IsDefault = false;
            }
        }

        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = record;
        else list.Add(record);
        File.WriteAllText(_paths.MedicalOrganizationsFile, JsonSerializer.Serialize(list, JsonOpts));
    }

    public void Delete(string id)
    {
        EnsureStorage();
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        File.WriteAllText(_paths.MedicalOrganizationsFile, JsonSerializer.Serialize(list, JsonOpts));
    }
}

using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public sealed class MedStandardStore
{
    private readonly MedDataPaths _paths;
    private readonly MedJsonListCache<MedStandardRecord> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public MedStandardStore(MedDataPaths paths) => _paths = paths;

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_paths.StandardsFile);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private string BackupFile => _paths.StandardsFile + ".bak";

    public List<MedStandardRecord> LoadAll() =>
        _cache.GetOrLoad(_paths.StandardsFile, LoadFromDisk);

    private List<MedStandardRecord> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_paths.StandardsFile))
            {
                var fromBak = TryLoadBackup();
                if (fromBak is { Count: > 0 })
                {
                    WriteAtomic(fromBak, refreshCache: false);
                    return fromBak;
                }

                return [];
            }

            var text = File.ReadAllText(_paths.StandardsFile);
            if (string.IsNullOrWhiteSpace(text))
            {
                return TryLoadBackup() ?? [];
            }

            var list = JsonSerializer.Deserialize<List<MedStandardRecord>>(text, JsonOpts);
            if (list is null)
            {
                return TryLoadBackup() ?? [];
            }

            foreach (var item in list)
            {
                item.Rules ??= [];
            }

            return list;
        }
        catch
        {
            return TryLoadBackup() ?? [];
        }
    }

    public List<MedStandardRecord> LoadByOrganization(string organization) =>
        LoadAll()
            .Where(x =>
            {
                // Пустая организация в стандарте — устаревшие записи (сохраняли при «Все организации»).
                // Показываем их в любой выбранной организации, чтобы можно было пересохранить.
                if (string.IsNullOrWhiteSpace(x.Organization))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(organization))
                {
                    return true;
                }

                return string.Equals(x.Organization, organization, StringComparison.OrdinalIgnoreCase) ||
                       OrganizationMatcher.Matches(organization, x.Organization) ||
                       OrganizationMatcher.Matches(x.Organization, organization);
            })
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Profession, StringComparer.CurrentCultureIgnoreCase)
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
            WriteAtomic(list);
        }

        return changed;
    }

    public MedStandardRecord? GetById(string id) =>
        LoadAll().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    public void Upsert(MedStandardRecord record)
    {
        record.Rules ??= [];
        EnsureDirectory();
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = record;
        else list.Add(record);
        WriteAtomic(list);
    }

    public void Delete(string id)
    {
        EnsureDirectory();
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        WriteAtomic(list);
    }

    private List<MedStandardRecord>? TryLoadBackup()
    {
        try
        {
            if (!File.Exists(BackupFile))
            {
                return null;
            }

            var text = File.ReadAllText(BackupFile);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var list = JsonSerializer.Deserialize<List<MedStandardRecord>>(text, JsonOpts);
            if (list is null || list.Count == 0)
            {
                return null;
            }

            foreach (var item in list)
            {
                item.Rules ??= [];
            }

            return list;
        }
        catch
        {
            return null;
        }
    }

    private void WriteAtomic(List<MedStandardRecord> list, bool refreshCache = true)
    {
        EnsureDirectory();

        // Бэкап текущего непустого файла перед перезаписью.
        try
        {
            if (File.Exists(_paths.StandardsFile))
            {
                var current = File.ReadAllText(_paths.StandardsFile);
                if (!string.IsNullOrWhiteSpace(current) &&
                    current.Trim() is not "[]" and not "null")
                {
                    File.Copy(_paths.StandardsFile, BackupFile, overwrite: true);
                }
            }
        }
        catch
        {
            // бэкап неблокирующий
        }

        var json = JsonSerializer.Serialize(list, JsonOpts);
        var temp = _paths.StandardsFile + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, _paths.StandardsFile, overwrite: true);
        try
        {
            File.Delete(temp);
        }
        catch
        {
            // ignore
        }

        if (list.Count > 0)
        {
            try
            {
                File.Copy(_paths.StandardsFile, BackupFile, overwrite: true);
            }
            catch
            {
                // ignore
            }
        }

        if (refreshCache)
        {
            _cache.Replace(list, _paths.StandardsFile);
        }
    }
}

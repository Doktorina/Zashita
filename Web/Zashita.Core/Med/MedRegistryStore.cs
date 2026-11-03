using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public sealed class MedRegistryStore
{
    private readonly MedDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedRegistryStore(MedDataPaths paths) => _paths = paths;

    public List<MedRegistryRow> LoadAll()
    {
        try
        {
            if (!File.Exists(_paths.RegistryFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.RegistryFile);
            return JsonSerializer.Deserialize<List<MedRegistryRow>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public List<MedRegistryRow> LoadByKind(string kind, string organization)
    {
        var normalizedKind = NormalizeKind(kind);
        return LoadAll()
            .Where(r => string.Equals(r.Kind, normalizedKind, StringComparison.OrdinalIgnoreCase))
            .Where(r => MedOrganizationFilter.Matches(organization, r.Organization))
            .OrderByDescending(r => r.Date)
            .ToList();
    }

    public int AlignOrganizationNames(IEnumerable<string> canonicalOrganizations)
    {
        var canonical = canonicalOrganizations
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        if (canonical.Count == 0)
        {
            return 0;
        }

        var list = LoadAll();
        var changed = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var row = list[i];
            var org = (row.Organization ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(org))
            {
                continue;
            }

            var resolved = OrganizationMatcher.ResolveOrganizationName(org, canonical);
            if (resolved is null
                || string.Equals(resolved, org, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            list[i] = row with { Organization = resolved };
            changed++;
        }

        if (changed > 0)
        {
            SaveAll(list);
        }

        return changed;
    }

    public void SaveAll(IEnumerable<MedRegistryRow> rows)
    {
        var data = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .OrderByDescending(r => r.Date)
            .ToList();
        File.WriteAllText(_paths.RegistryFile, JsonSerializer.Serialize(data, JsonOpts));
    }

    public void Upsert(MedRegistryRow row)
    {
        var list = LoadAll();
        var idx = list.FindIndex(x => string.Equals(x.Id, row.Id, StringComparison.Ordinal));
        if (idx >= 0) list[idx] = row;
        else list.Add(row);
        SaveAll(list);
    }

    public void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        SaveAll(list);
    }

    public static string NormalizeKind(string kind) =>
        string.IsNullOrWhiteSpace(kind) ? "general" : kind.Trim().ToLowerInvariant();
}

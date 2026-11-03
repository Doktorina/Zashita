using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Med;

public sealed class MedPhysicalRestrictionNotesStore
{
    private readonly MedDataPaths _paths;
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public MedPhysicalRestrictionNotesStore(MedDataPaths paths) => _paths = paths;

    public IReadOnlyDictionary<string, string> LoadMap(string organization)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        lock (_sync)
        {
            var data = LoadUnlocked();
            if (!data.ByOrganization.TryGetValue(org, out var employees) || employees is null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return employees.ToDictionary(
                pair => pair.Key,
                pair => pair.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveOrganization(string organization, IDictionary<string, string> notesByEmployee)
    {
        var org = organization.Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadUnlocked();
            data.ByOrganization[org] = notesByEmployee
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
            PersistUnlocked(data);
        }
    }

    public void Upsert(string organization, string employeeName, string workRestriction)
    {
        var org = organization.Trim();
        var employee = employeeName.Trim();
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(employee))
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadUnlocked();
            if (!data.ByOrganization.TryGetValue(org, out var employees) || employees is null)
            {
                employees = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                data.ByOrganization[org] = employees;
            }

            employees[employee] = workRestriction?.Trim() ?? string.Empty;
            PersistUnlocked(data);
        }
    }

    public void RenameOrganization(string oldName, string newName)
    {
        var oldOrg = oldName.Trim();
        var newOrg = newName.Trim();
        if (string.IsNullOrWhiteSpace(oldOrg) ||
            string.IsNullOrWhiteSpace(newOrg) ||
            string.Equals(oldOrg, newOrg, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_sync)
        {
            var data = LoadUnlocked();
            if (!data.ByOrganization.TryGetValue(oldOrg, out var notes) || notes is null || notes.Count == 0)
            {
                return;
            }

            data.ByOrganization.Remove(oldOrg);
            if (data.ByOrganization.TryGetValue(newOrg, out var existing) && existing is not null)
            {
                foreach (var pair in notes)
                {
                    existing[pair.Key] = pair.Value;
                }
            }
            else
            {
                data.ByOrganization[newOrg] = notes;
            }

            PersistUnlocked(data);
        }
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

        lock (_sync)
        {
            var data = LoadUnlocked();
            var changed = 0;
            foreach (var orgKey in data.ByOrganization.Keys.ToList())
            {
                var resolved = OrganizationMatcher.ResolveOrganizationName(orgKey, canonical);
                if (resolved is null
                    || string.Equals(resolved, orgKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!data.ByOrganization.TryGetValue(orgKey, out var notes) || notes is null)
                {
                    continue;
                }

                data.ByOrganization.Remove(orgKey);
                if (data.ByOrganization.TryGetValue(resolved, out var existing) && existing is not null)
                {
                    foreach (var pair in notes)
                    {
                        existing[pair.Key] = pair.Value;
                    }
                }
                else
                {
                    data.ByOrganization[resolved] = notes;
                }

                changed++;
            }

            if (changed > 0)
            {
                PersistUnlocked(data);
            }

            return changed;
        }
    }

    private MedPhysicalRestrictionNotesFile LoadUnlocked()
    {
        try
        {
            if (!File.Exists(_paths.PhysicalRestrictionNotesFile))
            {
                return new MedPhysicalRestrictionNotesFile();
            }

            var json = File.ReadAllText(_paths.PhysicalRestrictionNotesFile);
            var data = JsonSerializer.Deserialize<MedPhysicalRestrictionNotesFile>(json, JsonOpts)
                       ?? new MedPhysicalRestrictionNotesFile();
            data.ByOrganization ??=
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            return data;
        }
        catch
        {
            return new MedPhysicalRestrictionNotesFile();
        }
    }

    private void PersistUnlocked(MedPhysicalRestrictionNotesFile data) =>
        File.WriteAllText(_paths.PhysicalRestrictionNotesFile, JsonSerializer.Serialize(data, JsonOpts));

    private sealed class MedPhysicalRestrictionNotesFile
    {
        public Dictionary<string, Dictionary<string, string>> ByOrganization { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}

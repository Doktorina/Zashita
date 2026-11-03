using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Реквизиты шапки печати (БИК/банк) по организации — общие для приказов ОПР, СОУТ и др.</summary>
public sealed class OrganizationPrintDetailsStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _sync = new();

    public OrganizationPrintDetailsStore(RiskDataPaths paths) => _paths = paths;

    public string? GetBankLetterhead(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return null;
        }

        lock (_sync)
        {
            var map = LoadUnlocked();
            foreach (var pair in map)
            {
                if (OrganizationMatcher.Matches(org, pair.Key)
                    && RiskPrintChrome.IsFilledBankLetterhead(pair.Value))
                {
                    return pair.Value.Trim();
                }
            }
        }

        return null;
    }

    public void SaveBankLetterhead(string organization, string? bankLetterhead)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0 || !RiskPrintChrome.IsFilledBankLetterhead(bankLetterhead))
        {
            return;
        }

        lock (_sync)
        {
            var map = LoadUnlocked();
            var key = map.Keys.FirstOrDefault(k => OrganizationMatcher.Matches(org, k)) ?? org;
            map[key] = bankLetterhead!.Trim();
            File.WriteAllText(_paths.OrgPrintDetailsFile, JsonSerializer.Serialize(map, JsonOpts));
        }
    }

    public void RenameOrganization(string oldName, string newName)
    {
        var from = (oldName ?? string.Empty).Trim();
        var to = (newName ?? string.Empty).Trim();
        if (from.Length == 0 || to.Length == 0 || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_sync)
        {
            var map = LoadUnlocked();
            var key = map.Keys.FirstOrDefault(k => OrganizationMatcher.Matches(from, k));
            if (key is null || !map.TryGetValue(key, out var value))
            {
                return;
            }

            map.Remove(key);
            var target = map.Keys.FirstOrDefault(k => OrganizationMatcher.Matches(to, k)) ?? to;
            map[target] = value;
            File.WriteAllText(_paths.OrgPrintDetailsFile, JsonSerializer.Serialize(map, JsonOpts));
        }
    }

    public void RemoveOrganization(string organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            var map = LoadUnlocked();
            var key = map.Keys.FirstOrDefault(k => OrganizationMatcher.Matches(org, k));
            if (key is null)
            {
                return;
            }

            map.Remove(key);
            File.WriteAllText(_paths.OrgPrintDetailsFile, JsonSerializer.Serialize(map, JsonOpts));
        }
    }

    private Dictionary<string, string> LoadUnlocked()
    {
        try
        {
            if (!File.Exists(_paths.OrgPrintDetailsFile))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_paths.OrgPrintDetailsFile);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

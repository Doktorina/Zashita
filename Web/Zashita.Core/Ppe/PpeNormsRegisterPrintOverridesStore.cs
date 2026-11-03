using System.Text.Json;

namespace Zashita.Core.Ppe;

public sealed class PpeNormsRegisterPrintOverridesStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PpeNormsRegisterPrintOverridesStore(PpeDataPaths paths)
    {
        _filePath = Path.Combine(paths.DataDirectory, "norms-register-print-overrides.json");
    }

    public Dictionary<string, string> Load(string organizationKey)
    {
        var all = LoadAll();
        var key = NormalizeKey(organizationKey);
        return all.TryGetValue(key, out var map)
            ? new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(string organizationKey, Dictionary<string, string> overrides)
    {
        var all = LoadAll();
        var key = NormalizeKey(organizationKey);
        all[key] = overrides
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(all, JsonOpts));
    }

    private Dictionary<string, Dictionary<string, string>> LoadAll()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, JsonOpts)
                   ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeKey(string? organization) =>
        string.IsNullOrWhiteSpace(organization) ? "__all__" : organization.Trim().ToLowerInvariant();
}

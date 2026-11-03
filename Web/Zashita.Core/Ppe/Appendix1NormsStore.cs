using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zashita.Core.Ppe;

public sealed class Appendix1NormsStore
{
    private readonly string _docxPath;
    private readonly string _cachePath;
    private readonly object _sync = new();
    private List<Appendix1NormEntry>? _entries;
    private DateTime _cachedWriteTime = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Appendix1NormsStore(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _docxPath = Path.Combine(appDataDirectory, "appendix1-construction-norms.docx");
        _cachePath = Path.Combine(appDataDirectory, "appendix1-construction-norms.json");
    }

    public bool IsAvailable => File.Exists(_docxPath) || File.Exists(_cachePath);

    public IReadOnlyList<Appendix1NormEntry> LoadAll()
    {
        EnsureLoaded();
        return _entries ?? [];
    }

    public IReadOnlyList<string> GetProfessions() =>
        LoadAll()
            .Select(e => e.Profession)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public IReadOnlyList<string> GetProtectionTypes(string? profession = null)
    {
        var query = LoadAll().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(profession))
        {
            query = query.Where(e => string.Equals(e.Profession, profession, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .Select(e => e.ProtectionType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<Appendix1NormEntry> GetForProfession(string profession) =>
        LoadAll()
            .Where(e => string.Equals(e.Profession, profession, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public IReadOnlyList<PpeNomenclatureItem> GetNomenclatureItems() =>
        Appendix1NormsParser.ToNomenclatureItems(LoadAll());

    private void EnsureLoaded()
    {
        lock (_sync)
        {
            var docxTime = File.Exists(_docxPath) ? File.GetLastWriteTimeUtc(_docxPath) : DateTime.MinValue;
            var cacheTime = File.Exists(_cachePath) ? File.GetLastWriteTimeUtc(_cachePath) : DateTime.MinValue;

            if (_entries is not null &&
                ((File.Exists(_docxPath) && _cachedWriteTime == docxTime) ||
                 (!File.Exists(_docxPath) && _cachedWriteTime == cacheTime && File.Exists(_cachePath))))
            {
                return;
            }

            if (File.Exists(_docxPath) && (docxTime > cacheTime || !File.Exists(_cachePath)))
            {
                _entries = Appendix1NormsParser.Parse(_docxPath).ToList();
                File.WriteAllText(_cachePath, JsonSerializer.Serialize(_entries, JsonOpts));
                _cachedWriteTime = docxTime;
                return;
            }

            if (File.Exists(_cachePath))
            {
                try
                {
                    var json = File.ReadAllText(_cachePath);
                    _entries = JsonSerializer.Deserialize<List<Appendix1NormEntry>>(json, JsonOpts) ?? [];
                    _cachedWriteTime = cacheTime;
                    return;
                }
                catch
                {
                    _entries = [];
                }
            }

            _entries = [];
            _cachedWriteTime = DateTime.MinValue;
        }
    }
}

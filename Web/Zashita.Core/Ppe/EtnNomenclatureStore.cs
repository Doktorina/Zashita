using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zashita.Core.Ppe;

/// <summary>Справочник «Номенклатура ЕТН» (отдельный от Приложения 1 по профессиям).</summary>
public sealed class EtnNomenclatureStore
{
    private readonly PpeDataPaths _paths;
    private readonly object _sync = new();
    private List<PpeNomenclatureItem>? _cache;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EtnNomenclatureStore(PpeDataPaths paths) => _paths = paths;

    public bool HasSourceFile => File.Exists(_paths.EtnNomenclatureSourceFile);
    public bool HasData => LoadAll().Count > 0;

    public IReadOnlyList<PpeNomenclatureItem> LoadAll()
    {
        lock (_sync)
        {
            EnsureLoadedUnlocked();
            return (_cache ?? []).Select(Clone).ToList();
        }
    }

    public List<PpeNomenclatureItem> LoadForEditor() => LoadAll().ToList();

    public void SaveAll(IEnumerable<PpeNomenclatureItem> items)
    {
        lock (_sync)
        {
            var list = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .Select(Clone)
                .ToList();
            var root = new { items = list };
            File.WriteAllText(_paths.EtnNomenclatureFile, JsonSerializer.Serialize(root, JsonOpts));
            _cache = list;
        }
    }

    /// <summary>Загрузить/перезаписать из Excel App_Data/nomenclature-etn.xlsx.</summary>
    public List<PpeNomenclatureItem> ImportFromSourceFile(bool replaceExisting = true)
    {
        if (!HasSourceFile)
        {
            return LoadForEditor();
        }

        var parsed = EtnNomenclatureParser.Parse(_paths.EtnNomenclatureSourceFile).ToList();
        if (parsed.Count == 0)
        {
            return [];
        }

        if (!replaceExisting)
        {
            var existing = LoadForEditor();
            var byName = new Dictionary<string, PpeNomenclatureItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in existing.Concat(parsed))
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    byName[item.Name] = Clone(item);
                }
            }

            parsed = byName.Values.ToList();
        }

        SaveAll(parsed);
        return parsed;
    }

    public List<PpeNomenclatureItem> ImportFromStream(Stream stream, string fileName, bool replaceExisting = true)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"etn-import-{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
        try
        {
            using (var fs = File.Create(temp))
            {
                stream.CopyTo(fs);
            }

            var parsed = EtnNomenclatureParser.Parse(temp).ToList();
            if (parsed.Count == 0)
            {
                return [];
            }

            if (!replaceExisting)
            {
                var existing = LoadForEditor();
                var byName = new Dictionary<string, PpeNomenclatureItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in existing.Concat(parsed))
                {
                    if (!string.IsNullOrWhiteSpace(item.Name))
                    {
                        byName[item.Name] = Clone(item);
                    }
                }

                parsed = byName.Values.ToList();
            }

            SaveAll(parsed);
            try
            {
                File.Copy(temp, _paths.EtnNomenclatureSourceFile, overwrite: true);
            }
            catch
            {
                // JSON уже сохранён — копия Excel необязательна.
            }

            return parsed;
        }
        finally
        {
            try { File.Delete(temp); } catch { /* ignore */ }
        }
    }

    public IReadOnlyList<string> GetProtectionTypes() =>
        LoadAll()
            .Select(i => i.DisplayProtectionType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private void EnsureLoadedUnlocked()
    {
        if (_cache is not null)
        {
            return;
        }

        _cache = ReadJsonFile();
        if (_cache.Count > 0)
        {
            return;
        }

        if (HasSourceFile)
        {
            _cache = EtnNomenclatureParser.Parse(_paths.EtnNomenclatureSourceFile).ToList();
            if (_cache.Count > 0)
            {
                var root = new { items = _cache };
                File.WriteAllText(_paths.EtnNomenclatureFile, JsonSerializer.Serialize(root, JsonOpts));
            }
        }
    }

    private List<PpeNomenclatureItem> ReadJsonFile()
    {
        try
        {
            if (!File.Exists(_paths.EtnNomenclatureFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.EtnNomenclatureFile);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var arr))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<PpeNomenclatureItem>>(arr.GetRawText(), JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static PpeNomenclatureItem Clone(PpeNomenclatureItem source) => new()
    {
        Category = source.Category,
        ProtectionType = source.ProtectionType,
        Name = source.Name,
        Norm = source.Norm,
        NormParagraph = source.NormParagraph
    };
}

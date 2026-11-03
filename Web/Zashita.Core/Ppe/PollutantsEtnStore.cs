using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zashita.Core.Ppe;

/// <summary>Справочник «Производственные загрязнители ЕТН» (дерматологические СИЗ / смывающие).</summary>
public sealed class PollutantsEtnStore
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

    public PollutantsEtnStore(PpeDataPaths paths) => _paths = paths;

    public bool HasSourceFile => File.Exists(_paths.PollutantsEtnSourceFile);
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
            foreach (var item in list)
            {
                item.Category = "ДСИЗ";
            }

            File.WriteAllText(_paths.PollutantsEtnFile, JsonSerializer.Serialize(new { items = list }, JsonOpts));
            _cache = list;
        }
    }

    public List<PpeNomenclatureItem> ImportFromSourceFile(bool replaceExisting = true)
    {
        if (!HasSourceFile)
        {
            return LoadForEditor();
        }

        var parsed = PollutantsEtnParser.Parse(_paths.PollutantsEtnSourceFile).ToList();
        if (parsed.Count == 0)
        {
            return [];
        }

        if (!replaceExisting)
        {
            var byKey = new Dictionary<string, PpeNomenclatureItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in LoadForEditor().Concat(parsed))
            {
                byKey[$"{item.ProtectionType}|{item.Name}"] = Clone(item);
            }

            parsed = byKey.Values.ToList();
        }

        SaveAll(parsed);
        return parsed;
    }

    public List<PpeNomenclatureItem> ImportFromStream(Stream stream, string fileName, bool replaceExisting = true)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"pollutants-etn-{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
        try
        {
            using (var fs = File.Create(temp))
            {
                stream.CopyTo(fs);
            }

            var parsed = PollutantsEtnParser.Parse(temp).ToList();
            if (parsed.Count == 0)
            {
                return [];
            }

            if (!replaceExisting)
            {
                var byKey = new Dictionary<string, PpeNomenclatureItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in LoadForEditor().Concat(parsed))
                {
                    byKey[$"{item.ProtectionType}|{item.Name}"] = Clone(item);
                }

                parsed = byKey.Values.ToList();
            }

            SaveAll(parsed);
            try
            {
                File.Copy(temp, _paths.PollutantsEtnSourceFile, overwrite: true);
            }
            catch
            {
                // JSON уже сохранён.
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
            _cache = PollutantsEtnParser.Parse(_paths.PollutantsEtnSourceFile).ToList();
            if (_cache.Count > 0)
            {
                File.WriteAllText(
                    _paths.PollutantsEtnFile,
                    JsonSerializer.Serialize(new { items = _cache }, JsonOpts));
            }
        }
    }

    private List<PpeNomenclatureItem> ReadJsonFile()
    {
        try
        {
            if (!File.Exists(_paths.PollutantsEtnFile))
            {
                return [];
            }

            var json = File.ReadAllText(_paths.PollutantsEtnFile);
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
        Category = "ДСИЗ",
        ProtectionType = source.ProtectionType,
        Name = source.Name,
        Norm = source.Norm,
        NormParagraph = source.NormParagraph
    };
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Zashita.Core.Risks;

public sealed class SoutConditionsClassStore
{
    private readonly RiskDataPaths _paths;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SoutConditionsClassStore(RiskDataPaths paths) => _paths = paths;

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(_paths.ConditionsClassesFile))
            {
                var seeded = CreateDefaults();
                File.WriteAllText(_paths.ConditionsClassesFile, JsonSerializer.Serialize(seeded, JsonOpts));
                return seeded;
            }

            var json = File.ReadAllText(_paths.ConditionsClassesFile);
            var data = JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
            data = data
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (data.Count == 0 || NeedsCanonicalRefresh(data))
            {
                data = CreateDefaults();
                File.WriteAllText(_paths.ConditionsClassesFile, JsonSerializer.Serialize(data, JsonOpts));
            }

            return data;
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public void Save(IEnumerable<string> values)
    {
        var list = values
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        File.WriteAllText(_paths.ConditionsClassesFile, JsonSerializer.Serialize(list, JsonOpts));
    }

    /// <summary>
    /// Приводит произвольное значение из Word/старых данных к пункту перечня классов.
    /// </summary>
    public string MapToCatalog(string? raw) => MapToCatalog(raw, Load());

    public static string MapToCatalog(string? raw, IReadOnlyList<string> catalog)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0 || catalog.Count == 0)
        {
            return value;
        }

        foreach (var opt in catalog)
        {
            if (string.Equals(opt, value, StringComparison.OrdinalIgnoreCase))
            {
                return opt;
            }
        }

        var code = ExtractSubclassCode(value);
        if (string.IsNullOrWhiteSpace(code))
        {
            return value;
        }

        // Точное совпадение кода («3.1», «2»).
        var exactCode = catalog.FirstOrDefault(opt =>
            string.Equals(ExtractSubclassCode(opt), code, StringComparison.OrdinalIgnoreCase)
            && string.Equals(opt.Trim(), code, StringComparison.OrdinalIgnoreCase));
        if (exactCode is not null)
        {
            // Предпочитаем описательный пункт перечня, если есть.
            var descriptive = catalog.FirstOrDefault(opt =>
                string.Equals(ExtractSubclassCode(opt), code, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(opt.Trim(), code, StringComparison.OrdinalIgnoreCase));
            return descriptive ?? exactCode;
        }

        var byCode = catalog
            .Where(opt => string.Equals(ExtractSubclassCode(opt), code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(opt => opt.Length)
            .FirstOrDefault();
        return byCode ?? value;
    }

    /// <summary>Код подкласса: 1, 2, 3.1 … 3.4, 4.</summary>
    public static string? ExtractSubclassCode(string? text)
    {
        var value = (text ?? string.Empty).Trim().Replace(',', '.');
        if (value.Length == 0)
        {
            return null;
        }

        if (Regex.IsMatch(value, @"^[1-4](?:\.[1-4])?$"))
        {
            return value;
        }

        // «Вредные (класс 3.1)», «класс 2», «3.1 (вредные)», «итоговый класс (подкласс) 3.2»
        var m = Regex.Match(
            value,
            @"(?:класс(?:\s*\(подкласс\))?\s*)?(?<c>[1-4](?:\.[1-4])?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            m = Regex.Match(value, @"^(?<c>[1-4](?:\.[1-4])?)\s*\(");
        }

        if (!m.Success)
        {
            return null;
        }

        var code = m.Groups["c"].Value.Replace(',', '.');
        if (code.Contains('.', StringComparison.Ordinal))
        {
            var parts = code.Split('.');
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major)
                && major == 3
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor)
                && minor is >= 1 and <= 4)
            {
                return $"{major}.{minor}";
            }

            return null;
        }

        if (int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 4)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static bool NeedsCanonicalRefresh(IReadOnlyList<string> data)
    {
        // Старый посев без коротких подписей «1 (оптимальные)» — обновляем перечень.
        var hasShort = data.Any(x =>
            x.Contains("(оптимальные)", StringComparison.OrdinalIgnoreCase)
            || x.Contains("(допустимые)", StringComparison.OrdinalIgnoreCase)
            || x.Contains("(вредные)", StringComparison.OrdinalIgnoreCase)
            || x.Contains("(опасные)", StringComparison.OrdinalIgnoreCase));
        return !hasShort;
    }

    private static List<string> CreateDefaults() =>
    [
        "1 (оптимальные)",
        "2 (допустимые)",
        "3.1 (вредные)",
        "3.2 (вредные)",
        "3.3 (вредные)",
        "3.4 (вредные)",
        "4 (опасные)"
    ];
}

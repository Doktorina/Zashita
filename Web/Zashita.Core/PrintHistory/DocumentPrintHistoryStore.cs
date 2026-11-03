using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zashita.Core.PrintHistory;

public sealed class DocumentPrintHistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime PrintedAt { get; set; } = DateTime.Now;
    /// <summary>journal | ppe | med | risks | sout | training | other</summary>
    public string Category { get; set; } = "other";
    public string Title { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? OpenUrl { get; set; }
}

public sealed class DocumentPrintHistoryStore
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public DocumentPrintHistoryStore(string appDataFolder)
    {
        Directory.CreateDirectory(appDataFolder);
        _filePath = Path.Combine(appDataFolder, "print-history.json");
    }

    public void Append(
        string category,
        string title,
        string organization,
        string? detail = null,
        string? summary = null,
        string? openUrl = null)
    {
        Append(new DocumentPrintHistoryRecord
        {
            PrintedAt = DateTime.Now,
            Category = string.IsNullOrWhiteSpace(category) ? "other" : category.Trim().ToLowerInvariant(),
            Title = title?.Trim() ?? string.Empty,
            Organization = organization?.Trim() ?? string.Empty,
            Detail = detail?.Trim() ?? string.Empty,
            Summary = summary?.Trim() ?? string.Empty,
            OpenUrl = string.IsNullOrWhiteSpace(openUrl) ? null : openUrl.Trim()
        });
    }

    public void Append(DocumentPrintHistoryRecord record)
    {
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var list = LoadAllUnlocked().ToList();
                list.Insert(0, record);
                if (list.Count > 500)
                {
                    list = list.Take(500).ToList();
                }

                File.WriteAllText(_filePath, JsonSerializer.Serialize(list, JsonOpts));
            }
            catch
            {
                // не роняем печать из‑за сбоя журнала истории
            }
        }
    }

    public IReadOnlyList<DocumentPrintHistoryRecord> Query(
        string? search = null,
        DateTime? from = null,
        DateTime? to = null,
        string? organization = null,
        string? category = null)
    {
        lock (_gate)
        {
            IEnumerable<DocumentPrintHistoryRecord> q = LoadAllUnlocked();

            if (from is { } f)
            {
                q = q.Where(x => x.PrintedAt.Date >= f.Date);
            }

            if (to is { } t)
            {
                q = q.Where(x => x.PrintedAt.Date <= t.Date);
            }

            if (!string.IsNullOrWhiteSpace(organization))
            {
                q = q.Where(x =>
                    string.IsNullOrWhiteSpace(x.Organization) ||
                    x.Organization.Contains(organization, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(category) &&
                !category.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(x =>
                    x.Title.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.Organization.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.Detail.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.Summary.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.Category.Contains(s, StringComparison.OrdinalIgnoreCase));
            }

            return q.OrderByDescending(x => x.PrintedAt).ToList();
        }
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (_gate)
        {
            try
            {
                var list = LoadAllUnlocked().ToList();
                var removed = list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    return false;
                }

                File.WriteAllText(_filePath, JsonSerializer.Serialize(list, JsonOpts));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private IReadOnlyList<DocumentPrintHistoryRecord> LoadAllUnlocked()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<DocumentPrintHistoryRecord>>(
                       File.ReadAllText(_filePath), JsonOpts)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }
}

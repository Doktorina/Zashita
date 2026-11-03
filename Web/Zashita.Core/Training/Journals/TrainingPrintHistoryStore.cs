using System.Text.Json;
using System.Text.Json.Serialization;
using Zashita.Core.PrintHistory;
using Zashita.Core.Training;

namespace Zashita.Core.Training.Journals;

public sealed class TrainingPrintHistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime PrintedAt { get; set; } = DateTime.Now;
    public TrainingJournalKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string? BriefingType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int EntryCount { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>История печати журналов — пишет и в legacy-файл, и в общий print-history.json.</summary>
public sealed class TrainingPrintHistoryStore
{
    private readonly TrainingDataPaths _paths;
    private readonly DocumentPrintHistoryStore? _unified;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public TrainingPrintHistoryStore(TrainingDataPaths paths, DocumentPrintHistoryStore? unified = null)
    {
        _paths = paths;
        _unified = unified;
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile) ?? _paths.JournalsDirectory);
    }

    public void Append(TrainingPrintHistoryRecord record)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile) ?? _paths.JournalsDirectory);
                var list = LoadAllUnlocked().ToList();
                list.Insert(0, record);
                if (list.Count > 200)
                {
                    list = list.Take(200).ToList();
                }

                File.WriteAllText(HistoryFile, JsonSerializer.Serialize(list, JsonOpts));
            }
            catch
            {
                // ignore
            }
        }

        _unified?.Append(
            category: "journal",
            title: record.Title,
            organization: record.Organization,
            detail: string.IsNullOrWhiteSpace(record.BriefingType) ? record.Kind.ToString() : record.BriefingType,
            summary: string.IsNullOrWhiteSpace(record.Summary)
                ? $"Строк: {record.EntryCount}"
                : record.Summary,
            openUrl: $"/Training/Journal?title={Uri.EscapeDataString(record.Title)}");
    }

    public IReadOnlyList<TrainingPrintHistoryRecord> Query(
        string? search = null,
        DateTime? printedFrom = null,
        DateTime? printedTo = null,
        TrainingJournalKind? kind = null,
        string? organization = null,
        string? briefingType = null)
    {
        lock (_gate)
        {
            IEnumerable<TrainingPrintHistoryRecord> q = LoadAllUnlocked();

            if (printedFrom is { } from)
            {
                q = q.Where(x => x.PrintedAt.Date >= from.Date);
            }

            if (printedTo is { } to)
            {
                q = q.Where(x => x.PrintedAt.Date <= to.Date);
            }

            if (kind is { } k)
            {
                q = q.Where(x => x.Kind == k);
            }

            if (!string.IsNullOrWhiteSpace(organization))
            {
                q = q.Where(x =>
                    string.IsNullOrWhiteSpace(x.Organization) ||
                    x.Organization.Contains(organization, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(briefingType))
            {
                q = q.Where(x => (x.BriefingType ?? string.Empty)
                    .Contains(briefingType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(x =>
                    x.Title.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.Organization.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    x.Summary.Contains(s, StringComparison.OrdinalIgnoreCase));
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
                var path = ResolveExistingFile();
                if (path is null)
                {
                    return false;
                }

                var list = JsonSerializer.Deserialize<List<TrainingPrintHistoryRecord>>(
                               File.ReadAllText(path), JsonOpts)
                           ?? [];
                var removed = list.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile) ?? _paths.JournalsDirectory);
                File.WriteAllText(HistoryFile, JsonSerializer.Serialize(list, JsonOpts));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private string HistoryFile =>
        Path.Combine(_paths.JournalsDirectory, "training-print-history.json");

    private string? ResolveExistingFile()
    {
        foreach (var path in new[]
                 {
                     HistoryFile,
                     Path.Combine(Path.GetDirectoryName(_paths.JournalsHistoryFile) ?? "", "training-print-history.json")
                 })
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private IReadOnlyList<TrainingPrintHistoryRecord> LoadAllUnlocked()
    {
        try
        {
            var path = ResolveExistingFile();
            if (path is null)
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<TrainingPrintHistoryRecord>>(
                       File.ReadAllText(path), JsonOpts)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }
}

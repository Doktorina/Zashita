using System.Text.Json;
using Zashita.Core.Employees;

namespace Zashita.Web.Services.Connections;

public sealed class OneCSyncLogStore
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private const int MaxRuns = 50;

    public OneCSyncLogStore(IWebHostEnvironment env)
    {
        var appData = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appData);
        _filePath = Path.Combine(appData, "one-c-sync-log.json");
    }

    public IReadOnlyList<OneCSyncRunLog> LoadRecent(int take = 20)
    {
        lock (_sync)
        {
            return LoadLocked()
                .OrderByDescending(r => r.StartedAt)
                .Take(Math.Max(1, take))
                .ToList();
        }
    }

    public OneCSyncRunLog? GetById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (_sync)
        {
            return LoadLocked()
                .FirstOrDefault(r => string.Equals(r.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Append(OneCSyncRunLog run)
    {
        lock (_sync)
        {
            var list = LoadLocked();
            list.Insert(0, run);
            if (list.Count > MaxRuns)
            {
                list = list.Take(MaxRuns).ToList();
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(new OneCSyncLogDocument { Runs = list }, JsonOpts));
        }
    }

    private List<OneCSyncRunLog> LoadLocked()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = File.ReadAllText(_filePath);
            var doc = JsonSerializer.Deserialize<OneCSyncLogDocument>(json, JsonOpts);
            return doc?.Runs ?? [];
        }
        catch
        {
            return [];
        }
    }
}

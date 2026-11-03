using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Excel;
using Zashita.Core.Security;
using Zashita.Web.Services;
using Zashita.Web.Services.Connections;

namespace Zashita.Web.Pages.Data;

[RequireLogin]
public class UploadModel : PageModel
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private readonly ExcelWorkbookStore _store;
    private readonly OneCEmployeeSyncService _sync;
    private readonly OneCSyncLogStore _syncLog;
    private readonly StaffingChangeStore _staffingChanges;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;
    private readonly GlobalSearchService _search;
    private readonly IWebHostEnvironment _env;

    public UploadModel(
        ExcelWorkbookStore store,
        OneCEmployeeSyncService sync,
        OneCSyncLogStore syncLog,
        StaffingChangeStore staffingChanges,
        OrganizationStore orgs,
        OrganizationEmployeesService employees,
        GlobalSearchService search,
        IWebHostEnvironment env)
    {
        _store = store;
        _sync = sync;
        _syncLog = syncLog;
        _staffingChanges = staffingChanges;
        _orgs = orgs;
        _employees = employees;
        _search = search;
        _env = env;
    }

    public bool HasWorkbook => _store.HasWorkbook;
    public IReadOnlyList<string> SheetNames => _store.SheetNames;
    public string? Message { get; set; }
    public bool IsError { get; set; }
    public string SharePath => _sync.EffectiveSharePath;
    public string ConfiguredSharePath => _sync.ConfiguredSharePath;
    public bool SharePathMissing => _sync.SharePathMissing;
    public string? NewestFileName { get; private set; }
    public bool HasWorkersFile { get; private set; }
    public IReadOnlyList<OneCSyncRunLog> SyncRuns { get; private set; } = [];
    public ManualDataSyncInfo? LastManualPush { get; private set; }

    public void OnGet() => LoadStatus();

    public async Task<IActionResult> OnPostAsync(IFormFile? workbook, CancellationToken cancellationToken)
    {
        if (workbook is null || workbook.Length == 0)
        {
            IsError = true;
            Message = "Выберите файл Excel.";
            LoadStatus();
            return Page();
        }

        if (workbook.Length > MaxUploadBytes)
        {
            IsError = true;
            Message = "Файл слишком большой (максимум 20 МБ).";
            LoadStatus();
            return Page();
        }

        var name = Path.GetFileName(workbook.FileName);
        if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            IsError = true;
            Message = "Поддерживаются только файлы .xlsx и .xls.";
            LoadStatus();
            return Page();
        }

        try
        {
            await using var stream = workbook.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            _store.LoadFromStream(buffer, name);

            var appData = Path.Combine(_env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(appData);
            var target = SecurePath.CombineUnder(appData, OneCSyncContract.WorkersFileName);
            buffer.Position = 0;
            await using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                await buffer.CopyToAsync(fs, cancellationToken);
            }

            var run = _sync.SyncFromWorkersFile("manual-upload", name);
            IsError = !run.Success;
            Message = $"Файл «{name}» загружен. {run.Message} Организаций в списке: {_orgs.AllOrganizations.Count}.";
            try { _search.RebuildIndex(); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = $"Ошибка чтения файла: {ex.Message}";
        }

        LoadStatus();
        return Page();
    }

    public IActionResult OnPostSyncShared()
    {
        var run = _sync.ReplaceFromShared("manual-rebuild");
        _employees.InvalidateStaffCaches();
        IsError = !run.Success;
        Message = run.Success
            ? $"{run.Message} Активных в штате: {run.Updated}. Обновите страницу организации (F5)."
            : run.Message;
        try { _search.RebuildIndex(); } catch { /* ignore */ }
        LoadStatus();
        return Page();
    }

    /// <summary>JSON: расшифровка прогона (приёмы / увольнения / переводы).</summary>
    public IActionResult OnGetSyncRun(string? id)
    {
        var run = _syncLog.GetById(id);
        if (run is null)
        {
            return new JsonResult(new { ok = false, error = "Прогон не найден." })
            {
                StatusCode = 404
            };
        }

        var (changes, fromFallback, truncated) = ResolveStaffChanges(run);
        var hired = changes.Count(c => IsKind(c.Kind, "Hired"));
        var fired = changes.Count(c => IsKind(c.Kind, "Fired"));
        var transferred = changes.Count(c => IsKind(c.Kind, "Transferred"));
        return new JsonResult(new
        {
            ok = true,
            id = run.Id,
            startedAt = run.StartedAt.ToString("dd.MM.yyyy HH:mm"),
            source = run.Source,
            fileName = run.FileName,
            message = run.Message,
            success = run.Success,
            truncated,
            fromFallback,
            hired,
            fired,
            transferred,
            filteredInactive = run.FilteredInactive,
            filteredValidation = run.FilteredValidation,
            deduplicated = run.Deduplicated,
            hasDetails = hired + fired + transferred > 0
                         || run.FilteredInactive > 0
                         || run.Deduplicated > 0,
            changes = changes.Select(c => new
            {
                kind = c.Kind,
                kindLabel = OneCSyncStaffChange.KindLabel(c.Kind),
                date = c.OccurredAt?.ToString("dd.MM.yyyy HH:mm") ?? "",
                organization = c.Organization,
                fromOrganization = c.FromOrganization,
                person = c.Person,
                profession = c.Profession,
                message = c.Message
            })
        });
    }

    /// <summary>
    /// Скачать исходные файлы 1С из колонки «Файл» (Employees_zup*.csv), не расшифровку приёмов.
    /// </summary>
    public IActionResult OnGetSyncRunSourceFiles(string? id)
    {
        var run = _syncLog.GetById(id);
        if (run is null)
        {
            return NotFound();
        }

        var share = _sync.ConfiguredSharePath;
        if (string.IsNullOrWhiteSpace(share) || !Directory.Exists(share))
        {
            return NotFound($"Папка 1С недоступна: {share}");
        }

        var names = ParseRunSourceFileNames(run.FileName);
        if (names.Count == 0)
        {
            names = OneCSyncContract.CurrentLiveCsvNames.ToList();
        }

        var existing = new List<string>();
        foreach (var name in names)
        {
            try
            {
                var full = SecurePath.CombineUnder(share, name);
                if (System.IO.File.Exists(full))
                {
                    existing.Add(full);
                }
            }
            catch
            {
                // имя вне share — пропускаем
            }
        }

        if (existing.Count == 0)
        {
            return NotFound(
                "Файлы из прогона не найдены в папке 1С: " + string.Join(", ", names));
        }

        if (existing.Count == 1)
        {
            var path = existing[0];
            var bytes = System.IO.File.ReadAllBytes(path);
            return File(bytes, "text/csv; charset=utf-8", Path.GetFileName(path));
        }

        using var zipMs = new MemoryStream();
        using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var path in existing)
            {
                var entryName = Path.GetFileName(path);
                var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var fs = System.IO.File.OpenRead(path);
                fs.CopyTo(entryStream);
            }
        }

        var stamp = run.StartedAt.ToString("yyyyMMdd-HHmm");
        return File(
            zipMs.ToArray(),
            "application/zip",
            $"1c-staff-{stamp}.zip");
    }

    /// <summary>CSV-выгрузка расшифровки прогона (приёмы / увольнения / переводы).</summary>
    public IActionResult OnGetSyncRunExport(string? id)
    {
        var run = _syncLog.GetById(id);
        if (run is null)
        {
            return NotFound();
        }

        var (changes, _, _) = ResolveStaffChanges(run);
        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // Excel UTF-8 BOM
        sb.AppendLine("Вид;Дата;ФИО;Компания;Откуда;Должность;Комментарий");
        foreach (var c in changes)
        {
            sb.Append(Csv(OneCSyncStaffChange.KindLabel(c.Kind))).Append(';');
            sb.Append(Csv(c.OccurredAt?.ToString("dd.MM.yyyy HH:mm"))).Append(';');
            sb.Append(Csv(c.Person)).Append(';');
            sb.Append(Csv(c.Organization)).Append(';');
            sb.Append(Csv(c.FromOrganization)).Append(';');
            sb.Append(Csv(c.Profession)).Append(';');
            sb.Append(Csv(c.Message)).AppendLine();
        }

        var stamp = run.StartedAt.ToString("yyyyMMdd-HHmm");
        var fileName = $"sync-details-{stamp}-{run.Id[..Math.Min(8, run.Id.Length)]}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", fileName);
    }

    private static List<string> ParseRunSourceFileNames(string? fileName)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return result;
        }

        foreach (var part in fileName.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = Path.GetFileName(part.Trim());
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!result.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(name);
            }
        }

        return result;
    }

    /// <summary>
    /// Расшифровка из самого прогона; если пусто (старый журнал) — из staffing-changes по времени синка.
    /// </summary>
    private (List<OneCSyncStaffChange> Changes, bool FromFallback, bool Truncated) ResolveStaffChanges(OneCSyncRunLog run)
    {
        var stored = run.StaffChanges ?? [];
        if (stored.Count > 0)
        {
            return (stored, false, run.StaffChangesTruncated);
        }

        // Окно: от старта прогона до следующего прогона (или +6 ч), чтобы не потерять события.
        var from = run.StartedAt.AddMinutes(-2);
        var nextStart = _syncLog.LoadRecent(50)
            .Where(r => r.StartedAt > run.StartedAt)
            .Select(r => (DateTime?)r.StartedAt)
            .DefaultIfEmpty(null)
            .Min();
        var to = nextStart?.AddMinutes(-1)
                 ?? run.FinishedAt?.AddMinutes(30)
                 ?? run.StartedAt.AddHours(6);
        if (to <= from)
        {
            to = from.AddHours(2);
        }

        var events = _staffingChanges.LoadAround(from, to, OneCSyncRunLog.MaxStaffChangesStored);
        var changes = events
            .Select(OneCSyncStaffChange.FromEvent)
            .Where(c => !string.IsNullOrWhiteSpace(c.Person))
            .ToList();
        var truncated = events.Count >= OneCSyncRunLog.MaxStaffChangesStored;
        return (changes, changes.Count > 0, truncated);
    }

    private static bool IsKind(string? kind, string expected) =>
        string.Equals(kind, expected, StringComparison.OrdinalIgnoreCase);

    private static string Csv(string? value)
    {
        var t = (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
        return t.Contains(';') || t.Contains('"') || t.Contains('\n')
            ? $"\"{t}\""
            : t;
    }

    private void LoadStatus()
    {
        NewestFileName = _sync.FindNewestSharedFile()?.Name;
        HasWorkersFile = System.IO.File.Exists(_sync.WorkersFilePath);
        SyncRuns = _syncLog.LoadRecent(15);
        LastManualPush = ManualDataSyncInfo.TryLoad(
            Path.Combine(_env.ContentRootPath, "App_Data", "manual-data-sync.json"));
    }
}

public sealed class ManualDataSyncInfo
{
    public string? SyncedAtDisplay { get; init; }
    public string? Summary { get; init; }
    public string? Machine { get; init; }
    public string? Source { get; init; }
    public int FilesChanged { get; init; }
    public IReadOnlyList<ManualDataSyncChange> Changes { get; init; } = [];

    public static ManualDataSyncInfo? TryLoad(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = doc.RootElement;
            var display = root.TryGetProperty("SyncedAtDisplay", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(display)
                && root.TryGetProperty("SyncedAt", out var raw)
                && DateTimeOffset.TryParse(raw.GetString(), out var dto))
            {
                display = dto.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            }

            var changes = new List<ManualDataSyncChange>();
            if (root.TryGetProperty("Changes", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray().Take(40))
                {
                    changes.Add(new ManualDataSyncChange(
                        item.TryGetProperty("Path", out var p) ? p.GetString() ?? "" : "",
                        item.TryGetProperty("Kind", out var k) ? k.GetString() ?? "" : "",
                        item.TryGetProperty("Note", out var n) ? n.GetString() ?? "" : ""));
                }
            }

            return new ManualDataSyncInfo
            {
                SyncedAtDisplay = display,
                Summary = root.TryGetProperty("Summary", out var s) ? s.GetString() : null,
                Machine = root.TryGetProperty("Machine", out var m) ? m.GetString() : null,
                Source = root.TryGetProperty("Source", out var src) ? src.GetString() : null,
                FilesChanged = root.TryGetProperty("FilesChanged", out var fc) && fc.TryGetInt32(out var nFiles)
                    ? nFiles
                    : changes.Count,
                Changes = changes
            };
        }
        catch
        {
            return null;
        }
    }
}

public sealed record ManualDataSyncChange(string Path, string Kind, string Note);

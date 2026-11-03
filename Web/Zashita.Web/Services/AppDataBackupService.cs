using System.IO.Compression;

namespace Zashita.Web.Services;

/// <summary>
/// Архивы App_Data: риски, СОУТ, медосмотры, обучение и прочие JSON/сканы модулей.
/// — hourly: краткий страховой снимок (ротация);
/// — daily: полный суточный снимок перед ночным обновлением 1С (дольше хранится).
/// </summary>
public sealed class AppDataBackupService
{
    /// <summary>Почасовые/периодические: ~3 суток при интервале 6 ч.</summary>
    public const int KeepHourlyCount = 72;

    /// <summary>Суточные: 90 дней.</summary>
    public const int KeepDailyCount = 90;

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "backups",
        "inbox",
        "_tmp",
        "_risk-card-sample"
    };

    private static readonly HashSet<string> ExcludedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "errors.log",
        "med-status-audit.log"
    };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AppDataBackupService> _logger;
    private readonly object _sync = new();

    public AppDataBackupService(IWebHostEnvironment env, ILogger<AppDataBackupService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public string HourlyBackupRoot =>
        Path.Combine(_env.ContentRootPath, "App_Data", "backups", "hourly");

    public string DailyBackupRoot =>
        Path.Combine(_env.ContentRootPath, "App_Data", "backups", "daily");

    /// <summary>Совместимость со старым API.</summary>
    public string BackupRoot => HourlyBackupRoot;

    public AppDataBackupResult CreateHourlyBackup() =>
        CreateBackup(
            HourlyBackupRoot,
            $"appdata-{DateTime.Now:yyyy-MM-dd_HH-mm}.zip",
            KeepHourlyCount,
            kind: "hourly",
            compression: CompressionLevel.Fastest);

    /// <summary>
    /// Суточный полный снимок (риски, карты СОУТ, медосмотры, обучение и т.д.).
    /// Один файл на календарный день; повторный вызов в тот же день перезаписывает.
    /// </summary>
    public AppDataBackupResult CreateDailyBackup(DateTime? localStamp = null)
    {
        var when = localStamp ?? DateTime.Now;
        return CreateBackup(
            DailyBackupRoot,
            $"appdata-daily-{when:yyyy-MM-dd}.zip",
            KeepDailyCount,
            kind: "daily",
            compression: CompressionLevel.Optimal);
    }

    private AppDataBackupResult CreateBackup(
        string backupRoot,
        string fileName,
        int keepCount,
        string kind,
        CompressionLevel compression)
    {
        lock (_sync)
        {
            var appData = Path.Combine(_env.ContentRootPath, "App_Data");
            if (!Directory.Exists(appData))
            {
                return new AppDataBackupResult(false, null, 0, 0, "App_Data не найден");
            }

            Directory.CreateDirectory(backupRoot);
            var zipPath = Path.Combine(backupRoot, fileName);
            var tempPath = zipPath + ".partial";

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                var fileCount = 0;
                using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.EnumerateFiles(appData, "*", SearchOption.AllDirectories))
                    {
                        if (!ShouldInclude(appData, file))
                        {
                            continue;
                        }

                        var entryName = Path.GetRelativePath(appData, file).Replace('\\', '/');
                        archive.CreateEntryFromFile(file, entryName, compression);
                        fileCount++;
                    }
                }

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                File.Move(tempPath, zipPath);
                var sizeBytes = new FileInfo(zipPath).Length;
                var removed = PruneOldBackups(backupRoot, keepCount, fileName.StartsWith("appdata-daily-", StringComparison.OrdinalIgnoreCase)
                    ? "appdata-daily-*.zip"
                    : "appdata-*.zip");

                _logger.LogInformation(
                    "App_Data {Kind} backup: {Zip} ({SizeMb:0.0} MB, {Files} files), pruned {Removed}",
                    kind,
                    Path.GetFileName(zipPath),
                    sizeBytes / (1024d * 1024d),
                    fileCount,
                    removed);

                return new AppDataBackupResult(true, zipPath, fileCount, sizeBytes, null);
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // ignore
                }

                _logger.LogError(ex, "App_Data {Kind} backup failed", kind);
                return new AppDataBackupResult(false, null, 0, 0, ex.Message);
            }
        }
    }

    public static int PruneOldBackups(string backupRoot, int keepCount) =>
        PruneOldBackups(backupRoot, keepCount, "appdata-*.zip");

    public static int PruneOldBackups(string backupRoot, int keepCount, string searchPattern)
    {
        if (!Directory.Exists(backupRoot) || keepCount < 1)
        {
            return 0;
        }

        var files = Directory.GetFiles(backupRoot, searchPattern)
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var removed = 0;
        foreach (var old in files.Skip(keepCount))
        {
            try
            {
                old.Delete();
                removed++;
            }
            catch
            {
                // ignore locked files
            }
        }

        return removed;
    }

    private static bool ShouldInclude(string appDataRoot, string filePath)
    {
        var name = Path.GetFileName(filePath);
        if (ExcludedFileNames.Contains(name))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        if (ext.Equals(".log", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bak", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".zip.tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(appDataRoot, filePath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            if (ExcludedDirectoryNames.Contains(part)
                || part.StartsWith("_tmp", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith(".", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record AppDataBackupResult(
    bool Success,
    string? ZipPath,
    int FileCount,
    long SizeBytes,
    string? Error);

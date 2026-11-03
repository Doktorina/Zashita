using Zashita.Core.Employees;
using Zashita.Web.Services;

namespace Zashita.Web.Services.Connections;

/// <summary>
/// Автоимпорт 1С без ручного запуска:
/// — каждый день в 04:00 по Екатеринбургу;
/// — перед ночным синком — полный zip-бэкап App_Data (риски, СОУТ, медосмотры, обучение…);
/// — сразу, если в SharedPath (/srv/share) появился более новый файл (проверка каждые 5 мин);
/// — после рестарта — если файл новее последней успешной синхронизации.
/// Нужны: systemd-служба сайта 24/7 и доступный OneC:SharedPath.
/// </summary>
public sealed class OneCDailySyncHostedService : BackgroundService
{
    private static readonly TimeZoneInfo Ekaterinburg = ResolveEkaterinburgTimeZone();
    private static readonly TimeSpan NightSyncLocalTime = new(4, 0, 0);
    private const string StampFileName = "one-c-sync-stamp.json";

    private readonly IServiceProvider _services;
    private readonly IWebHostEnvironment _env;
    private readonly AppDataBackupService _backup;
    private readonly ILogger<OneCDailySyncHostedService> _logger;
    private DateTime _lastProcessedWriteUtc = DateTime.MinValue;
    private DateOnly? _lastNightSyncLocalDate;
    private DateOnly? _lastDailyBackupLocalDate;
    private DateTime _lastMissingPathLogUtc = DateTime.MinValue;

    public OneCDailySyncHostedService(
        IServiceProvider services,
        IWebHostEnvironment env,
        AppDataBackupService backup,
        ILogger<OneCDailySyncHostedService> logger)
    {
        _services = services;
        _env = env;
        _backup = backup;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Короткая пауза после старта — не блокировать поднятие сайта, но и не ждать 10 минут.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _lastProcessedWriteUtc = LoadStampWriteUtc();

        using (var bootScope = _services.CreateScope())
        {
            var bootSync = bootScope.ServiceProvider.GetRequiredService<OneCEmployeeSyncService>();
            if (!bootSync.IsEnabled)
            {
                _logger.LogInformation("1C auto-sync disabled (OneC:Enabled=false). Demo uses App_Data only.");
                return;
            }
        }

        _logger.LogInformation(
            "1C auto-sync armed: SharedPath check every 5 min, daily 04:00 Asia/Yekaterinburg, stampUtc={Stamp:u}",
            _lastProcessedWriteUtc == DateTime.MinValue ? null : _lastProcessedWriteUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<OneCEmployeeSyncService>();
                var log = scope.ServiceProvider.GetRequiredService<OneCSyncLogStore>();
                var newest = sync.FindNewestSharedFile();
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ekaterinburg);
                var todayLocal = DateOnly.FromDateTime(nowLocal);

                // Первый тик после рестарта: если stamp пустой — берём время последнего успешного синка из журнала.
                if (_lastProcessedWriteUtc == DateTime.MinValue)
                {
                    var lastOk = log.LoadRecent(5).FirstOrDefault(r => r.Success);
                    if (lastOk?.FinishedAt is { } finished && finished > DateTime.MinValue)
                    {
                        _lastProcessedWriteUtc = DateTime.SpecifyKind(finished, DateTimeKind.Utc);
                        _logger.LogInformation(
                            "1C auto-sync: restored stamp from last success {Finished:u}",
                            _lastProcessedWriteUtc);
                    }
                }

                var dueBySchedule = nowLocal.TimeOfDay >= NightSyncLocalTime
                                    && _lastNightSyncLocalDate != todayLocal;
                var dueByNewFile = newest is not null
                                   && newest.LastWriteTimeUtc > _lastProcessedWriteUtc.AddSeconds(1);

                // Суточный бэкап всех данных (риски/СОУТ/медосмотры/обучение…) —
                // до импорта 1С, один раз в календарный день по Екатеринбургу.
                if (dueBySchedule && _lastDailyBackupLocalDate != todayLocal)
                {
                    try
                    {
                        var bak = _backup.CreateDailyBackup(nowLocal);
                        if (bak.Success)
                        {
                            _lastDailyBackupLocalDate = todayLocal;
                            _logger.LogInformation(
                                "Daily App_Data backup OK: {Zip} ({Files} files, {SizeMb:0.0} MB)",
                                Path.GetFileName(bak.ZipPath),
                                bak.FileCount,
                                bak.SizeBytes / (1024d * 1024d));
                        }
                        else
                        {
                            _logger.LogWarning("Daily App_Data backup failed: {Error}", bak.Error);
                        }
                    }
                    catch (Exception bakEx)
                    {
                        _logger.LogError(bakEx, "Daily App_Data backup threw");
                    }
                }

                if (sync.SharePathMissing)
                {
                    if (DateTime.UtcNow - _lastMissingPathLogUtc >= TimeSpan.FromHours(1))
                    {
                        _lastMissingPathLogUtc = DateTime.UtcNow;
                        _logger.LogError(
                            "1C SharedPath недоступен: {Path}. Автосинк пропущен. " +
                            "Проверьте SharedPath (OneC) и права пользователя службы.",
                            sync.ConfiguredSharePath);
                    }

                    // Даже без SharedPath отмечаем ночной слот, чтобы бэкап не крутился каждые 5 мин.
                    if (dueBySchedule)
                    {
                        _lastNightSyncLocalDate = todayLocal;
                    }
                }
                else if (dueBySchedule || dueByNewFile)
                {
                    var reason = dueBySchedule && !dueByNewFile
                        ? "daily-4am-ekb"
                        : dueByNewFile && !dueBySchedule
                            ? "share-new-file"
                            : "share-new-file+daily";
                    _logger.LogInformation(
                        "1C sync start ({Reason}), file={File} writeUtc={Write:u}, local={Local:yyyy-MM-dd HH:mm}",
                        reason,
                        newest?.FullName ?? "(none)",
                        newest?.LastWriteTimeUtc,
                        nowLocal);

                    var run = sync.SyncFromShared(reason);
                    if (run.Success)
                    {
                        var batch = OneCEmployeeSyncService.SelectCurrentCsvBatch(sync.FindSharedFiles());
                        if (batch.Count > 0)
                        {
                            _lastProcessedWriteUtc = batch.Max(f => f.LastWriteTimeUtc);
                            SaveStampWriteUtc(
                                _lastProcessedWriteUtc,
                                string.Join(", ", batch.Select(f => f.Name)),
                                run.Message);
                        }
                        else if (newest is not null)
                        {
                            _lastProcessedWriteUtc = newest.LastWriteTimeUtc;
                            SaveStampWriteUtc(_lastProcessedWriteUtc, newest.Name, run.Message);
                        }
                        else
                        {
                            _lastProcessedWriteUtc = DateTime.UtcNow;
                            SaveStampWriteUtc(_lastProcessedWriteUtc, null, run.Message);
                        }

                        if (dueBySchedule)
                        {
                            _lastNightSyncLocalDate = todayLocal;
                        }
                    }

                    _logger.LogInformation("1C sync: {Message}", run.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "1C daily sync tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private string StampPath =>
        Path.Combine(_env.ContentRootPath, "App_Data", StampFileName);

    private DateTime LoadStampWriteUtc()
    {
        try
        {
            var path = StampPath;
            if (!File.Exists(path))
            {
                return DateTime.MinValue;
            }

            var json = File.ReadAllText(path);
            var stamp = System.Text.Json.JsonSerializer.Deserialize<SyncStamp>(json);
            if (stamp?.LastFileWriteUtc is null || stamp.LastFileWriteUtc == DateTime.MinValue)
            {
                return DateTime.MinValue;
            }

            return DateTime.SpecifyKind(stamp.LastFileWriteUtc.Value, DateTimeKind.Utc);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private void SaveStampWriteUtc(DateTime writeUtc, string? fileName, string? message)
    {
        try
        {
            var dir = Path.GetDirectoryName(StampPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var stamp = new SyncStamp
            {
                LastFileWriteUtc = DateTime.SpecifyKind(writeUtc, DateTimeKind.Utc),
                FileName = fileName,
                SyncedAtUtc = DateTime.UtcNow,
                Message = message
            };
            File.WriteAllText(
                StampPath,
                System.Text.Json.JsonSerializer.Serialize(
                    stamp,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "1C auto-sync: failed to save stamp");
        }
    }

    private sealed class SyncStamp
    {
        public DateTime? LastFileWriteUtc { get; set; }
        public string? FileName { get; set; }
        public DateTime? SyncedAtUtc { get; set; }
        public string? Message { get; set; }
    }

    private static TimeZoneInfo ResolveEkaterinburgTimeZone()
    {
        foreach (var id in new[] { "Asia/Yekaterinburg", "Russian Standard Time", "Ekaterinburg Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Yekaterinburg-Fixed",
            TimeSpan.FromHours(5),
            "Yekaterinburg",
            "Yekaterinburg");
    }
}

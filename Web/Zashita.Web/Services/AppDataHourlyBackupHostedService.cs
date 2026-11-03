namespace Zashita.Web.Services;

/// <summary>Раз в час сохраняет снимок всех заполняемых данных (App_Data).</summary>
public sealed class AppDataHourlyBackupHostedService : BackgroundService
{
    private readonly AppDataBackupService _backup;
    private readonly ILogger<AppDataHourlyBackupHostedService> _logger;

    public AppDataHourlyBackupHostedService(
        AppDataBackupService backup,
        ILogger<AppDataHourlyBackupHostedService> logger)
    {
        _backup = backup;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
            try
            {
                // Не сразу после старта — иначе первый бэкап конкурирует с прогревом страниц.
                await Task.Delay(TimeSpan.FromMinutes(45), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = _backup.CreateHourlyBackup();
                    if (!result.Success)
                    {
                        _logger.LogWarning("Hourly App_Data backup skipped/failed: {Error}", result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Hourly App_Data backup tick failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
    }
}

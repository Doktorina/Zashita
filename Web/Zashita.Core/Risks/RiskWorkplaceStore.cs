namespace Zashita.Core.Risks;

/// <summary>
/// Перечень рабочих мест модуля «Риски» — файл risk-workplaces.json,
/// полностью независим от СОУТ (workplaces.json).
/// </summary>
public sealed class RiskWorkplaceStore : WorkplaceStore
{
    public RiskWorkplaceStore(RiskDataPaths paths)
        : base(RequireRiskFile(paths))
    {
    }

    private static string RequireRiskFile(RiskDataPaths paths)
    {
        var file = paths.RiskWorkplacesFile?.Trim() ?? string.Empty;
        if (!file.EndsWith("risk-workplaces.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RiskWorkplaceStore должен использовать risk-workplaces.json, а не workplaces.json СОУТ.");
        }

        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Пустой risk-workplaces.json после разделения хранилищ — один раз подтянуть
        // прежний перечень (workplaces.json), чтобы не потерять заведённые РМ/карты.
        SeedFromLegacyIfEmpty(file, paths.WorkplacesFile);

        if (!File.Exists(file))
        {
            File.WriteAllText(file, "[]");
        }

        return file;
    }

    private static void SeedFromLegacyIfEmpty(string riskFile, string? legacyFile)
    {
        try
        {
            var needsSeed = !File.Exists(riskFile) ||
                            new FileInfo(riskFile).Length < 8 ||
                            string.Equals(
                                File.ReadAllText(riskFile).Trim(),
                                "[]",
                                StringComparison.Ordinal);
            if (!needsSeed || string.IsNullOrWhiteSpace(legacyFile) || !File.Exists(legacyFile))
            {
                return;
            }

            if (new FileInfo(legacyFile).Length < 8)
            {
                return;
            }

            File.Copy(legacyFile, riskFile, overwrite: true);
        }
        catch
        {
            // Не блокируем старт: при ошибке останется пустой перечень.
        }
    }
}

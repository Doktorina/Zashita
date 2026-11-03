namespace Zashita.Web.Services;

/// <summary>
/// Главная страница модуля = отчёт состояния по блоку («Отчет по медосмотрам» и т.п.).
/// </summary>
public static class ModuleHomeReports
{
    public static readonly IReadOnlyDictionary<string, ModuleHomeReport> BySlug =
        new Dictionary<string, ModuleHomeReport>(StringComparer.OrdinalIgnoreCase)
        {
            ["медосмотры"] = new("/Med/Status", "Отчет по медосмотрам", "Медосмотры"),
            ["обучение"] = new("/Training/Status", "Отчет по обучению", "Обучение"),
            ["риски"] = new("/Risks/Reports/Status", "Отчет по рискам", "Риски"),
            ["специальная-оценка"] = new("/Sout/Status", "Отчет по СОУТ", "Специальная оценка")
        };

    public static bool TryGet(string? slug, out ModuleHomeReport report)
    {
        report = default!;
        return !string.IsNullOrWhiteSpace(slug) && BySlug.TryGetValue(slug.Trim(), out report!);
    }

    public static string SectionsUrl(string slug) => $"/module/{Uri.EscapeDataString(slug)}";
}

public sealed record ModuleHomeReport(string Url, string Title, string ModuleName);

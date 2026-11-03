namespace Zashita.Core.Med;

/// <summary>Статусы медосмотра для отчётов и планирования.</summary>
public static class MedExamStatuses
{
    public const string Passed = "Пройден";
    public const string Planned = "Запланирован";
    public const string NotPassed = "Не пройден";
    /// <summary>Осмотр был пройден, но срок действия истёк.</summary>
    public const string Overdue = "Просрочено";

    /// <summary>Устаревший алиас фильтра (закладки / старые ссылки).</summary>
    public const string OverdueLegacy = "Просрочен";

    public static readonly string[] ReportFilterOptions =
        [Passed, Planned, NotPassed, Overdue];

    public static bool IsOverdueFilter(string? filter) =>
        string.Equals(filter, Overdue, StringComparison.OrdinalIgnoreCase)
        || string.Equals(filter, OverdueLegacy, StringComparison.OrdinalIgnoreCase);
}

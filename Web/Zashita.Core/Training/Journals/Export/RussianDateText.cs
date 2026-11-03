namespace Zashita.Core.Training.Journals.Export;

public static class RussianDateText
{
    private static readonly string[] Months =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];

    /// <summary>18 мая 2026 г.</summary>
    public static string FullCalendar(DateTime date)
    {
        var d = date.Date;
        return $"{d.Day:00} {Months[d.Month - 1]} {d.Year} г.";
    }

    public static string FullCalendarWithTime(DateTime local) =>
        $"{FullCalendar(local)} {local:HH:mm}";

    /// <summary>22.11.2033</summary>
    public static string Short(DateTime date) => date.Date.ToString("dd.MM.yyyy");

    public static string Short(DateTime? date) => date is { } d ? Short(d) : string.Empty;
}


namespace Zashita.Core.Training;

public static class TrainingConductValues
{
    public const string NotTrainingResult = "Не обучаем";

    public static bool IsNotTraining(string? result)
    {
        var r = (result ?? string.Empty).Trim();
        return r.Equals(NotTrainingResult, StringComparison.OrdinalIgnoreCase)
               || r.Contains("не обучаем", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOneTimePeriodicity(string? periodicity)
    {
        var p = (periodicity ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (p.Length == 0)
        {
            return false;
        }

        return p is "1 раз" or "однократно"
               || p.Contains("единораз", StringComparison.Ordinal)
               || p.Contains("при приеме", StringComparison.Ordinal)
               || p.Contains("при приёме", StringComparison.Ordinal)
               || p.Contains("при поступлен", StringComparison.Ordinal)
               || p.Contains("по необходимости", StringComparison.Ordinal);
    }

    /// <summary>Комментарий явно связан с заявкой на обучение, а не просто заметка.</summary>
    public static bool LooksLikeApplicationComment(string? comment)
    {
        var c = (comment ?? string.Empty).Trim();
        if (c.Length == 0)
        {
            return false;
        }

        return c.Contains("заявк", StringComparison.OrdinalIgnoreCase)
               || c.Contains("Планирование:", StringComparison.OrdinalIgnoreCase)
               || c.StartsWith("Создано:", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeProtocolMovement(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v) || IsBlankMarker(v))
        {
            return string.Empty;
        }

        if (v.Contains("подпис", StringComparison.OrdinalIgnoreCase))
        {
            return "на подписи";
        }

        if (v.Contains("готов", StringComparison.OrdinalIgnoreCase))
        {
            return "готов";
        }

        return v;
    }

    public static string NormalizeUpdStatus(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v) || IsBlankMarker(v))
        {
            return string.Empty;
        }

        if (v.Contains("нет", StringComparison.OrdinalIgnoreCase))
        {
            return "УПД нет";
        }

        if (v.Contains("есть", StringComparison.OrdinalIgnoreCase))
        {
            return "УПД есть";
        }

        return v;
    }

    /// <summary>«—» / «-» / «–» — пустое значение в селектах отчёта.</summary>
    public static bool IsBlankMarker(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        return v is "—" or "-" or "–" or "−" or "‒";
    }
}

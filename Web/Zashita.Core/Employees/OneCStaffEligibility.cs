namespace Zashita.Core.Employees;

/// <summary>
/// Правила отбора актуального штата из выгрузки 1С (ETL), см. deploy/1C-ETL-ШТАТ.md.
/// Источник: Employees_zup.csv + Employees_zup_anzhela.csv.
/// Главное: последняя запись по [человек + ЮЛ], активность только по датам (не по тексту статуса).
/// </summary>
public static class OneCStaffEligibility
{
    /// <summary>
    /// Текст статуса содержит «увол» — подсказка для отображения.
    /// На попадание в штат не влияет: решают даты.
    /// </summary>
    public static bool IsDismissedStatusText(string? status)
    {
        var s = NormalizeStatus(status);
        return s.Contains("увол", StringComparison.Ordinal);
    }

    /// <summary>
    /// Статусы «похоже на работу» (декрет и т.п.) — для бейджей/отчётов.
    /// Пустой статус при отсутствии даты увольнения считаем активным.
    /// </summary>
    public static bool IsOnStaffStatus(string? status)
    {
        if (IsDismissedStatusText(status))
        {
            return false;
        }

        // Пусто / любой не-увольнительный текст — не блокируем штат (даты важнее).
        return true;
    }

    /// <summary>Дата увольнения пустая или позже сегодня.</summary>
    public static bool IsOpenByDismissalDate(DateTime? dismissalDate, DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        return dismissalDate is null || dismissalDate.Value.Date > today;
    }

    /// <summary>Дата приёма обязательна и уже наступила.</summary>
    public static bool IsHiredByEmploymentDate(DateTime? employmentDate, DateTime? asOf = null)
    {
        if (employmentDate is null)
        {
            return false;
        }

        var today = (asOf ?? DateTime.Today).Date;
        return employmentDate.Value.Date <= today;
    }

    /// <summary>
    /// Активность строки только по датам (статус CSV игнорируем — часто мусор).
    /// </summary>
    public static bool IsActiveByDates(
        DateTime? dismissalDate,
        DateTime? employmentDate,
        DateTime? asOf = null) =>
        IsHiredByEmploymentDate(employmentDate, asOf)
        && IsOpenByDismissalDate(dismissalDate, asOf);

    /// <summary>Строка выгрузки = актуальное место работы (даты).</summary>
    public static bool IsActiveCsvRow(
        string? hrStatus,
        DateTime? dismissalDate,
        DateTime? employmentDate,
        DateTime? asOf = null)
    {
        _ = hrStatus; // текст статуса не решающий
        return IsActiveByDates(dismissalDate, employmentDate, asOf);
    }

    public static bool IsEmployedRecord(OrganizationEmployee employee, DateTime? asOf = null)
    {
        if (employee is null || !employee.IsActive)
        {
            return false;
        }

        return IsActiveByDates(employee.DismissalDate, employee.EmploymentDate, asOf);
    }

    /// <summary>СНИЛС из 11 цифр после нормализации.</summary>
    public static bool IsValidSnils(string? snils)
    {
        var digits = OneCEmployeeListParser.NormalizeSnils(snils);
        return digits.Length == 11;
    }

    /// <summary>
    /// Стабильный ключ человека: СНИЛС (11 цифр) либо ФИО_без_пробелов + ДатаРождения.
    /// </summary>
    public static string PersonStableKey(string? snils, string? fullName, DateTime? birthDate)
    {
        var digits = OneCEmployeeListParser.NormalizeSnils(snils);
        if (digits.Length == 11)
        {
            return "s:" + digits;
        }

        var fio = string.Concat((fullName ?? string.Empty)
                .Where(c => !char.IsWhiteSpace(c)))
            .ToLowerInvariant()
            .Replace('ё', 'е');
        var birth = birthDate is { Year: >= 1901 }
            ? birthDate.Value.ToString("yyyyMMdd")
            : string.Empty;
        return "n:" + fio + "_" + birth;
    }

    public static string PersonStableKey(OneCEmployeeRow row) =>
        PersonStableKey(row.Snils, row.Name, row.BirthDate);

    public static string PersonStableKey(OrganizationEmployee employee) =>
        PersonStableKey(employee.Snils, employee.Name, employee.BirthDate);

    /// <summary>
    /// Сравнение «свежести» двух строк одного человека в одном ЮЛ.
    /// Приоритет: ДатаПриема → при том же приёме строка С увольнением полнее пустой →
    /// ДатаПеревода → среди открытых пустая ДатаУвольнения выше прошлой.
    /// </summary>
    public static int CompareFreshness(OneCEmployeeRow left, OneCEmployeeRow right)
    {
        var hireCmp = (left.EmploymentDate ?? DateTime.MinValue)
            .CompareTo(right.EmploymentDate ?? DateTime.MinValue);
        if (hireCmp != 0)
        {
            return hireCmp;
        }

        // Тот же приём в двух CSV: «уволен 12.07.2024» побеждает «Работа» без даты.
        var leftHasFire = left.DismissalDate is not null;
        var rightHasFire = right.DismissalDate is not null;
        if (leftHasFire != rightHasFire)
        {
            return leftHasFire ? 1 : -1;
        }

        var transferCmp = (left.TransferDate ?? DateTime.MinValue)
            .CompareTo(right.TransferDate ?? DateTime.MinValue);
        if (transferCmp != 0)
        {
            return transferCmp;
        }

        if (leftHasFire && rightHasFire)
        {
            return left.DismissalDate!.Value.CompareTo(right.DismissalDate!.Value);
        }

        // Оба без увольнения — равноценны.
        return 0;
    }

    /// <summary>
    /// Ключ места работы для дедупа совместительств (если в одном ЮЛ несколько актуальных —
    /// после выбора «последней» записи обычно одна).
    /// </summary>
    public static string EmploymentDedupeKey(string organization, OrganizationEmployee employee)
    {
        var person = PersonStableKey(employee);
        var profession = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var department = ProfessionMatcher.Normalize(employee.Department ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var employmentType = OneCStaffNormalize.EmploymentType(employee.EmploymentType)
            .Trim()
            .ToLowerInvariant();
        var org = OrganizationMatcher.Normalize(organization);
        return $"{person}|{org}|{profession}|{department}|{employmentType}";
    }

    public static string EmploymentDedupeKey(string organization, OneCEmployeeRow row)
    {
        var person = PersonStableKey(row);
        var profession = ProfessionMatcher.Normalize(row.Profession ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var department = ProfessionMatcher.Normalize(row.Department ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var employmentType = OneCStaffNormalize.EmploymentType(row.EmploymentType)
            .Trim()
            .ToLowerInvariant();
        var org = OrganizationMatcher.Normalize(organization);
        return $"{person}|{org}|{profession}|{department}|{employmentType}";
    }

    public static DateTime RankDate(OrganizationEmployee employee) =>
        MaxDate(employee.EmploymentDate, employee.TransferDate) ?? DateTime.MinValue;

    public static DateTime RankDate(OneCEmployeeRow row) =>
        MaxDate(row.EmploymentDate, row.TransferDate) ?? DateTime.MinValue;

    private static DateTime? MaxDate(DateTime? a, DateTime? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return a.Value >= b.Value ? a : b;
    }

    private static string NormalizeStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

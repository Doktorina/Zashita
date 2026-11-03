namespace Zashita.Core.Employees;

/// <summary>Нормализация статуса сотрудника из 1С для подсветки в отчётах.</summary>
public static class EmployeeHrStatus
{
    public static string Normalize(string? raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        // «Работа» без доп. пометок — обычный статус, не показываем в бейджах отчётов.
        if (string.Equals(s, "Работа", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "Работает", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (s.Contains("увол", StringComparison.OrdinalIgnoreCase))
        {
            return "Увольнение";
        }

        return s;
    }

    /// <summary>Текст статуса для карточки сотрудника (в т.ч. «Работа» из 1С).</summary>
    public static string ForDisplay(string? stored, bool isActive = true)
    {
        var s = (stored ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return isActive ? "Работа" : string.Empty;
    }

    public static bool IsDismissed(string? status) =>
        string.Equals(Normalize(status), "Увольнение", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// В штате по выгрузке 1С: «Работа» / «Работает» и отпуск по уходу / декрет
    /// (место работы сохраняется). Увольнение и прочие статусы — нет.
    /// </summary>
    public static bool IsWorkingStatus(string? status) =>
        OneCStaffEligibility.IsOnStaffStatus(status);

    /// <summary>То же, что <see cref="IsWorkingStatus"/> — актуальное место работы.</summary>
    public static bool IsOnStaffStatus(string? status) =>
        OneCStaffEligibility.IsOnStaffStatus(status);

    public static bool IsEmployed(OrganizationEmployee employee) =>
        OneCStaffEligibility.IsEmployedRecord(employee);

    public static bool IsAbsence(string? status)
    {
        var s = Normalize(status);
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        return s.Contains("отпуск", StringComparison.OrdinalIgnoreCase)
               || s.Contains("больнич", StringComparison.OrdinalIgnoreCase)
               || s.Contains("уход за ребен", StringComparison.OrdinalIgnoreCase)
               || s.Contains("уход за ребён", StringComparison.OrdinalIgnoreCase)
               || s.Contains("декрет", StringComparison.OrdinalIgnoreCase)
               || s.Contains("нетрудоспособ", StringComparison.OrdinalIgnoreCase);
    }

    public static string CssClass(string? status) =>
        IsAbsence(status) ? "emp-hr-absence" : "emp-hr-status";

    /// <summary>Карта «ФИО → статус из 1С» для подсветки в отчётах.</summary>
    public static Dictionary<string, string> BuildMap(IEnumerable<OrganizationEmployee> employees)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in employees)
        {
            var name = e.Name?.Trim() ?? string.Empty;
            var hr = Normalize(e.HrStatus);
            if (name.Length == 0 || hr.Length == 0)
            {
                continue;
            }

            map[name] = hr;
        }

        return map;
    }
}

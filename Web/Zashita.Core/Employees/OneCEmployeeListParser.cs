using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Zashita.Core.Employees;

/// <summary>
/// Парсер отчёта 1С «Личные данные сотрудников» / «Список работников организации».
/// Контракт колонок (если есть): Сотрудник/ФИО, Должность/Профессия, Подразделение,
/// СНИЛС, Дата рождения, Дата приема, Дата увольнения, Код/GUID.
/// </summary>
public static class OneCEmployeeListParser
{
    private static readonly Regex QuotedOrgRegex = new(
        @"Организация\s+Равно\s+[""«](?<org>[^""»]+)[""»]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SnilsDigits = new(@"\d+", RegexOptions.Compiled);

    public static bool TryParse(
        DataTable table,
        out string? organization,
        out IReadOnlyList<OneCEmployeeRow> employees)
    {
        organization = null;
        employees = [];

        if (table.Rows.Count == 0)
        {
            return false;
        }

        var headerRowIndex = -1;
        var employeeColumn = -1;
        var professionColumn = -1;
        var departmentColumn = -1;
        var snilsColumn = -1;
        var birthColumn = -1;
        var hireColumn = -1;
        var fireColumn = -1;
        var transferColumn = -1;
        var statusColumn = -1;
        var employmentTypeColumn = -1;
        var externalIdColumn = -1;
        var addressColumn = -1;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var colIndex = 0; colIndex < table.Columns.Count; colIndex++)
            {
                var text = GetCellText(row, colIndex);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (organization is null)
                {
                    organization = TryExtractOrganization(text, row, colIndex);
                }

                if (IsEmployeeHeader(text))
                {
                    headerRowIndex = rowIndex;
                    employeeColumn = colIndex;
                }

                if (IsProfessionHeader(text))
                {
                    professionColumn = colIndex;
                }

                if (IsDepartmentHeader(text))
                {
                    departmentColumn = colIndex;
                }

                if (IsSnilsHeader(text))
                {
                    snilsColumn = colIndex;
                }

                if (IsBirthHeader(text))
                {
                    birthColumn = colIndex;
                }

                if (IsFireHeader(text))
                {
                    fireColumn = colIndex;
                }
                else if (IsHireHeader(text))
                {
                    hireColumn = colIndex;
                }

                if (IsTransferHeader(text))
                {
                    transferColumn = colIndex;
                }

                if (IsStatusHeader(text))
                {
                    statusColumn = colIndex;
                }

                if (IsEmploymentTypeHeader(text))
                {
                    employmentTypeColumn = colIndex;
                }

                if (IsExternalIdHeader(text))
                {
                    externalIdColumn = colIndex;
                }

                if (IsAddressHeader(text))
                {
                    addressColumn = colIndex;
                }
            }
        }

        if (headerRowIndex < 0 || employeeColumn < 0)
        {
            return organization is not null;
        }

        var list = new List<OneCEmployeeRow>();
        for (var rowIndex = headerRowIndex + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var name = GetCellText(row, employeeColumn);
            if (!IsValidEmployeeName(name))
            {
                continue;
            }

            var profession = professionColumn >= 0
                ? GetCellText(row, professionColumn)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(profession))
            {
                profession = "Сотрудники";
            }

            var fireDate = fireColumn >= 0
                ? ParseDate(GetCellText(row, fireColumn))
                : null;
            var status = statusColumn >= 0 ? GetCellText(row, statusColumn) : "";
            if (fireDate is not null && string.IsNullOrWhiteSpace(status))
            {
                status = "Увольнение";
            }

            list.Add(new OneCEmployeeRow(
                name,
                profession,
                departmentColumn >= 0 ? GetCellText(row, departmentColumn) : "",
                NormalizeSnils(snilsColumn >= 0 ? GetCellText(row, snilsColumn) : ""),
                externalIdColumn >= 0 ? GetCellText(row, externalIdColumn) : "",
                ParseDate(birthColumn >= 0 ? GetCellText(row, birthColumn) : ""),
                ParseDate(hireColumn >= 0 ? GetCellText(row, hireColumn) : ""),
                addressColumn >= 0 ? GetCellText(row, addressColumn) : "",
                status,
                ParseDate(transferColumn >= 0 ? GetCellText(row, transferColumn) : ""),
                employmentTypeColumn >= 0 ? GetCellText(row, employmentTypeColumn) : "",
                fireDate));
        }

        employees = list;
        return organization is not null || list.Count > 0;
    }

    public static string NormalizeSnils(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var digits = string.Concat(SnilsDigits.Matches(raw).Select(m => m.Value));
        return digits.Length >= 11 ? digits[..11] : digits;
    }

    /// <summary>Отображение СНИЛС: 204-013-969 10.</summary>
    public static string FormatSnils(string? raw)
    {
        var digits = NormalizeSnils(raw);
        if (digits.Length != 11)
        {
            return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
        }

        return $"{digits[..3]}-{digits[3..6]}-{digits[6..9]} {digits[9..11]}";
    }

    public static string BuildMatchKey(string organization, OneCEmployeeRow row) =>
        BuildMatchKey(
            organization,
            row.Snils,
            row.ExternalId,
            row.Name,
            row.Profession,
            row.EmploymentType,
            row.EmploymentDate);

    public static string BuildMatchKey(string organization, OrganizationEmployee employee) =>
        BuildMatchKey(
            organization,
            employee.Snils,
            employee.ExternalId,
            employee.Name,
            employee.Profession,
            employee.EmploymentType,
            employee.EmploymentDate);

    /// <summary>
    /// Ключ трудового отношения: человек + должность + вид занятости + дата приёма.
    /// Один СНИЛС в одной организации может иметь несколько активных записей (совместительство).
    /// </summary>
    public static string BuildMatchKey(
        string organization,
        string snils,
        string externalId,
        string name,
        string? profession = null,
        string? employmentType = null,
        DateTime? employmentDate = null)
    {
        var personKey = BuildPersonKey(organization, snils, externalId, name);
        var professionKey = NormalizeEmploymentKeyPart(profession);
        var employmentTypeKey = NormalizeEmploymentKeyPart(employmentType);
        var hireKey = employmentDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty;
        return $"{personKey}|{professionKey}|{employmentTypeKey}|{hireKey}";
    }

    public static string BuildPersonKey(string organization, string snils, string externalId, string name)
    {
        var snilsNorm = NormalizeSnils(snils);
        if (!string.IsNullOrWhiteSpace(snilsNorm))
        {
            return "snils:" + snilsNorm;
        }

        if (!string.IsNullOrWhiteSpace(externalId))
        {
            return "ext:" + externalId.Trim().ToLowerInvariant();
        }

        return "name:" + organization.Trim().ToLowerInvariant() + "|" + name.Trim().ToLowerInvariant();
    }

    public static bool IsPrimaryEmployment(string? employmentType)
    {
        var value = (employmentType ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        return string.IsNullOrWhiteSpace(value) || value.Contains("основн", StringComparison.Ordinal);
    }

    private static string NormalizeEmploymentKeyPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // «Мастер участка» и «Мастер участка *» — одно трудовое отношение.
        var normalized = ProfessionMatcher.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = value.Trim();
        }

        var chars = normalized
            .Trim()
            .ToLowerInvariant()
            .Replace('ё', 'е')
            .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            .ToArray();
        return string.Join('_', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? TryExtractOrganization(string text, DataRow row, int colIndex)
    {
        if (string.Equals(text, "Организация", StringComparison.OrdinalIgnoreCase))
        {
            for (var c = colIndex + 1; c < row.Table.Columns.Count; c++)
            {
                var value = GetCellText(row, c);
                if (OrganizationMatcher.LooksLikeOrganization(value))
                {
                    return value;
                }
            }
        }

        var match = QuotedOrgRegex.Match(text.Replace('\n', ' ').Replace('\r', ' '));
        if (match.Success)
        {
            return match.Groups["org"].Value.Trim();
        }

        if (OrganizationMatcher.LooksLikeOrganization(text))
        {
            return text.Trim();
        }

        return null;
    }

    private static bool IsEmployeeHeader(string text) =>
        string.Equals(text, "Сотрудник", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "Работник", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "ФИО", StringComparison.OrdinalIgnoreCase);

    private static bool IsProfessionHeader(string text) =>
        text.Contains("должность", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("профессия", StringComparison.OrdinalIgnoreCase);

    private static bool IsDepartmentHeader(string text) =>
        text.Contains("подраздел", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("отдел", StringComparison.OrdinalIgnoreCase);

    private static bool IsSnilsHeader(string text) =>
        text.Contains("снилс", StringComparison.OrdinalIgnoreCase);

    private static bool IsBirthHeader(string text) =>
        text.Contains("рождения", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("дата рожд", StringComparison.OrdinalIgnoreCase);

    private static bool IsHireHeader(string text) =>
        !IsFireHeader(text) &&
        (text.Contains("приема", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("приёма", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("принят", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("трудоустрой", StringComparison.OrdinalIgnoreCase));

    private static bool IsFireHeader(string text) =>
        text.Contains("увольнен", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("дата увол", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("датаувольн", StringComparison.OrdinalIgnoreCase) ||
        (text.Contains("увол", StringComparison.OrdinalIgnoreCase) &&
         text.Contains("дата", StringComparison.OrdinalIgnoreCase));

    private static bool IsTransferHeader(string text) =>
        text.Contains("перевод", StringComparison.OrdinalIgnoreCase);

    private static bool IsStatusHeader(string text) =>
        string.Equals(text.Trim(), "Статус", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("статус", StringComparison.OrdinalIgnoreCase);

    private static bool IsEmploymentTypeHeader(string text)
    {
        var t = text.Trim();
        return t.Contains("вид занятости", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("занятост", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("совмещ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExternalIdHeader(string text)
    {
        var t = text.Trim();
        return string.Equals(t, "GUID", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(t, "Код", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("идентификатор", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("уид", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("ref", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAddressHeader(string text)
    {
        var t = text.Trim();
        if (t.Contains("электрон", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("e-mail", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("email", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("места работы", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("место работы", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return t.Contains("домашний адрес", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("адрес проживания", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("адрес регистрации", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("адрес места жительства", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(t, "Адрес", StringComparison.OrdinalIgnoreCase) ||
               t.StartsWith("Адрес ", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var ru))
        {
            return ru.Date;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any)
            ? any.Date
            : null;
    }

    private static string GetCellText(DataRow row, int colIndex)
    {
        if (colIndex < 0 || colIndex >= row.Table.Columns.Count)
        {
            return string.Empty;
        }

        return row[colIndex]?.ToString()?.Trim() ?? string.Empty;
    }

    private static bool IsValidEmployeeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !name.Contains("не указан", StringComparison.OrdinalIgnoreCase) &&
        !IsEmployeeHeader(name) &&
        !string.Equals(name, "Организация", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, "Количество", StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith("Отбор", StringComparison.OrdinalIgnoreCase) &&
        !OrganizationMatcher.LooksLikeOrganization(name);
}

public sealed record OneCEmployeeRow(
    string Name,
    string Profession,
    string Department = "",
    string Snils = "",
    string ExternalId = "",
    DateTime? BirthDate = null,
    DateTime? EmploymentDate = null,
    string HomeAddress = "",
    string HrStatus = "",
    DateTime? TransferDate = null,
    string EmploymentType = "",
    DateTime? DismissalDate = null);

using System.Text;

namespace Zashita.Core.Employees;

/// <summary>
/// Парсер суточной выгрузки 1С ЗУП в CSV:
/// ФИО;ДатаРождения;Должность;Подразделение;ЮрЛицо;ДатаПриема;ДатаУвольнения;ДатаПеревода;Статус;ВидЗанятости;СНИЛС;АдресРегистрации
/// </summary>
public static class OneCEmployeesCsvParser
{
    public static Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>> ParseFiles(
        IEnumerable<string> filePaths)
    {
        var result = new Dictionary<string, List<(OneCEmployeeRow, bool)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            foreach (var (org, row, active) in ParseFile(path))
            {
                if (!result.TryGetValue(org, out var list))
                {
                    list = [];
                    result[org] = list;
                }

                list.Add((row, active));
            }
        }

        return result;
    }

    public static IEnumerable<(string Organization, OneCEmployeeRow Row, bool IsActive)> ParseFile(string filePath)
    {
        using var reader = new StreamReader(filePath, DetectEncoding(filePath));
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            yield break;
        }

        var headers = SplitCsvLine(headerLine)
            .Select(h => h.Trim().Trim('"').ToLowerInvariant())
            .ToList();

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            map[headers[i]] = i;
        }

        int Col(params string[] names)
        {
            foreach (var name in names)
            {
                if (map.TryGetValue(name.ToLowerInvariant(), out var idx))
                {
                    return idx;
                }
            }

            return -1;
        }

        var iName = Col("фио", "сотрудник");
        var iBirth = Col("датарождения", "дата рождения");
        var iProf = Col("должность", "профессия");
        var iDept = Col("подразделение");
        var iOrg = Col("юрлицо", "организация", "юридическое лицо");
        var iHire = Col("датаприема", "дата приема", "датаприёма");
        var iFire = Col("датаувольнения", "дата увольнения");
        var iTransfer = Col("датаперевода", "дата перевода");
        var iStatus = Col("статус");
        var iEmploymentType = Col("видзанятости", "вид занятости", "занятость");
        var iSnils = Col("снилс");
        var iAddress = Col(
            "домашнийадрес",
            "домашний адрес",
            "адреспроживания",
            "адрес проживания",
            "адресрегистрации",
            "адрес регистрации",
            "адрес");

        if (iName < 0 || iOrg < 0)
        {
            yield break;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = SplitCsvLine(line);
            var name = Get(cells, iName);
            var orgRaw = Get(cells, iOrg);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(orgRaw))
            {
                continue;
            }

            var org = NormalizeOrganizationName(orgRaw);
            if (string.IsNullOrWhiteSpace(org))
            {
                continue;
            }

            var nameNorm = OneCStaffNormalize.PersonName(name);
            var status = OneCStaffNormalize.HrStatus(Get(cells, iStatus));
            var fireDate = OneCStaffNormalize.ParseDismissalDate(Get(cells, iFire));
            var transferDate = OneCStaffNormalize.ParseDate(Get(cells, iTransfer));
            var hireDate = OneCStaffNormalize.ParseDate(Get(cells, iHire));
            var employmentType = OneCStaffNormalize.EmploymentType(Get(cells, iEmploymentType));
            // Предварительно по датам; финал — после выбора «последней» записи по ЮЛ.
            var isActive = OneCStaffEligibility.IsActiveByDates(fireDate, hireDate);

            var profession = Get(cells, iProf);
            if (string.IsNullOrWhiteSpace(profession))
            {
                profession = "Сотрудники";
            }

            yield return (
                org,
                new OneCEmployeeRow(
                    nameNorm,
                    profession,
                    Get(cells, iDept),
                    OneCEmployeeListParser.NormalizeSnils(Get(cells, iSnils)),
                    "",
                    OneCStaffNormalize.ParseDate(Get(cells, iBirth)),
                    hireDate,
                    Get(cells, iAddress),
                    status,
                    transferDate,
                    employmentType,
                    fireDate),
                isActive);
        }
    }

    public static string NormalizeOrganizationName(string raw)
    {
        var value = (raw ?? string.Empty).Trim().Trim('"').Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        value = value.Replace("\"\"", "\"", StringComparison.Ordinal).Trim().Trim('"').Trim();
        value = value.Replace("«", "\"", StringComparison.Ordinal).Replace("»", "\"", StringComparison.Ordinal);

        // В 1С два разных ярлыка:
        // «КСК ООО» → ООО «КСК» (~3 чел.);
        // «ООО "КСК"» → ООО «Компания Строй Каркас».
        if (OrganizationLegalEntitySplit.IsCsvRealKskLabel(value))
        {
            return OrganizationLegalEntitySplit.KskOrganization;
        }

        if (OrganizationLegalEntitySplit.IsCsvOooKskAsKarkasLabel(value))
        {
            return OrganizationLegalEntitySplit.StroyKarkasOrganization;
        }

        if (value.StartsWith("ООО ", StringComparison.OrdinalIgnoreCase))
        {
            var body = value[4..].Trim().Trim('"').Trim();
            value = string.IsNullOrWhiteSpace(body) ? "ООО" : $"ООО «{body}»";
        }
        else if (value.EndsWith(" ООО", StringComparison.OrdinalIgnoreCase) &&
                 !value.StartsWith("ООО", StringComparison.OrdinalIgnoreCase))
        {
            var body = value[..^4].Trim().Trim('"').Trim();
            value = $"ООО «{body}»";
        }
        else if (value.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length > 2 && value[2] != ' ')
            {
                value = "ИП " + value[2..].TrimStart();
            }
        }

        return value;
    }

    private static string Get(IReadOnlyList<string> cells, int index) =>
        index >= 0 && index < cells.Count ? cells[index].Trim().Trim('"').Trim() : string.Empty;

    private static Encoding DetectEncoding(string path)
    {
        var bom = new byte[3];
        using (var fs = File.OpenRead(path))
        {
            _ = fs.Read(bom, 0, 3);
        }

        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        // Без BOM: пробуем UTF-8; если заголовок не читается как русские колонки 1С — Windows-1251.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        try
        {
            using var reader = new StreamReader(path, utf8, detectEncodingFromByteOrderMarks: false);
            var header = reader.ReadLine() ?? string.Empty;
            var lower = header.ToLowerInvariant();
            if (lower.Contains("фио", StringComparison.Ordinal)
                || lower.Contains("юрлицо", StringComparison.Ordinal)
                || lower.Contains("сотрудник", StringComparison.Ordinal)
                || lower.Contains("датаувольнения", StringComparison.Ordinal)
                || lower.Contains("дата увольнения", StringComparison.Ordinal))
            {
                return utf8;
            }
        }
        catch
        {
            // fall through
        }

        return Encoding.GetEncoding(1251);
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == ';' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        result.Add(sb.ToString());
        return result;
    }
}

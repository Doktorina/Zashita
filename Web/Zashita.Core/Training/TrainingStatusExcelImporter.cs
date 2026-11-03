using System.Globalization;
using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Zashita.Core.Employees;

namespace Zashita.Core.Training;

public sealed class TrainingStatusExcelImportResult
{
    public int UpdatedRows { get; set; }
    public int CreatedRows { get; set; }
    public int SkippedRows { get; set; }
    public int PreservedFilledRows { get; set; }
    public List<string> Warnings { get; } = [];

    public string FormatMessage()
    {
        var sb = new StringBuilder();
        sb.Append($"Импорт выгрузки: обновлено — {UpdatedRows}, создано — {CreatedRows}");
        if (SkippedRows > 0)
        {
            sb.Append($", пропущено — {SkippedRows}");
        }

        if (PreservedFilledRows > 0)
        {
            sb.Append($", уже заполненные не трогали — {PreservedFilledRows}");
        }

        sb.Append('.');
        if (Warnings.Count > 0)
        {
            sb.Append(' ');
            sb.Append(string.Join("; ", Warnings.Take(8)));
            if (Warnings.Count > 8)
            {
                sb.Append($" … (+{Warnings.Count - 8})");
            }

            sb.Append('.');
        }

        return sb.ToString();
    }
}

/// <summary>
/// Импорт файла «Скачать Excel» (отчёт / архив обучения) обратно в проведения.
/// Формат совпадает с <see cref="TrainingStatusExcelExporter"/>.
/// </summary>
public static class TrainingStatusExcelImporter
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static bool LooksLikeStatusExport(Stream stream, string? fileName = null)
    {
        try
        {
            stream.Position = 0;
            using var workbook = OpenWorkbook(stream, fileName);
            var sheet = workbook.GetSheetAt(0);
            if (sheet is null)
            {
                return false;
            }

            var header = sheet.GetRow(0);
            if (header is null)
            {
                return false;
            }

            var map = BuildHeaderMap(header);
            return map.ContainsKey("fio") && map.ContainsKey("program") && map.ContainsKey("protocol");
        }
        catch
        {
            return false;
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }
        }
    }

    public static TrainingStatusExcelImportResult Import(
        Stream stream,
        string? fileName,
        string targetOrganization,
        IReadOnlyList<TrainingStatusRow> reportRows,
        IReadOnlyList<OrganizationEmployee> employees,
        TrainingConductStore conduct,
        bool forArchive,
        bool overwriteExisting = false)
    {
        var result = new TrainingStatusExcelImportResult();
        var org = (targetOrganization ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org))
        {
            result.Warnings.Add("Не выбрана организация.");
            return result;
        }

        stream.Position = 0;
        using var workbook = OpenWorkbook(stream, fileName);
        var sheet = workbook.GetSheetAt(0);
        if (sheet is null)
        {
            result.Warnings.Add("В файле нет листов.");
            return result;
        }

        var header = sheet.GetRow(0);
        if (header is null)
        {
            result.Warnings.Add("Нет строки заголовков.");
            return result;
        }

        var cols = BuildHeaderMap(header);
        if (!cols.ContainsKey("fio") || !cols.ContainsKey("program"))
        {
            result.Warnings.Add("Не найдены колонки «ФИО» / «Программа» — это не выгрузка отчёта.");
            return result;
        }

        var employedNames = employees
            .Where(EmployeeHrStatus.IsEmployed)
            .Select(e => (e.Name ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var list = conduct.LoadAll();
        var reportByKey = reportRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Person) && !string.IsNullOrWhiteSpace(r.Program))
            .GroupBy(r => PersonProgramKey(r.Person, r.Profession, r.Program), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (var r = 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            var person = Cell(row, cols, "fio");
            var program = Cell(row, cols, "program");
            if (string.IsNullOrWhiteSpace(person) || string.IsNullOrWhiteSpace(program))
            {
                result.SkippedRows++;
                continue;
            }

            var rowOrg = Cell(row, cols, "organization");
            if (!string.IsNullOrWhiteSpace(rowOrg)
                && !OrganizationMatchesForImport(rowOrg, org))
            {
                result.SkippedRows++;
                continue;
            }

            var professionRaw = Cell(row, cols, "profession");
            var (professionFromFile, objectFromProfession) =
                ProfessionMatcher.SplitProfessionAndObject(professionRaw);
            var profession = professionFromFile;
            var department = Cell(row, cols, "department");
            var protocol = Cell(row, cols, "protocol");
            var trainingDate = ParseDate(Cell(row, cols, "trainingDate"));
            var nextDate = ParseDate(Cell(row, cols, "nextDate"));
            var movement = TrainingConductValues.NormalizeProtocolMovement(Cell(row, cols, "movement"));
            var upd = TrainingConductValues.NormalizeUpdStatus(Cell(row, cols, "upd"));
            var comment = Cell(row, cols, "comment");
            var objectNote = Cell(row, cols, "object");
            if (string.IsNullOrWhiteSpace(objectNote) && !string.IsNullOrWhiteSpace(objectFromProfession))
            {
                objectNote = objectFromProfession;
            }

            var transferKind = Cell(row, cols, "transferKind");

            if (string.IsNullOrWhiteSpace(protocol)
                && trainingDate is null
                && string.IsNullOrWhiteSpace(movement)
                && string.IsNullOrWhiteSpace(upd)
                && string.IsNullOrWhiteSpace(comment)
                && string.IsNullOrWhiteSpace(objectNote))
            {
                result.SkippedRows++;
                continue;
            }

            // Статус/трафик из выгрузки не пишем как номер протокола.
            if (LooksLikeStatusLabel(protocol))
            {
                protocol = string.Empty;
            }

            reportByKey.TryGetValue(PersonProgramKey(person, profession, program), out var reportRow);
            if (reportRow is null && !string.IsNullOrWhiteSpace(professionRaw))
            {
                // Ключ мог строиться со скобками — повторный поиск без объекта.
                reportByKey.TryGetValue(PersonProgramKey(person, professionRaw, program), out reportRow);
            }

            var employeeProfessions = employees
                .Where(e => string.Equals(e.Name?.Trim(), person.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(e => (e.Profession ?? string.Empty).Trim())
                .Where(p => p.Length > 0 && p != "—")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var matchedStaffProfession = !string.IsNullOrWhiteSpace(profession)
                ? employeeProfessions.FirstOrDefault(p => ProfessionMatcher.MatchesForImport(p, profession))
                : null;
            // Профессию из файла не совпали — берём штат/отчёт, строку не отбрасываем (иначе «импорт ничего не заполнил»).
            if (!string.IsNullOrWhiteSpace(profession)
                && employeeProfessions.Count > 0
                && matchedStaffProfession is null)
            {
                result.Warnings.Add(
                    $"{person}: профессия в файле «{profession}» → в отчёте «{employeeProfessions[0]}»");
            }
            else if (!string.IsNullOrWhiteSpace(profession)
                     && employeeProfessions.Count == 0
                     && !string.IsNullOrWhiteSpace(reportRow?.Profession)
                     && reportRow!.Profession != "—"
                     && !ProfessionMatcher.MatchesForImport(reportRow.Profession, profession))
            {
                result.Warnings.Add(
                    $"{person}: профессия в файле «{profession}» → в отчёте «{reportRow.Profession}»");
            }

            var resolvedProfession = !string.IsNullOrWhiteSpace(matchedStaffProfession)
                ? matchedStaffProfession!
                : (!string.IsNullOrWhiteSpace(reportRow?.Profession) && reportRow!.Profession != "—"
                    ? reportRow.Profession
                    : (!string.IsNullOrWhiteSpace(employeeProfessions.FirstOrDefault())
                        ? employeeProfessions[0]
                        : (profession ?? string.Empty)));
            var resolvedDepartment = !string.IsNullOrWhiteSpace(department)
                ? department
                : reportRow?.Department ?? string.Empty;

            var wantSnapshot = forArchive
                               && (string.Equals(transferKind, "Перевод", StringComparison.OrdinalIgnoreCase)
                                   || employedNames.Contains(person.Trim()));

            var existing = FindConduct(
                list,
                org,
                person,
                program,
                resolvedProfession,
                reportRow?.ConductId,
                wantSnapshot,
                forArchive);

            var isNew = existing is null;
            existing ??= new TrainingConductRecord { Id = Guid.NewGuid().ToString("N") };

            var fillOnlyEmpty = !overwriteExisting && !isNew;
            var hadIncomingData = !string.IsNullOrWhiteSpace(protocol)
                                  || trainingDate.HasValue
                                  || nextDate.HasValue
                                  || !string.IsNullOrWhiteSpace(movement)
                                  || !string.IsNullOrWhiteSpace(upd)
                                  || !string.IsNullOrWhiteSpace(comment)
                                  || !string.IsNullOrWhiteSpace(objectNote);

            // Без «перезаписать»: уже заполненная строка (протокол или дата) — целиком пропускаем.
            if (fillOnlyEmpty && HasFilledTrainingData(existing))
            {
                if (hadIncomingData)
                {
                    result.PreservedFilledRows++;
                }
                else
                {
                    result.SkippedRows++;
                }

                continue;
            }

            var changed = false;
            void Set(string? next, Action<string> apply, string current, bool onlyIfEmpty = false)
            {
                var value = (next ?? string.Empty).Trim();
                if (onlyIfEmpty && !string.IsNullOrWhiteSpace(current))
                {
                    return;
                }

                if (string.Equals(current ?? string.Empty, value, StringComparison.Ordinal))
                {
                    return;
                }

                apply(value);
                changed = true;
            }

            existing.Organization = org;
            Set(resolvedDepartment, v => existing.Department = v, existing.Department);
            Set(resolvedProfession, v => existing.Profession = v, existing.Profession);
            Set(person, v => existing.Person = v, existing.Person);
            Set(program, v => existing.Program = v, existing.Program);

            if (forArchive && wantSnapshot && !existing.IsArchivedSnapshot)
            {
                existing.IsArchivedSnapshot = true;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(protocol))
            {
                Set(protocol, v =>
                {
                    existing.ProtocolNumber = v;
                    existing.CertificateNumber = v;
                }, existing.ProtocolNumber);
            }

            if (trainingDate.HasValue)
            {
                if (existing.TrainingDate?.Date != trainingDate.Value.Date)
                {
                    existing.TrainingDate = trainingDate.Value.Date;
                    changed = true;
                    if (string.IsNullOrWhiteSpace(existing.Result)
                        || existing.Result.Contains("не проводилось", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.Result = program.Contains("проверк", StringComparison.OrdinalIgnoreCase)
                            ? "Сдал"
                            : "Пройден";
                    }
                }
            }

            if (nextDate.HasValue)
            {
                if (existing.NextTrainingDate?.Date != nextDate.Value.Date)
                {
                    existing.NextTrainingDate = nextDate.Value.Date;
                    changed = true;
                }
            }

            Set(movement, v => existing.ProtocolMovement = v, existing.ProtocolMovement);
            Set(upd, v => existing.UpdStatus = v, existing.UpdStatus);
            Set(comment, v => existing.Comment = v, existing.Comment);
            Set(objectNote, v => existing.ObjectNote = v, existing.ObjectNote);

            if (!changed && !isNew)
            {
                if (fillOnlyEmpty && hadIncomingData)
                {
                    result.PreservedFilledRows++;
                }
                else
                {
                    result.SkippedRows++;
                }

                continue;
            }

            var idx = list.FindIndex(x => string.Equals(x.Id, existing.Id, StringComparison.Ordinal));
            if (idx >= 0)
            {
                list[idx] = existing;
                result.UpdatedRows++;
            }
            else
            {
                list.Add(existing);
                result.CreatedRows++;
            }
        }

        if (result.UpdatedRows > 0 || result.CreatedRows > 0)
        {
            conduct.SaveAll(list);
        }
        else if (result.SkippedRows > 0 && result.Warnings.Count == 0)
        {
            result.Warnings.Add("Подходящих незаполненных строк не найдено.");
        }

        return result;
    }

    private static bool HasFilledTrainingData(TrainingConductRecord record)
    {
        var protocol = (record.ProtocolNumber ?? string.Empty).Trim();
        var certificate = (record.CertificateNumber ?? string.Empty).Trim();
        // Старые выгрузки писали Traffic в колонку протокола — не считаем это «уже заполнено».
        var hasProtocol = (!string.IsNullOrWhiteSpace(protocol) && !LooksLikeStatusLabel(protocol))
                          || (!string.IsNullOrWhiteSpace(certificate) && !LooksLikeStatusLabel(certificate));
        return hasProtocol || record.TrainingDate.HasValue;
    }

    private static TrainingConductRecord? FindConduct(
        List<TrainingConductRecord> list,
        string organization,
        string person,
        string program,
        string profession,
        string? conductId,
        bool wantSnapshot,
        bool forArchive)
    {
        if (!string.IsNullOrWhiteSpace(conductId))
        {
            var byId = list.FirstOrDefault(x => string.Equals(x.Id, conductId, StringComparison.Ordinal));
            if (byId is not null)
            {
                return byId;
            }
        }

        bool Matches(TrainingConductRecord x) =>
            string.Equals(x.Person, person, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Program, program, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(x.Organization)
                || OrganizationMatcher.Matches(x.Organization, organization)
                || string.Equals(x.Organization, organization, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(profession)
                || string.IsNullOrWhiteSpace(x.Profession)
                || string.Equals(x.Profession, profession, StringComparison.OrdinalIgnoreCase)
                || x.Profession == "—"
                || profession == "—");

        if (forArchive && wantSnapshot)
        {
            return list.FirstOrDefault(x => x.IsArchivedSnapshot && Matches(x))
                   ?? list.FirstOrDefault(x => !x.IsArchivedSnapshot && Matches(x));
        }

        if (forArchive)
        {
            return list.FirstOrDefault(x => !x.IsArchivedSnapshot && Matches(x))
                   ?? list.FirstOrDefault(x => x.IsArchivedSnapshot && Matches(x));
        }

        return list.FirstOrDefault(x => !x.IsArchivedSnapshot && Matches(x));
    }

    private static Dictionary<string, int> BuildHeaderMap(IRow header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 0; c < header.LastCellNum; c++)
        {
            var text = NormalizeHeader(GetCellString(header.GetCell(c)));
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Contains("организац", StringComparison.Ordinal))
            {
                map["organization"] = c;
            }
            else if (text.Contains("подраздел", StringComparison.Ordinal))
            {
                map["department"] = c;
            }
            else if (text is "фио" || text.Contains("фамилия", StringComparison.Ordinal))
            {
                map["fio"] = c;
            }
            else if (text.Contains("должност", StringComparison.Ordinal) || text.Contains("професс", StringComparison.Ordinal))
            {
                map["profession"] = c;
            }
            else if (text.Contains("увольнен", StringComparison.Ordinal) && text.Contains("перевод", StringComparison.Ordinal)
                     && !text.Contains("дата", StringComparison.Ordinal))
            {
                map["transferKind"] = c;
            }
            else if (text.Contains("дата увольнен", StringComparison.Ordinal) || text.Contains("дата перевод", StringComparison.Ordinal))
            {
                map["dismissalDate"] = c;
            }
            else if (text.Contains("программ", StringComparison.Ordinal))
            {
                map["program"] = c;
            }
            else if (text.Contains("периодич", StringComparison.Ordinal))
            {
                map["periodicity"] = c;
            }
            else if (text.Contains("номер протокол", StringComparison.Ordinal) || text == "протокол")
            {
                map["protocol"] = c;
            }
            else if (text.Contains("дата обучен", StringComparison.Ordinal))
            {
                map["trainingDate"] = c;
            }
            else if (text.Contains("дата следующ", StringComparison.Ordinal))
            {
                map["nextDate"] = c;
            }
            else if (text.Contains("движен", StringComparison.Ordinal))
            {
                map["movement"] = c;
            }
            else if (text.Contains("упд", StringComparison.Ordinal))
            {
                map["upd"] = c;
            }
            else if (text.Contains("комментар", StringComparison.Ordinal))
            {
                map["comment"] = c;
            }
            else if (text.Contains("объект", StringComparison.Ordinal))
            {
                map["object"] = c;
            }
            else if (text.Contains("статус", StringComparison.Ordinal))
            {
                map["status"] = c;
            }
        }

        return map;
    }

    private static string Cell(IRow row, Dictionary<string, int> cols, string key) =>
        cols.TryGetValue(key, out var idx) ? GetCellString(row.GetCell(idx)) : string.Empty;

    /// <summary>
    /// Совпадение юрлица из Excel с выбранным в шапке (в т.ч. «ИП Шубин» ↔ «ИП Шубин Д.В.»).
    /// </summary>
    private static bool OrganizationMatchesForImport(string rowOrg, string selectedOrg)
    {
        if (string.Equals(rowOrg, selectedOrg, StringComparison.OrdinalIgnoreCase)
            || OrganizationMatcher.Matches(rowOrg, selectedOrg)
            || OrganizationMatcher.Matches(selectedOrg, rowOrg))
        {
            return true;
        }

        var a = (rowOrg ?? string.Empty).Trim();
        var b = (selectedOrg ?? string.Empty).Trim();
        if (!a.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
            || !b.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var nameA = OrganizationMatcher.InferIndividualEntrepreneurEmployee(a);
        var nameB = OrganizationMatcher.InferIndividualEntrepreneurEmployee(b);
        if (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB))
        {
            return false;
        }

        return EmployeeNameMatcher.Matches(nameA, nameB)
               || OrganizationMatcher.IsIndividualEntrepreneurPerson(a, nameB)
               || OrganizationMatcher.IsIndividualEntrepreneurPerson(b, nameA);
    }

    private static string PersonProgramKey(string? person, string? profession, string? program)
    {
        var prof = string.IsNullOrWhiteSpace(profession) ? "—" : profession.Trim();
        return $"{(person ?? string.Empty).Trim()}|{prof}|{(program ?? string.Empty).Trim()}";
    }

    private static bool LooksLikeStatusLabel(string? value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        return v is "не пройдено" or "пройдено" or "просрочено" or "актуально" or "сдал" or "не проводилось"
               or "green" or "yellow" or "red" or "pink" or "blue" or "orange" or "grey" or "gray"
               || v.Contains("не пройд", StringComparison.Ordinal)
               || v.Contains("требует", StringComparison.Ordinal);
    }

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim();
        string[] formats =
        [
            "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy",
            "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"
        ];
        if (DateTime.TryParseExact(t, formats, Ru, DateTimeStyles.None, out var exact))
        {
            return exact.Date;
        }

        // Excel часто отдаёт дату как serial (например 45432) без формата ячейки.
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa)
            || double.TryParse(t, NumberStyles.Float, Ru, out oa))
        {
            if (oa is >= 20000 and <= 80000)
            {
                try
                {
                    return DateTime.FromOADate(oa).Date;
                }
                catch
                {
                    // ignore
                }
            }
        }

        return DateTime.TryParse(t, Ru, DateTimeStyles.None, out var any) ? any.Date : null;
    }

    private static string NormalizeHeader(string? value)
    {
        var t = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        return string.Join(' ', t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static IWorkbook OpenWorkbook(Stream stream, string? fileName)
    {
        // NPOI закрывает переданный Stream при Dispose workbook — всегда открываем с копии.
        var bytes = stream switch
        {
            MemoryStream ms => ms.ToArray(),
            _ => ReadAllBytes(stream)
        };
        var local = new MemoryStream(bytes);
        var name = fileName ?? string.Empty;
        if (name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return new HSSFWorkbook(local);
        }

        return new XSSFWorkbook(local);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek)
        {
            var pos = stream.Position;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            stream.Position = pos;
            return ms.ToArray();
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string GetCellString(ICell? cell)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        switch (cell.CellType)
        {
            case CellType.String:
                return cell.StringCellValue?.Trim() ?? string.Empty;
            case CellType.Numeric:
                return FormatNumericCell(cell);
            case CellType.Boolean:
                return cell.BooleanCellValue ? "да" : "нет";
            case CellType.Formula:
                try
                {
                    var cached = cell.CachedFormulaResultType;
                    if (cached == CellType.Numeric)
                    {
                        return FormatNumericCell(cell);
                    }

                    if (cached == CellType.String)
                    {
                        return cell.StringCellValue?.Trim() ?? string.Empty;
                    }

                    return cell.StringCellValue?.Trim() ?? string.Empty;
                }
                catch
                {
                    try
                    {
                        return FormatNumericCell(cell);
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            default:
                return string.Empty;
        }
    }

    private static string FormatNumericCell(ICell cell)
    {
        var value = cell.NumericCellValue;
        if (DateUtil.IsCellDateFormatted(cell)
            || (value is >= 20000 and <= 80000 && LooksLikeDateColumn(cell)))
        {
            try
            {
                return DateTime.FromOADate(value).ToString("dd.MM.yyyy", Ru);
            }
            catch
            {
                // fall through
            }
        }

        // Целые протоколы без «.0»
        if (Math.Abs(value - Math.Truncate(value)) < 0.0000001)
        {
            return ((long)Math.Truncate(value)).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Эвристика: число в колонке даты обучения / следующей даты.</summary>
    private static bool LooksLikeDateColumn(ICell cell)
    {
        var row = cell.Row;
        var sheet = row?.Sheet;
        var header = sheet?.GetRow(0);
        if (header is null)
        {
            return true; // лучше распознать дату, чем потерять её
        }

        var title = NormalizeHeader(GetHeaderRaw(header.GetCell(cell.ColumnIndex)));
        return title.Contains("дата", StringComparison.Ordinal);
    }

    private static string GetHeaderRaw(ICell? cell)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        return cell.CellType == CellType.String
            ? cell.StringCellValue?.Trim() ?? string.Empty
            : cell.ToString()?.Trim() ?? string.Empty;
    }
}

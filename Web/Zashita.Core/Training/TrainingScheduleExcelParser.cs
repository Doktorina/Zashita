using System.Globalization;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;

namespace Zashita.Core.Training;

/// <summary>Строка графика обучения из Excel (один человек).</summary>
public sealed class TrainingScheduleExcelPersonRow
{
    public string Person { get; init; } = string.Empty;
    public string Profession { get; init; } = string.Empty;
    public string Snils { get; init; } = string.Empty;
    /// <summary>Ключ колонки (нормализованный заголовок) → сырой текст ячейки.</summary>
    public Dictionary<string, string> CellsByColumnKey { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Ключи колонок с зелёной заливкой → «движение протокола: готов».</summary>
    public HashSet<string> GreenFillColumnKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>В строке есть ячейка «а» / «a» → «УПД есть» для А/Б/В/СИЗ/ПП.</summary>
    public bool HasUpdLetter { get; init; }

    /// <summary>В строке есть ячейка «ж» (колонка «Ж») → галочка «Журнал».</summary>
    public bool HasJournalMarker { get; init; }
}

/// <summary>Лист графика = одна организация.</summary>
public sealed class TrainingScheduleExcelSheet
{
    public string SheetName { get; init; } = string.Empty;
    public string OrganizationHint { get; init; } = string.Empty;
    public IReadOnlyList<string> ColumnHeaders { get; init; } = [];
    public IReadOnlyList<TrainingScheduleExcelPersonRow> People { get; init; } = [];
}

public sealed class TrainingScheduleExcelParseResult
{
    public List<TrainingScheduleExcelSheet> Sheets { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

/// <summary>Парсер Excel «График обучения работников» (NPOI).</summary>
public static class TrainingScheduleExcelParser
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Regex ProtocolDateRx = new(
        @"от\s+(\d{1,2}[./]\d{1,2}[./]\d{2,4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static TrainingScheduleExcelParseResult Parse(Stream stream, string? fileName = null)
    {
        var result = new TrainingScheduleExcelParseResult();
        IWorkbook workbook;
        try
        {
            // NPOI закрывает Stream при Dispose — парсим копию байтов.
            var bytes = stream is MemoryStream ms ? ms.ToArray() : CopyStream(stream);
            var local = new MemoryStream(bytes);
            var name = (fileName ?? string.Empty).ToLowerInvariant();
            workbook = name.EndsWith(".xls", StringComparison.Ordinal) && !name.EndsWith(".xlsx", StringComparison.Ordinal)
                ? new HSSFWorkbook(local)
                : new XSSFWorkbook(local);
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Не удалось открыть файл Excel: " + ex.Message);
            return result;
        }

        try
        {
        for (var s = 0; s < workbook.NumberOfSheets; s++)
        {
            var sheet = workbook.GetSheetAt(s);
            if (sheet is null || sheet.LastRowNum < 2)
            {
                continue;
            }

            var parsed = ParseSheet(sheet);
            if (parsed is null)
            {
                result.Warnings.Add($"Лист «{sheet.SheetName}»: не найден заголовок графика обучения.");
                continue;
            }

            if (parsed.People.Count == 0)
            {
                result.Warnings.Add($"Лист «{sheet.SheetName}»: нет строк с сотрудниками.");
            }

            result.Sheets.Add(parsed);
        }

        if (result.Sheets.Count == 0)
        {
            result.Warnings.Add("В файле нет распознанных листов графика обучения.");
            }
        }
        finally
        {
            workbook.Close();
        }

        return result;
    }

    private static byte[] CopyStream(Stream stream)
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

    public static bool TryParseProtocolCell(string? raw, out string protocolText, out DateTime? trainingDate)
    {
        protocolText = string.Empty;
        trainingDate = null;
        var text = NormalizeCell(raw);
        if (string.IsNullOrWhiteSpace(text) || IsSkipCell(text))
        {
            return false;
        }

        // Ячейка — только дата (дата приёма / дата предыдущей проверки)
        if (Regex.IsMatch(text, @"^\d{1,2}[./]\d{1,2}[./]\d{2,4}\.?$"))
        {
            trainingDate = ParseFlexibleDate(text.TrimEnd('.'));
            return trainingDate.HasValue;
        }

        trainingDate = ExtractTrainingDateFromCell(text);
        protocolText = FormatCleanProtocolText(text, trainingDate);
        if (string.IsNullOrWhiteSpace(protocolText))
        {
        protocolText = text;
        }

        protocolText = NormalizeOrgAliasesInText(protocolText);
        return true;
    }

    /// <summary>Дата обучения из «от 05.10.2023» / хвоста ячейки.</summary>
    public static DateTime? ExtractTrainingDateFromCell(string? raw)
    {
        var text = NormalizeCell(raw);
        if (text.Length == 0)
        {
            return null;
        }

        var m = ProtocolDateRx.Match(text);
        if (m.Success)
        {
            var d = ParseFlexibleDate(m.Groups[1].Value);
            if (d.HasValue)
            {
                return d;
            }
        }

        // Дата сразу после «№ …» без «от»: «№ 8545 10.08.2023»
        var afterNum = Regex.Match(
            text,
            @"№\s*[A-Za-zА-Яа-я0-9][A-Za-zА-Яа-я0-9./\-–—]*\s+(\d{1,2}[./]\d{1,2}[./]\d{2,4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (afterNum.Success)
        {
            var d = ParseFlexibleDate(afterNum.Groups[1].Value);
            if (d.HasValue)
            {
                return d;
            }
        }

            var tail = Regex.Match(text, @"(\d{1,2}[./]\d{1,2}[./]\d{2,4})\s*\.?$");
        return tail.Success ? ParseFlexibleDate(tail.Groups[1].Value) : null;
    }

    /// <summary>
    /// В поле протокола — только «Удостоверение/Протокол № … от дата»,
    /// без хвоста «Ответственный… 52 часа УЦ Специалист».
    /// </summary>
    public static string FormatCleanProtocolText(string? raw, DateTime? trainingDate = null)
    {
        var text = NormalizeCell(raw);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        trainingDate ??= ExtractTrainingDateFromCell(text);
        var heightPrefix = ExtractHeightGroupPrefix(text);

        // «Удостоверение № … от DD.MM.YYYY» / «Протокол № … от …»
        var doc = Regex.Match(
            text,
            @"((?:Удостоверение|Протокол|Свидетельство)\s*№\s*[A-Za-zА-Яа-я0-9][A-Za-zА-Яа-я0-9./\-–—\s]*?\s+от\s+\d{1,2}[./]\d{1,2}[./]\d{2,4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (doc.Success)
        {
            return WithHeightGroupPrefix(
                heightPrefix,
                Regex.Replace(doc.Groups[1].Value, @"\s+", " ").Trim());
        }

        // «№ … от дата» без слова Удостоверение
        var numOt = Regex.Match(
            text,
            @"(№\s*[A-Za-zА-Яа-я0-9][A-Za-zА-Яа-я0-9./\-–—]*\s+от\s+\d{1,2}[./]\d{1,2}[./]\d{2,4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (numOt.Success)
        {
            var body = Regex.Replace(numOt.Groups[1].Value, @"\s+", " ").Trim();
            if (text.Contains("удостовер", StringComparison.OrdinalIgnoreCase))
            {
                return WithHeightGroupPrefix(heightPrefix, "Удостоверение " + body);
            }

            if (text.Contains("протокол", StringComparison.OrdinalIgnoreCase))
            {
                return WithHeightGroupPrefix(heightPrefix, "Протокол " + body);
            }

            return WithHeightGroupPrefix(heightPrefix, body);
        }

        // Обрезаем хвост курса после даты.
        if (trainingDate.HasValue)
        {
            var dateStr = trainingDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            var idx = text.IndexOf(dateStr, StringComparison.Ordinal);
            if (idx < 0)
            {
                dateStr = trainingDate.Value.ToString("d.M.yyyy", CultureInfo.InvariantCulture);
                idx = text.IndexOf(dateStr, StringComparison.Ordinal);
            }

            if (idx >= 0)
            {
                return WithHeightGroupPrefix(
                    heightPrefix,
                    text[..(idx + dateStr.Length)].Trim());
            }
        }

        // Высота: оставляем «N гр» + протокол до лишнего хвоста.
        var heightCut = Regex.Match(
            text,
            @"^(\s*[123]\s*гр(?:упп\w*)?\.?\s*(?:Протокол|Удостоверение).*?от\s+\d{1,2}[./]\d{1,2}[./]\d{2,4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (heightCut.Success)
        {
            return Regex.Replace(heightCut.Groups[1].Value, @"\s+", " ").Trim();
        }

        return WithHeightGroupPrefix(heightPrefix, text);
    }

    /// <summary>«3 гр» / «2 группа» в начале ячейки высоты — сохраняем в протоколе.</summary>
    internal static string ExtractHeightGroupPrefix(string? text)
    {
        var t = NormalizeCell(text);
        if (t.Length == 0)
        {
            return string.Empty;
        }

        var m = Regex.Match(
            t,
            @"^\s*([123])\s*[-.]?\s*(?:я|й|ой|ая)?\s*(гр(?:упп\w*)?\.?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return string.Empty;
        }

        return m.Groups[1].Value + " " + m.Groups[2].Value.Trim().TrimEnd('.');
    }

    private static string WithHeightGroupPrefix(string prefix, string body)
    {
        var clean = Regex.Replace((body ?? string.Empty).Trim(), @"\s+", " ");
        if (clean.Length == 0)
        {
            return string.Empty;
        }

        var p = (prefix ?? string.Empty).Trim();
        if (p.Length == 0)
        {
            return clean;
        }

        // Уже есть префикс группы — не дублируем.
        if (Regex.IsMatch(
                clean,
                @"^\s*[123]\s*[-.]?\s*(?:я|й|ой|ая)?\s*гр(?:упп\w*)?\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return clean;
        }

        return p + " " + clean;
    }

    /// <summary>Метка УПД в графике: одиночная буква «а» / «a».</summary>
    public static bool IsUpdLetterCell(string? raw)
    {
        var t = NormalizeCell(raw).Trim();
        return t.Equals("а", StringComparison.OrdinalIgnoreCase)
               || t.Equals("a", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Метка «Журнал» в графике: одиночная буква «ж» (колонка «Ж»).</summary>
    public static bool IsJournalLetterCell(string? raw)
    {
        var t = NormalizeCell(raw).Trim();
        return t.Equals("ж", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSkipCell(string? raw)
    {
        var t = NormalizeCell(raw).ToLowerInvariant().Replace('ё', 'е');
        if (t.Length == 0 || t is "-" or "—" or "." || IsUpdLetterCell(t) || IsJournalLetterCell(t))
        {
            return true;
        }

        return t.Contains("увольн", StringComparison.Ordinal)
               || t.Contains("уволен", StringComparison.Ordinal)
               || t.Contains("перевод", StringComparison.Ordinal)
               || t.Contains("не обуча", StringComparison.Ordinal)
               || t.Contains("не учим", StringComparison.Ordinal)
               || t.Contains("не учили", StringComparison.Ordinal)
               || t.Contains("заявка", StringComparison.Ordinal)
               || t.StartsWith("диплом", StringComparison.Ordinal)
               || IsEmploymentOrVenueNoteCell(t);
    }

    /// <summary>
    /// Заметки вроде «обучен Ресурсом ССтрой Основное место» — не номер протокола.
    /// Без «протокол»/«удостоверение»/«№…» в А/Б/СИЗ/ПП не пишем.
    /// </summary>
    public static bool IsEmploymentOrVenueNoteCell(string? raw)
    {
        var t = NormalizeCell(raw).ToLowerInvariant().Replace('ё', 'е');
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0)
        {
            return false;
        }

        // Место обучения важнее даты: «…Основное место от 19.09.2023» — всё равно пометка.
        if (t.Contains("основное место", StringComparison.Ordinal)
            || t.Contains("совмест", StringComparison.Ordinal)
            || t is "основное" or "совм" or "совм.")
        {
            return true;
        }

        if (LooksLikeProtocolOrCertificate(t))
        {
            return false;
        }

        var mentionsStandartStroy =
            t.Contains("сстрой", StringComparison.Ordinal)
            || t.Contains("стандартстрой", StringComparison.Ordinal)
            || t.Contains("стандарт строй", StringComparison.Ordinal)
            || Regex.IsMatch(t, @"стандарт\s*строй", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // «обучен Ресурсом…» / «обучен в Стандарт Строй…» / «обучен в УЦ…» без номера документа
        if (t.Contains("обучен", StringComparison.Ordinal)
            && (t.Contains("ресурс", StringComparison.Ordinal)
                || mentionsStandartStroy
                || t.Contains(" уц", StringComparison.Ordinal)
                || t.StartsWith("уц ", StringComparison.Ordinal)
                || t.Contains("учебн", StringComparison.Ordinal)
                || Regex.IsMatch(t, @"\bобучен\s+в\b", RegexOptions.IgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <summary>Есть признаки удостоверения/протокола (не свободная пометка).</summary>
    public static bool LooksLikeProtocolOrCertificate(string? raw)
    {
        var t = NormalizeCell(raw).ToLowerInvariant().Replace('ё', 'е');
        if (t.Length == 0)
        {
            return false;
        }

        if (t.Contains("протокол", StringComparison.Ordinal)
            || t.Contains("удостовер", StringComparison.Ordinal)
            || t.Contains("свидетель", StringComparison.Ordinal))
        {
            return true;
        }

        // «№ U123/10» / «№8545»
        return Regex.IsMatch(t, @"№\s*[a-zа-я0-9]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>«ССтрой» в Excel → «СтандартСтрой» в протоколе/тексте.</summary>
    public static string NormalizeOrgAliasesInText(string? raw)
    {
        var text = NormalizeCell(raw);
        if (text.Length == 0)
        {
            return text;
        }

        // Аббревиатура «ССтрой» / «С-Строй» → полное «СтандартСтрой»
        text = Regex.Replace(
            text,
            @"\b[СCсc]\s*[-.]?\s*[СCсc]трой\b",
            "СтандартСтрой",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(
            text,
            @"\b[СCсc]строй\b",
            "СтандартСтрой",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return text;
    }

    /// <summary>Ключ колонки для сопоставления с программой отчёта.</summary>
    public static string NormalizeColumnKey(string? header)
    {
        var t = NormalizeCell(header).ToLowerInvariant().Replace('ё', 'е');
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0)
        {
            return string.Empty;
        }

        // Короткие коды программ (в т.ч. «А+Б», «А-Б», «В - 1 раз в год»)
        if (t.Contains("дата прием", StringComparison.Ordinal)
            || t.Contains("приема на работ", StringComparison.Ordinal)
            || t.Contains("прием на работ", StringComparison.Ordinal))
        {
            return "hire";
        }

        if (Regex.IsMatch(t, @"^[аa]\s*[-–+/]\s*[бb]\b")
            || t.Contains("а-б", StringComparison.Ordinal)
            || t.Contains("а+б", StringComparison.Ordinal)
            || t is "а-б" or "аб" or "а+б")
        {
            return "ab";
        }

        // Промбез «А.1» / «Б.8.2» — до «Программа А/Б», иначе «А.1» схлопывается в ключ a.
        if (LooksLikeIndustrialSafetyCodeHeader(t))
        {
            var earlyPb = TryExtractIndustrialSafetyCode(t);
            if (earlyPb is not null)
            {
                return "pb:" + earlyPb;
            }
        }

        if (Regex.IsMatch(t, @"^а\s*[-–.]?\s*1\b") || t.StartsWith("а - 1", StringComparison.Ordinal))
        {
            return "a";
        }

        if (Regex.IsMatch(t, @"^б\s*[-–.]?\s*1\b") || t.StartsWith("б - 1", StringComparison.Ordinal))
        {
            return "b";
        }

        // «В - 1 раз в год» / «В В - 1 раз в год» (склейка заголовков)
        if (Regex.IsMatch(t, @"\bв(\s+в)?\s*[-–.]?\s*1\s*раз", RegexOptions.IgnoreCase)
            || Regex.IsMatch(t, @"^[вb]\s*[-–]\s*1\b")
            || t == "в"
            || t.StartsWith("в -", StringComparison.Ordinal)
            || t.StartsWith("программа в", StringComparison.Ordinal)
            || (t.Contains("программа", StringComparison.Ordinal) && Regex.IsMatch(t, @"\bв\b")
                && t.Contains("1 раз в год", StringComparison.Ordinal)
                && !t.Contains("а-б", StringComparison.Ordinal)
                && !t.Contains("а+б", StringComparison.Ordinal)
                && !t.Contains("сиз", StringComparison.Ordinal)))
        {
            return "v";
        }

        // Высота раньше СИЗ/ПП — заголовок длинный, может содержать лишнее.
        // «1 гр» / «2 гр» / «3 гр» в заголовке → отдельные ключи, иначе все колонки схлопываются.
        if (t.Contains("высот", StringComparison.Ordinal)
            || LooksLikeHeightGroupOnlyHeader(t))
        {
            return ClassifyHeightColumnKey(t);
        }

        if (t.Contains("сиз", StringComparison.Ordinal))
        {
            return "siz";
        }

        if (t.Contains("пп", StringComparison.Ordinal) && !t.Contains("пб", StringComparison.Ordinal)
            || t.Contains("первая помощ", StringComparison.Ordinal)
            || t.Contains("перв. помощ", StringComparison.Ordinal))
        {
            return "pp";
        }

        if (t.Contains("пожар", StringComparison.Ordinal)
            || t.Contains(" пб", StringComparison.Ordinal)
            || t.Contains("по пб", StringComparison.Ordinal)
            || t.StartsWith("пб", StringComparison.Ordinal)
            || t.Contains("противопожар", StringComparison.Ordinal)
            || t.Contains("птм", StringComparison.Ordinal))
        {
            return ClassifyFireColumnKey(t);
        }

        if (t.Contains("стропаль", StringComparison.Ordinal))
        {
            return "slinger";
        }

        if (t.Contains("эл. безопас", StringComparison.Ordinal) || t.Contains("электробезопас", StringComparison.Ordinal)
            || t.Contains("эл безопас", StringComparison.Ordinal))
        {
            return "electrical";
        }

        // Коды промбезопасности А.1 / A.1, Б.8.2 / B.8.2 (Excel часто даёт латиницу).
        var pbCode = TryExtractIndustrialSafetyCode(t);
        if (pbCode is not null)
        {
            return "pb:" + pbCode;
        }

        if (t.Contains("пром", StringComparison.Ordinal) && t.Contains("безопас", StringComparison.Ordinal))
        {
            return "industrial";
        }

        if (t.Contains("соут", StringComparison.Ordinal))
        {
            return "sout";
        }

        if (t.Contains("проф обучен", StringComparison.Ordinal) || t.Contains("профобучение", StringComparison.Ordinal))
        {
            return "prof";
        }

        return "h:" + t;
    }

    /// <summary>
    /// Подзаголовок только «1 гр» / «2 группа» без слова «высота» (родитель уже «Работы на высоте»).
    /// </summary>
    internal static bool LooksLikeHeightGroupOnlyHeader(string normalizedHeader)
    {
        var t = (normalizedHeader ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Length is 0 or > 40)
        {
            return false;
        }

        // Не путать с «1 раз в год», датами и т.п.
        if (t.Contains("раз", StringComparison.Ordinal)
            || t.Contains("дата", StringComparison.Ordinal)
            || t.Contains("проверк", StringComparison.Ordinal)
            || t.Contains("протокол", StringComparison.Ordinal))
        {
            return false;
        }

        return InferHeightGroupFromHeaderText(t) is not null;
    }

    /// <summary>Ключ колонки высоты: height / height:1 / height:2 / height:3.</summary>
    internal static string ClassifyHeightColumnKey(string normalizedHeader)
    {
        var t = (normalizedHeader ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        var groups = InferAllHeightGroupsFromText(t);
        if (groups.Count == 1)
        {
            return $"height:{groups[0]}";
        }

        // «1 гр, 2 гр, 3 гр» в одной колонке — группа только из ячейки.
        return "height";
    }

    /// <summary>«1 гр», «2 группа», «гр.3» в заголовке или ячейке.</summary>
    public static int? InferHeightGroupFromHeaderText(string? text)
    {
        var all = InferAllHeightGroupsFromText(text);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>Все упомянутые группы 1–3 в тексте (порядок появления).</summary>
    public static List<int> InferAllHeightGroupsFromText(string? text)
    {
        var t = NormalizeCell(text).ToLowerInvariant().Replace('ё', 'е');
        t = Regex.Replace(t, @"\s+", " ").Trim();
        var found = new List<int>();
        if (t.Length == 0)
        {
            return found;
        }

        // Сначала явный префикс ячейки: «2 гр Протокол…» / «3 группа …»
        var lead = Regex.Match(
            t,
            @"^\s*([123])\s*[-.]?\s*(?:я|й|ой|ая)?\s*гр(?:упп\w*)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (lead.Success
            && int.TryParse(lead.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leadG)
            && leadG is >= 1 and <= 3)
        {
            found.Add(leadG);
            return found;
        }

        // Не путать «1 раз в …» / даты с группой высоты.
        // Ищем явные маркеры группы; «1 гр» в начале протокола — основной случай.
        var patterns = new[]
        {
            // «1 гр», «1гр», «1 гр.», «1-я гр»
            @"(?:^|[^\d])([123])\s*[-.]?\s*(?:я|й|ой|ая)?\s*гр(?:упп\w*)?(?:\.|\b|$)",
            @"\bгр\.?\s*([123])\b",
            @"\bгрупп[аыуе]?\s*(?:№\s*)?([123])\b",
            @"\b([123])\s*group\b",
            @"([123])гр\.?(?:\b|$)",
        };

        foreach (var pattern in patterns)
        {
            foreach (Match m in Regex.Matches(t, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            for (var i = 1; i < m.Groups.Count; i++)
            {
                if (!m.Groups[i].Success)
                {
                    continue;
                }

                if (int.TryParse(m.Groups[i].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
                    && g is >= 1 and <= 3
                    && !found.Contains(g))
                {
                    found.Add(g);
                }
                }
            }

            // Достаточно первой найденной группы в ячейке протокола.
            if (found.Count > 0)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// Подтипы колонок ПБ: «Ответственный…» и «Инструктажи…» нельзя схлопывать в один ключ fire —
    /// иначе удостоверение ответственного попадает в программу инструктажей.
    /// </summary>
    internal static string ClassifyFireColumnKey(string normalizedHeader)
    {
        var t = (normalizedHeader ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Contains("ответственн", StringComparison.Ordinal))
        {
            return "fire:responsible";
        }

        // Только «специалист по пожарной профилактике», не «УЦ Специалист».
        if (t.Contains("специалист", StringComparison.Ordinal)
            && (t.Contains("профилактик", StringComparison.Ordinal)
                || (t.Contains("пожар", StringComparison.Ordinal) && t.Contains("специалист по", StringComparison.Ordinal))))
        {
            return "fire:specialist";
        }

        if (t.Contains("проведен", StringComparison.Ordinal) && t.Contains("инструктаж", StringComparison.Ordinal))
        {
            return "fire:conducting";
        }

        if (t.Contains("инструктаж", StringComparison.Ordinal))
        {
            if (t.Contains("вводн", StringComparison.Ordinal))
            {
                return "fire:intro";
            }

            if (t.Contains("первичн", StringComparison.Ordinal))
            {
                return "fire:primary";
            }

            if (t.Contains("повторн", StringComparison.Ordinal))
            {
                return "fire:repeat";
            }

            // «Инструктажи по ПБ 24 часа»
            return "fire:briefings";
        }

        if (t.Contains("обучен", StringComparison.Ordinal)
            && (t.Contains("мер", StringComparison.Ordinal) || t.Contains("пожар", StringComparison.Ordinal)))
        {
            return "fire:training";
        }

        return "fire";
    }

    /// <summary>
    /// Тип программы ПБ по тексту ячейки (удостоверение часто содержит название курса).
    /// Курс в удостоверении важнее заголовка колонки.
    /// </summary>
    public static string? InferFireColumnKeyFromCell(string? cellText)
    {
        var t = NormalizeCell(cellText).ToLowerInvariant().Replace('ё', 'е');
        if (t.Length < 8)
        {
            return null;
        }

        var looksFire = t.Contains("пожар", StringComparison.Ordinal)
                        || t.Contains("пб", StringComparison.Ordinal)
                        || t.Contains("противопожар", StringComparison.Ordinal)
                        || t.Contains("птм", StringComparison.Ordinal)
                        || t.Contains("ответственн", StringComparison.Ordinal)
                        || (t.Contains("инструктаж", StringComparison.Ordinal)
                            && (t.Contains("пб", StringComparison.Ordinal) || t.Contains("пожар", StringComparison.Ordinal)));
        if (!looksFire)
        {
            return null;
        }

        // Порядок: явный курс в тексте удостоверения важнее заголовка колонки.
        // «УЦ Специалист» не перекрывает «Ответственный…».
        if (t.Contains("ответственн", StringComparison.Ordinal))
        {
            return "fire:responsible";
        }

        // «Специалист по пожарной профилактике» — до общих «инструктажи».
        if (t.Contains("специалист", StringComparison.Ordinal)
            && (t.Contains("профилактик", StringComparison.Ordinal)
                || t.Contains("специалист по пожар", StringComparison.Ordinal)))
        {
            return "fire:specialist";
        }

        if (t.Contains("проведен", StringComparison.Ordinal) && t.Contains("инструктаж", StringComparison.Ordinal))
        {
            return "fire:conducting";
        }

        if ((t.Contains("инструктаж", StringComparison.Ordinal) || t.Contains("инструктажи", StringComparison.Ordinal))
            && (t.Contains("пб", StringComparison.Ordinal) || t.Contains("пожар", StringComparison.Ordinal))
            && !t.Contains("ответственн", StringComparison.Ordinal)
            && !t.Contains("вводн", StringComparison.Ordinal)
            && !t.Contains("первичн", StringComparison.Ordinal)
            && !t.Contains("повторн", StringComparison.Ordinal)
            && !t.Contains("специалист", StringComparison.Ordinal))
        {
            return "fire:briefings";
        }

        var key = ClassifyFireColumnKey(t);
        return key is "fire" ? null : key;
    }

    /// <summary>
    /// Выбирает фрагмент заголовка, дающий самый точный ключ колонки
    /// (код промбез / подтип ПБ вместо общего «Промышленная…» / «Пожарная…»).
    /// </summary>
    public static string PickBestHeaderForColumnKey(IReadOnlyList<string> parts)
    {
        var distinct = parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0)
        {
            return string.Empty;
        }

        if (distinct.Count == 1)
        {
            return distinct[0];
        }

        // Явный код промбезопасности в любой строке заголовка — всегда он
        // (иначе «Промышленная безопасность» / «Дата очередной проверки» схлопывают колонки).
        string? pbPart = null;
        foreach (var part in distinct)
        {
            if (TryExtractIndustrialSafetyCode(part) is not null)
            {
                if (pbPart is null || part.Length < pbPart.Length)
                {
                    pbPart = part;
                }
            }
        }

        if (pbPart is not null)
        {
            return pbPart;
        }

        string? best = null;
        var bestScore = int.MinValue;
        foreach (var part in distinct)
        {
            var key = NormalizeColumnKey(part);
            var score = FireHeaderSpecificityScore(key);
            // Более длинный уточняющий подзаголовок чуть предпочтительнее.
            score += Math.Min(part.Length, 80) / 40;
            if (score > bestScore)
            {
                bestScore = score;
                best = part;
            }
        }

        // Если все части дали общий fire — склеиваем: «Пожарная…» + «Ответственный…».
        if (bestScore <= 1)
        {
            return string.Join(" ", distinct);
        }

        return best ?? string.Join(" ", distinct);
    }

    private static int FireHeaderSpecificityScore(string key)
    {
        var k = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (k.StartsWith("fire:", StringComparison.Ordinal))
        {
            return 10;
        }

        if (k.StartsWith("height:", StringComparison.Ordinal))
        {
            return 10;
        }

        if (k.StartsWith("pb:", StringComparison.Ordinal))
        {
            // Выше общего industrial / даты проверки — иначе все колонки А.1/Б.8.x схлопываются.
            return 12;
        }

        if (k is "fire")
        {
            return 1;
        }

        if (k is "height")
        {
            return 1;
        }

        if (k is "industrial")
        {
            return 1;
        }

        if (k is "hire" or "a" or "b" or "ab" or "v" or "siz" or "pp" or "electrical")
        {
            return 8;
        }

        // «Дата очередной проверки знаний» — служебная подпись, не программа.
        if (k.StartsWith("h:дата", StringComparison.Ordinal)
            || k.Contains("очередн", StringComparison.Ordinal)
            || (k.Contains("проверк", StringComparison.Ordinal) && k.Contains("дата", StringComparison.Ordinal)))
        {
            return 0;
        }

        return 2;
    }

    /// <summary>
    /// Заголовок колонки — код области аттестации, а не «А - 1 раз в 3 года».
    /// </summary>
    private static bool LooksLikeIndustrialSafetyCodeHeader(string normalizedHeader)
    {
        var t = (normalizedHeader ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return false;
        }

        if (t.Contains("раз", StringComparison.Ordinal)
            || t.Contains("программ", StringComparison.Ordinal)
            || t.Contains("год", StringComparison.Ordinal)
            || t.Contains("месяц", StringComparison.Ordinal)
            || t.Contains("инструктаж", StringComparison.Ordinal))
        {
            return false;
        }

        // «А.1», «A.1.», «Б.8.2. Эксплуатация…» — точка после буквы обязательна
        // (иначе «раз в 3 года» даёт ложный код В.3).
        return Regex.IsMatch(
            t,
            @"^[aабbвvгg]\.\s*\d+(?:\.\d+)?\.?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Код области аттестации: «А.1», «A.1.», «Б.8.2», «B.8.22» → канон «А.1» / «Б.8.2» (кириллица).
    /// </summary>
    public static string? TryExtractIndustrialSafetyCode(string? text)
    {
        var t = NormalizeCell(text).ToLowerInvariant().Replace('ё', 'е');
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length == 0)
        {
            return null;
        }

        // Точка после буквы обязательна: А.1 / Б.8.22, не «А - 1» и не «в 3 года».
        var m = Regex.Match(
            t,
            @"(?:^|[^\w.])([aабbвvгg])\.\s*(\d+)(?:\.(\d+))?(?=$|[^\d])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            m = Regex.Match(
                t,
                @"^([aабbвvгg])\.\s*(\d+)(?:\.(\d+))?\.?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!m.Success)
        {
            return null;
        }

        var letter = NormalizeIndustrialSafetyLetter(m.Groups[1].Value);
        if (letter is null)
        {
            return null;
        }

        var major = m.Groups[2].Value;
        var minor = m.Groups[3].Success ? m.Groups[3].Value : null;
        return minor is null ? $"{letter}.{major}" : $"{letter}.{major}.{minor}";
    }

    private static string? NormalizeIndustrialSafetyLetter(string raw)
    {
        var ch = raw.Trim().ToLowerInvariant().Replace('ё', 'е');
        if (ch.Length == 0)
        {
            return null;
        }

        return ch[0] switch
        {
            'а' or 'a' => "А",
            'б' or 'b' => "Б",
            'в' or 'v' => "В",
            'г' or 'g' => "Г",
            _ => null
        };
    }

    /// <summary>Число часов из заголовка/ячейки («26 часов», «32 часа»).</summary>
    public static int? TryExtractHours(string? text)
    {
        var t = NormalizeCell(text);
        if (t.Length == 0)
        {
            return null;
        }

        var m = Regex.Match(t, @"(\d{1,3})\s*час", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours))
        {
            return null;
        }

        return hours is >= 1 and <= 500 ? hours : null;
    }

    private static TrainingScheduleExcelSheet? ParseSheet(ISheet sheet)
    {
        var maxRow = sheet.LastRowNum;
        var maxCol = 0;
        for (var r = 0; r <= Math.Min(maxRow, 15); r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            maxCol = Math.Max(maxCol, (int)row.LastCellNum);
        }

        if (maxCol < 5)
        {
            return null;
        }

        var orgHint = sheet.SheetName?.Trim() ?? string.Empty;
        var headerRowIndex = -1;
        var fioCol = -1;
        var professionCol = -1;
        var snilsCol = -1;

        for (var r = 0; r <= Math.Min(maxRow, 12); r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            for (var c = 0; c < maxCol; c++)
            {
                var text = GetCellString(row.GetCell(c));
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (text.Contains("ГРАФИК", StringComparison.OrdinalIgnoreCase)
                    && text.Contains("ОБУЧ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Строка с названием орг под заголовком
                if ((text.Contains("ООО", StringComparison.OrdinalIgnoreCase)
                     || text.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("АО ", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("АО«", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("АО\"", StringComparison.OrdinalIgnoreCase))
                    && text.Length < 120
                    && !text.Contains("Фамилия", StringComparison.OrdinalIgnoreCase))
                {
                    orgHint = text.Trim('"', '«', '»', ' ', '\'');
                }

                if (text.Contains("Фамилия", StringComparison.OrdinalIgnoreCase)
                    && text.Contains("Имя", StringComparison.OrdinalIgnoreCase))
                {
                    headerRowIndex = r;
                    fioCol = c;
                }

                if (headerRowIndex == r
                    && (text.Contains("должность", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("професси", StringComparison.OrdinalIgnoreCase)))
                {
                    professionCol = c;
                }

                if (headerRowIndex == r && text.Contains("СНИЛС", StringComparison.OrdinalIgnoreCase))
                {
                    snilsCol = c;
                }
            }

            if (headerRowIndex >= 0 && fioCol >= 0)
            {
                break;
            }
        }

        if (headerRowIndex < 0 || fioCol < 0)
        {
            return null;
        }

        // Собираем заголовки колонок программ: строки header..header+2
        var columnKeys = new Dictionary<int, string>();
        var columnHeaders = new List<string>();
        for (var c = 0; c < maxCol; c++)
        {
            if (c == fioCol || c == professionCol || c == snilsCol || c < fioCol)
            {
                continue;
            }

            var parts = new List<string>();
            for (var r = Math.Max(0, headerRowIndex - 1); r <= headerRowIndex + 2 && r <= maxRow; r++)
            {
                var cell = sheet.GetRow(r)?.GetCell(c);
                var t = GetCellString(cell);
                if (string.IsNullOrWhiteSpace(t)
                    || t.Contains("Фамилия", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("СНИЛС", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Дата рождения", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("а", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("ж", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("№", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("№№", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("п/п", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                parts.Add(t);
            }

            // Для ПБ важна узкая строка («Ответственный…» / «Инструктажи…»), а не общий
            // заголовок «Пожарная безопасность» — иначе обе колонки схлопываются в fire.
            var header = PickBestHeaderForColumnKey(parts);
            var key = NormalizeColumnKey(header);

            // «Дата очередной проверки» — служебная подпись под кодом промбез; саму программу не отбрасываем.
            if (!key.StartsWith("pb:", StringComparison.OrdinalIgnoreCase)
                && header.Contains("очередн", StringComparison.OrdinalIgnoreCase)
                && header.Contains("проверк", StringComparison.OrdinalIgnoreCase)
                && !header.Contains("предыдущ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(key) || key.StartsWith("h:дата", StringComparison.Ordinal))
            {
                if (header.Contains("Дата", StringComparison.OrdinalIgnoreCase)
                    && header.Contains("проверк", StringComparison.OrdinalIgnoreCase)
                    && !header.Contains("предыдущ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // Не дублируем чисто date-колонки рядом с программой (кроме даты приёма)
            if (key.StartsWith("h:дата", StringComparison.Ordinal) && key != "hire")
            {
                continue;
            }

            columnKeys[c] = key;
            columnHeaders.Add(header.Length > 0 ? header : key);
        }

        var people = new List<TrainingScheduleExcelPersonRow>();
        for (var r = headerRowIndex + 1; r <= maxRow; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            var person = GetCellString(row.GetCell(fioCol));
            if (string.IsNullOrWhiteSpace(person)
                || person.Contains("Фамилия", StringComparison.OrdinalIgnoreCase)
                || person.Length < 5)
            {
                continue;
            }

            // Отсечь строки-разделители
            if (person.Contains("ГРАФИК", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var greenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasUpdLetter = false;
            var hasJournalMarker = false;

            // Метки «а» / «ж» часто в узких колонках слева от ФИО — без программного заголовка.
            for (var c = 0; c < maxCol; c++)
            {
                var mark = GetCellString(row.GetCell(c));
                if (IsUpdLetterCell(mark))
                {
                    hasUpdLetter = true;
                }

                if (IsJournalLetterCell(mark))
                {
                    hasJournalMarker = true;
                }

                if (hasUpdLetter && hasJournalMarker)
                {
                    break;
                }
            }

            foreach (var (col, key) in columnKeys)
            {
                var cell = row.GetCell(col);
                if (IsGreenFill(cell))
                {
                    // Зелёная заливка любой программной колонки → «движение протокола: готов».
                    greenKeys.Add(key);
                }

                var raw = GetCellString(cell);
                if (string.IsNullOrWhiteSpace(raw) || IsUpdLetterCell(raw) || IsJournalLetterCell(raw))
                {
                    continue;
                }

                // Если уже есть значение по ключу — оставляем более «протокольное»
                if (cells.TryGetValue(key, out var prev)
                    && IsRicherTrainingCell(prev, raw))
                {
                    continue;
                }

                cells[key] = raw;
            }

            people.Add(new TrainingScheduleExcelPersonRow
            {
                Person = CleanPersonName(person),
                Profession = professionCol >= 0 ? GetCellString(row.GetCell(professionCol)) : string.Empty,
                Snils = snilsCol >= 0 ? GetCellString(row.GetCell(snilsCol)) : string.Empty,
                CellsByColumnKey = cells,
                GreenFillColumnKeys = greenKeys,
                HasUpdLetter = hasUpdLetter,
                HasJournalMarker = hasJournalMarker
            });
        }

        return new TrainingScheduleExcelSheet
        {
            SheetName = sheet.SheetName ?? string.Empty,
            OrganizationHint = string.IsNullOrWhiteSpace(orgHint) ? sheet.SheetName ?? string.Empty : orgHint,
            ColumnHeaders = columnHeaders,
            People = people
        };
    }

    private static string CleanPersonName(string person)
    {
        var t = person.Trim();
        t = Regex.Replace(t, @"\s*\(совм\.?\)\s*", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }

    /// <summary>true, если <paramref name="existing"/> информативнее <paramref name="incoming"/> (не перезаписывать).</summary>
    private static bool IsRicherTrainingCell(string existing, string incoming)
    {
        static int Score(string s)
        {
            var t = s ?? string.Empty;
            var score = t.Length;
            if (t.Contains("Удостоверение", StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }

            if (t.Contains("Протокол", StringComparison.OrdinalIgnoreCase))
            {
                score += 800;
            }

            if (t.Contains("от ", StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }

            return score;
        }

        return Score(existing) >= Score(incoming);
    }

    private static string GetCellString(ICell? cell)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        try
        {
            return cell.CellType switch
            {
                CellType.String => cell.StringCellValue?.Trim() ?? string.Empty,
                CellType.Numeric when DateUtil.IsCellDateFormatted(cell) =>
                    DateTime.FromOADate(cell.NumericCellValue).ToString("dd.MM.yyyy", Ru),
                CellType.Numeric => FormatScheduleNumeric(cell.NumericCellValue),
                CellType.Boolean => cell.BooleanCellValue ? "true" : "false",
                CellType.Formula => GetFormulaValue(cell),
                _ => cell.ToString()?.Trim() ?? string.Empty
            };
        }
        catch
        {
            return cell.ToString()?.Trim() ?? string.Empty;
        }
    }

    /// <summary>Колонки программ ОТ: А / Б / А-Б / В / СИЗ / ПП.</summary>
    public static bool IsOtCoreColumnKey(string? columnKey)
    {
        var key = (columnKey ?? string.Empty).Trim().ToLowerInvariant();
        return key is "a" or "b" or "ab" or "v" or "siz" or "pp";
    }

    /// <summary>Зелёная заливка ячейки (протокол готов к движению).</summary>
    public static bool IsGreenFill(ICell? cell)
    {
        if (cell?.CellStyle is null)
        {
            return false;
        }

        var style = cell.CellStyle;

        // XSSF (.xlsx): прямой RGB, tint, theme → RGB.
        if (style is XSSFCellStyle xssfStyle)
        {
            if (TryXssfColorIsGreen(xssfStyle.FillForegroundXSSFColor, cell.Sheet?.Workbook))
            {
                return true;
            }

            if (TryXssfColorIsGreen(xssfStyle.FillBackgroundXSSFColor, cell.Sheet?.Workbook))
            {
                return true;
            }
        }

        // HSSF (.xls): палитра.
        if (cell.Sheet?.Workbook is HSSFWorkbook hssf)
        {
            try
            {
                if (style.FillPattern is not FillPattern.NoFill)
                {
                    var palette = hssf.GetCustomPalette();
                    var color = palette?.GetColor(style.FillForegroundColor);
                    var triplet = color?.GetTriplet();
                    if (triplet is { Length: >= 3 } && IsGreenRgb(triplet[0], triplet[1], triplet[2]))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // indexed/palette quirks
            }
        }

        if (style.FillPattern is FillPattern.NoFill)
        {
            return false;
        }

        // Типовые индексы зелёного в палитре Excel (classic + accents).
        var idx = style.FillForegroundColor;
        return idx is 11 or 17 or 42 or 50 or 57 or 60 or 3 or 4
               or 12 or 13 or 16 or 18 or 19 or 26 or 35 or 41 or 43 or 47 or 51;
    }

    private static bool TryXssfColorIsGreen(XSSFColor? color, IWorkbook? workbook)
    {
        if (color is null)
        {
            return false;
        }

        try
        {
            // С учётом tint (светлые зелёные заливки Excel).
            var tinted = color.GetRgbWithTint();
            if (tinted is { Length: >= 3 })
            {
                var o = tinted.Length >= 4 ? tinted.Length - 3 : 0;
                if (IsGreenRgb(tinted[o], tinted[o + 1], tinted[o + 2]))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        if (TryGetXssfRgb(color, out var r, out var g, out var b) && IsGreenRgb(r, g, b))
        {
            return true;
        }

        // Theme-цвет без готового RGB — резолвим через тему книги.
        try
        {
            if (color.IsThemed && workbook is XSSFWorkbook xssfWb)
            {
                var theme = xssfWb.GetTheme();
                if (theme is not null)
                {
                    var themed = theme.GetThemeColor((int)color.Theme);
                    if (themed is not null)
                    {
                        if (TryGetXssfRgb(themed, out r, out g, out b))
                        {
                            ApplyTint(ref r, ref g, ref b, color.Tint);
                            if (IsGreenRgb(r, g, b))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // theme API differs across NPOI versions
        }

        // Indexed green на XSSF.
        try
        {
            if (color.IsIndexed)
            {
                var idx = color.Indexed;
                if (idx is 11 or 17 or 42 or 50 or 57 or 60 or 3 or 4
                    or 12 or 13 or 16 or 18 or 19 or 26 or 35 or 41 or 43 or 47 or 51)
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static void ApplyTint(ref byte r, ref byte g, ref byte b, double tint)
    {
        if (Math.Abs(tint) < 0.0001)
        {
            return;
        }

        static byte Mix(byte c, double t)
        {
            if (t < 0)
            {
                return (byte)Math.Clamp(c * (1.0 + t), 0, 255);
            }

            return (byte)Math.Clamp(c + (255 - c) * t, 0, 255);
        }

        r = Mix(r, tint);
        g = Mix(g, tint);
        b = Mix(b, tint);
    }

    private static bool TryGetXssfRgb(XSSFColor? color, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (color is null)
        {
            return false;
        }

        try
        {
            var rgb = color.RGB;
            if (rgb is null || rgb.Length < 3)
            {
                return false;
            }

            // Иногда первый байт — alpha.
            var offset = rgb.Length >= 4 ? rgb.Length - 3 : 0;
            r = rgb[offset];
            g = rgb[offset + 1];
            b = rgb[offset + 2];
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Светло-зелёный / зелёный фон (не серый, не жёлтый).</summary>
    private static bool IsGreenRgb(byte r, byte g, byte b)
    {
        // Зелёный канал заметно выше красного и синего.
        if (g >= 90 && g >= r + 20 && g >= b + 12)
        {
            return true;
        }

        // Бледно-зелёные заливки Excel (напр. 198,239,206 / 226,239,218).
        if (g >= 170 && r >= 120 && b >= 120 && g > r && g > b && (g - r) + (g - b) >= 24)
        {
            return true;
        }

        // Ярко-зелёный акцент (0,176,80 и рядом).
        if (g >= 140 && r <= 120 && b <= 140 && g >= r + 40)
        {
            return true;
        }

        return false;
    }

    private static string GetFormulaValue(ICell cell)
    {
        try
        {
            return cell.CachedFormulaResultType switch
            {
                CellType.String => cell.StringCellValue?.Trim() ?? string.Empty,
                CellType.Numeric when DateUtil.IsCellDateFormatted(cell) =>
                    DateTime.FromOADate(cell.NumericCellValue).ToString("dd.MM.yyyy", Ru),
                CellType.Numeric => FormatScheduleNumeric(cell.NumericCellValue),
                _ => cell.ToString()?.Trim() ?? string.Empty
            };
        }
        catch
        {
            return cell.ToString()?.Trim() ?? string.Empty;
        }
    }

    private static string FormatScheduleNumeric(double value)
    {
        if (Math.Abs(value - Math.Truncate(value)) < 0.0000001)
        {
            return ((long)Math.Truncate(value)).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeCell(string? raw) =>
        string.Join(" ", (raw ?? string.Empty)
            .Replace('\u00a0', ' ')
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        .Trim();

    private static DateTime? ParseFlexibleDate(string text)
    {
        var t = text.Trim().Replace('/', '.');
        if (DateTime.TryParseExact(t, ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy"], Ru, DateTimeStyles.None, out var d))
        {
            if (d.Year < 100)
            {
                d = d.AddYears(2000);
            }

            return d.Date;
        }

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

        return DateTime.TryParse(t, Ru, DateTimeStyles.None, out d) ? d.Date : null;
    }
}

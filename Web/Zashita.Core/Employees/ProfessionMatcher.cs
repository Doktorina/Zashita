using System.Text;
using System.Text.RegularExpressions;

namespace Zashita.Core.Employees;

/// <summary>
/// Сопоставление профессий из 1С, Word-карт СОУТ и стандартов.
/// Учитывает разряды/категории, падежи, аббревиатуры и «хвосты»:
/// «Начальник ПТО» ≈ «Начальник производственно-технического отдела»,
/// «Генеральный директор предприятия» ≈ «генеральный директор».
/// </summary>
public static class ProfessionMatcher
{
    private static readonly string[] GradeNoise =
    [
        "разряд", "разряда", "разряду", "разрядом", "разряде",
        "категория", "категории", "категорию", "категорией", "кат",
        "класса", "класс", "классам",
        "и", "или"
    ];

    /// <summary>Хвосты, которые не меняют должность (часто в штате / на карте).</summary>
    private static readonly string[] OptionalTrailingNoise =
    [
        "предприятия", "предприятие", "организации", "организация",
        "общества", "общество", "учреждения", "учреждение",
        "компании", "компания", "фирмы", "фирма",
        "работника", "работник", "должности", "должность"
    ];

    // Длинные окончания первыми — «автомобильного» → «автомобильн».
    private static readonly string[] StemEndings =
    [
        "ого", "его", "ому", "ему", "ыми", "ими", "ами", "ями",
        "ов", "ев", "ам", "ям", "ах", "ях",
        "ой", "ый", "ий", "ая", "яя", "ое", "ее", "ые", "ие", "ую", "юю",
        "ом", "ем", "ём",
        "а", "я", "у", "ю", "ы", "и", "е", "о"
    ];

    /// <summary>Частые сокращения в должностях (токен → развёрнутая фраза).</summary>
    private static readonly Dictionary<string, string> KnownAbbreviations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["пто"] = "производственно технического отдела",
            ["гип"] = "главный инженер проекта",
            ["ахо"] = "административно хозяйственного отдела",
            ["ахч"] = "административно хозяйственной части",
            ["оот"] = "отдела охраны труда",
            ["отпб"] = "охраны труда и промышленной безопасности",
            ["укс"] = "управления капитального строительства",
            ["пдо"] = "производственно диспетчерского отдела"
        };

    /// <summary>
    /// «Архитектор (Офис БЦ Демидов)» → профессия + объект.
    /// Скобки с разрядом/категорией/совместительством не считаются объектом.
    /// </summary>
    public static (string Profession, string ObjectNote) SplitProfessionAndObject(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var match = Regex.Match(text, @"^(.*?)\s*\(([^()]*)\)\s*$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return (text, string.Empty);
        }

        var profession = match.Groups[1].Value.Trim();
        var inside = match.Groups[2].Value.Trim();
        if (profession.Length == 0 || inside.Length == 0 || LooksLikeGradeOrQualifierInParens(inside))
        {
            return (text, string.Empty);
        }

        return (profession, inside);
    }

    /// <summary>
    /// Совпадение профессии из файла с профессией в программе (штаб/отчёт).
    /// Сначала снимаются скобки с объектом, затем <see cref="Matches"/>.
    /// </summary>
    public static bool MatchesForImport(string? programProfession, string? fileProfession)
    {
        var (fileProf, _) = SplitProfessionAndObject(fileProfession);
        var (programProf, _) = SplitProfessionAndObject(programProfession);
        if (string.IsNullOrWhiteSpace(fileProf))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(programProf) || programProf == "—")
        {
            return true;
        }

        return Matches(programProf, fileProf);
    }

    private static bool LooksLikeGradeOrQualifierInParens(string inside)
    {
        var t = inside.Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Length == 0)
        {
            return true;
        }

        if (Regex.IsMatch(t, @"^\d{1,2}(\s*[-–—]\s*\d{1,2})?\s*(разр\.?|разряд|кат\.?|категория)?$"))
        {
            return true;
        }

        if (t.Contains("разря", StringComparison.Ordinal)
            || t.Contains("категор", StringComparison.Ordinal)
            || t.StartsWith("кат ", StringComparison.Ordinal)
            || t is "кат" or "разр" or "разр."
            || t.Contains("совм", StringComparison.Ordinal)
            || t.Contains("совместит", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static bool Matches(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var left = Normalize(expected);
        var right = Normalize(actual);
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftCanon = CanonicalKey(left);
        var rightCanon = CanonicalKey(right);
        if (leftCanon.Length > 0 &&
            rightCanon.Length > 0 &&
            string.Equals(leftCanon, rightCanon, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Одинаковый «корень» после снятия разряда/категории.
        var leftCore = CoreKey(left);
        var rightCore = CoreKey(right);
        if (leftCore.Length > 0 &&
            rightCore.Length > 0 &&
            string.Equals(leftCore, rightCore, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // «Начальник ПТО» ≈ «Начальник производственно-технического отдела»
        if (MatchesWithAcronyms(left, right))
        {
            return true;
        }

        var leftExpanded = ExpandKnownAbbreviations(left);
        var rightExpanded = ExpandKnownAbbreviations(right);
        if (!string.Equals(leftExpanded, left, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rightExpanded, right, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(CanonicalKey(leftExpanded), CanonicalKey(rightExpanded), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CanonicalKey(leftExpanded), CanonicalKey(right), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(CanonicalKey(left), CanonicalKey(rightExpanded), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (MatchesWithAcronyms(leftExpanded, rightExpanded) ||
                MatchesWithAcronyms(leftExpanded, right) ||
                MatchesWithAcronyms(left, rightExpanded))
            {
                return true;
            }
        }

        // «Генеральный директор предприятия» ⊇ «генеральный директор»
        if (IsSameJobWithOptionalNoise(leftExpanded, rightExpanded))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Канонический ключ: аббревиатуры развёрнуты, хвосты вроде «предприятия» сняты, стемминг.
    /// </summary>
    public static string CanonicalKey(string? value)
    {
        var expanded = ExpandKnownAbbreviations(Normalize(value));
        var tokens = MeaningfulTokens(expanded)
            .Select(Stem)
            .Where(t => t.Length > 0 && !IsGradeNoise(t) && !IsOptionalTrailingNoise(t))
            .ToArray();
        return tokens.Length == 0 ? string.Empty : string.Join(' ', tokens);
    }

    /// <summary>Нормализация для хранения/отображения: без «*», ё→е, схлопнутые пробелы.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value
            .Trim()
            .Replace('*', ' ')
            .Replace('ё', 'е')
            .Replace('Ё', 'Е')
            .Replace('\u00ad', ' ')
            .Replace('\u2010', '-')
            .Replace('\u2011', '-')
            .Replace('\u2012', '-')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2212', '-');

        // «173. Начальник…» / «№ 12 Инженер…» с карт/перечней.
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"^(?:№\s*)?\d{1,4}[\.\)]\s+",
            string.Empty);

        cleaned = cleaned.TrimEnd('.', ' ', ';', ',');
        return string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Ключ для сравнения: нормализация + без разрядов + стемминг токенов.</summary>
    public static string CoreKey(string? value)
    {
        var tokens = SignificantStemmedTokens(Normalize(value));
        return tokens.Length == 0 ? string.Empty : string.Join(' ', tokens);
    }

    public static string[] SignificantStemmedTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return Tokenize(Normalize(value))
            .Select(t => Stem(t.Replace('ё', 'е').Replace('Ё', 'Е')))
            .Where(t => t.Length > 0 && !IsGradeNoise(t))
            .ToArray();
    }

    /// <summary>
    /// Сопоставление с аббревиатурами по инициалам подряд идущих слов
    /// (ПТО ← производственно + технического + отдела).
    /// </summary>
    public static bool MatchesWithAcronyms(string? left, string? right)
    {
        var a = MeaningfulTokens(left);
        var b = MeaningfulTokens(right);
        if (a.Count == 0 || b.Count == 0)
        {
            return false;
        }

        return TokensAlignWithAcronyms(a, b) || TokensAlignWithAcronyms(b, a);
    }

    private static bool IsSameJobWithOptionalNoise(string left, string right)
    {
        var a = MeaningfulTokens(left)
            .Select(Stem)
            .Where(t => t.Length > 0 && !IsGradeNoise(t) && !IsOptionalTrailingNoise(t))
            .ToList();
        var b = MeaningfulTokens(right)
            .Select(Stem)
            .Where(t => t.Length > 0 && !IsGradeNoise(t) && !IsOptionalTrailingNoise(t))
            .ToList();
        if (a.Count == 0 || b.Count == 0)
        {
            return false;
        }

        if (a.Count == b.Count)
        {
            return a.Zip(b, TokensEqual).All(x => x);
        }

        // Более длинная формулировка = короткая + необязательный хвост (уже снят) или
        // только совпадение префикса при равном «ядре» должности (≥2 слова).
        var shorter = a.Count <= b.Count ? a : b;
        var longer = a.Count <= b.Count ? b : a;
        if (shorter.Count < 2)
        {
            return false;
        }

        for (var i = 0; i < shorter.Count; i++)
        {
            if (!TokensEqual(shorter[i], longer[i]))
            {
                return false;
            }
        }

        // Лишние слова справа — только «шум» (на случай если стем не снял падеж).
        return longer.Skip(shorter.Count).All(IsOptionalTrailingNoise);
    }

    private static bool TokensAlignWithAcronyms(
        IReadOnlyList<string> pattern,
        IReadOnlyList<string> expanded)
    {
        var i = 0;
        var j = 0;
        var usedAcronym = false;
        while (i < pattern.Count && j < expanded.Count)
        {
            if (TokensEqual(pattern[i], expanded[j]))
            {
                i++;
                j++;
                continue;
            }

            if (TryConsumeAcronym(pattern[i], expanded, j, out var take) && take >= 2)
            {
                i++;
                j += take;
                usedAcronym = true;
                continue;
            }

            if (TryConsumeAcronym(expanded[j], pattern, i, out var take2) && take2 >= 2)
            {
                j++;
                i += take2;
                usedAcronym = true;
                continue;
            }

            return false;
        }

        return usedAcronym && i == pattern.Count && j == expanded.Count;
    }

    private static bool TryConsumeAcronym(
        string acronym,
        IReadOnlyList<string> words,
        int start,
        out int count)
    {
        count = 0;
        var letters = new StringBuilder();
        foreach (var ch in acronym)
        {
            if (char.IsLetter(ch))
            {
                letters.Append(char.ToLowerInvariant(ch == 'ё' ? 'е' : ch));
            }
        }

        var ac = letters.ToString();
        // 2–6 букв: ПТО, УКС, ГИП…
        if (ac.Length is < 2 or > 6)
        {
            return false;
        }

        // Не считаем обычное слово аббревиатурой («отдел», «мастер»).
        if (ac.Length >= 5 && CountVowels(ac) >= 2)
        {
            return false;
        }

        // Словарь: «ПТО» → «производственно технического отдела»
        if (KnownAbbreviations.TryGetValue(ac, out var expansion))
        {
            var expected = Tokenize(expansion)
                .Select(t => t.ToLowerInvariant().Replace('ё', 'е'))
                .ToArray();
            if (start + expected.Length <= words.Count &&
                expected.Select((t, idx) => TokensEqual(t, words[start + idx])).All(x => x))
            {
                count = expected.Length;
                return true;
            }
        }

        if (start + ac.Length > words.Count)
        {
            return false;
        }

        for (var k = 0; k < ac.Length; k++)
        {
            var word = words[start + k];
            if (word.Length == 0)
            {
                return false;
            }

            var first = word[0] == 'ё' ? 'е' : word[0];
            if (char.ToLowerInvariant(first) != ac[k])
            {
                return false;
            }
        }

        count = ac.Length;
        return true;
    }

    private static int CountVowels(string lower)
    {
        var n = 0;
        foreach (var ch in lower)
        {
            if ("аеёиоуыэюя".Contains(ch))
            {
                n++;
            }
        }

        return n;
    }

    private static string ExpandKnownAbbreviations(string value)
    {
        var tokens = Tokenize(Normalize(value));
        if (tokens.Length == 0)
        {
            return value;
        }

        var parts = new List<string>();
        var changed = false;
        foreach (var token in tokens)
        {
            var key = token.ToLowerInvariant().Replace('ё', 'е');
            if (KnownAbbreviations.TryGetValue(key, out var expansion))
            {
                var expTokens = Tokenize(expansion);
                // «Отдел ПТО» → не дублировать «отдел … отдела»
                if (parts.Count > 0 && expTokens.Length > 1 &&
                    TokensEqual(parts[^1], expTokens[^1]))
                {
                    expansion = string.Join(' ', expTokens.Take(expTokens.Length - 1));
                }

                parts.Add(expansion);
                changed = true;
            }
            else
            {
                parts.Add(token);
            }
        }

        return changed ? string.Join(' ', parts) : value;
    }

    /// <summary>Одинаковый набор значимых слов (для подразделений: «Отдел ПТО» ≈ «… отдел»).</summary>
    public static bool SameSignificantTokens(string? left, string? right)
    {
        var a = CanonicalKey(left).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = CanonicalKey(right).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        var setA = a.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var setB = b.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (setA.SetEquals(setB))
        {
            return true;
        }

        // Одно название — уточнение другого (≥2 общих корня).
        var inter = setA.Intersect(setB, StringComparer.OrdinalIgnoreCase).Count();
        return inter >= 2 && (setA.IsSubsetOf(setB) || setB.IsSubsetOf(setA));
    }

    private static List<string> MeaningfulTokens(string? value) =>
        Tokenize(Normalize(value))
            .Select(t => t.ToLowerInvariant().Replace('ё', 'е'))
            .Where(t => t.Length > 0 && !IsGradeNoise(t))
            .ToList();

    private static bool TokensEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Stem(left), Stem(right), StringComparison.OrdinalIgnoreCase);

    private static string Stem(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var t = token.Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Length <= 3 || t.All(char.IsDigit))
        {
            return t;
        }

        foreach (var ending in StemEndings)
        {
            if (t.Length > ending.Length + 2 &&
                t.EndsWith(ending, StringComparison.Ordinal))
            {
                return t[..^ending.Length];
            }
        }

        return t;
    }

    private static bool IsGradeNoise(string token)
    {
        if (token.Length == 0)
        {
            return true;
        }

        if (token.All(char.IsDigit))
        {
            return true;
        }

        var lower = token.ToLowerInvariant().Replace('ё', 'е');
        return GradeNoise.Any(n => string.Equals(n, lower, StringComparison.Ordinal));
    }

    private static bool IsOptionalTrailingNoise(string token)
    {
        var lower = token.ToLowerInvariant().Replace('ё', 'е');
        var stem = Stem(lower);
        return OptionalTrailingNoise.Any(n =>
            string.Equals(n, lower, StringComparison.Ordinal) ||
            string.Equals(Stem(n), stem, StringComparison.Ordinal));
    }

    private static string[] Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        // Все виды дефисов/тире (в т.ч. неразрывный) — иначе «производственно‑технического»
        // остаётся одним токеном и «ПТО» не сопоставляется.
        var normalized = value
            .Replace('\u00ad', ' ') // soft hyphen
            .Replace('\u2010', '-')
            .Replace('\u2011', '-')
            .Replace('\u2012', '-')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2212', '-');

        return normalized.Split(
            [' ', '-', '/', ',', '.', '(', ')', '—', '–', ';', '+', '\\'],
            StringSplitOptions.RemoveEmptyEntries);
    }
}

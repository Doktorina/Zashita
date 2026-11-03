using System.Text.RegularExpressions;

namespace Zashita.Core.Training;

/// <summary>
/// Порядок строк программ в отчёте по обучению.
/// </summary>
public static class TrainingProgramDisplayOrder
{
    private const int UnknownBase = 100_000;

    public static int GetRank(string? programName)
    {
        var n = Normalize(programName);
        if (n.Length == 0)
        {
            return UnknownBase;
        }

        // 1–4. Инструктажи (вводные / первичные)
        if (IsIntroOt(n)) return 100;
        if (IsIntroFire(n)) return 200;
        if (IsPrimaryFire(n)) return 250;
        if (IsPrimaryOt(n)) return 300;

        // 5–9. Программы ОТ: А → Б → В → СИЗ → ПП (+ пакеты рядом)
        if (IsBundleAbvSizPp(n)) return 400;
        if (IsBundleAbSizPp(n)) return 405;
        if (IsProgramA(n)) return 410;
        if (IsProgramB(n)) return 420;
        if (IsProgramV(n)) return 430;
        if (IsSiz(n)) return 440;
        if (IsFirstAid(n)) return 450;

        // 10. Работы на высоте 1 / 2 / 3 гр.
        if (IsHeight(n)) return 500 + HeightGroupSubRank(n);

        // 11. Профобучение (стропальщик, люлька, арматурщик, …)
        if (IsProfTraining(n)) return 600;

        // 12. Электробезопасность («Группа по электробезопасности 1»)
        if (IsElectro(n)) return 700;

        // 13–15. Пожарная квалификация / проведение инструктажей
        if (IsFireSpecialist(n)) return 800;
        if (IsResponsiblePb(n)) return 900;
        if (IsConductingPbInstructions(n)) return 950;
        if (IsFireInstructions(n)) return 1_000;

        // 16–17. Повторные инструктажи
        if (IsRepeatOt(n)) return 1_100;
        if (IsRepeatFire(n)) return 1_200;

        // 18. Промбезопасность (А.1, Б.7.3, …)
        if (IsIndustrial(n)) return 1_300;

        return UnknownBase;
    }

    public static int Compare(string? left, string? right) =>
        GetRank(left) != GetRank(right)
            ? GetRank(left).CompareTo(GetRank(right))
            : string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');

    private static bool Contains(string n, string fragment) =>
        n.Contains(fragment, StringComparison.Ordinal);

    private static bool IsIntroOt(string n) =>
        Contains(n, "вводн")
        && (Contains(n, "охран") || Contains(n, " по от") || Contains(n, "охране труда"))
        && !Contains(n, "пожар")
        && !Contains(n, "противопожар")
        && !Contains(n, " по пб");

    private static bool IsIntroFire(string n) =>
        Contains(n, "вводн")
        && (Contains(n, "пожар") || Contains(n, "противопожар") || Contains(n, "по пожарной") || Contains(n, " по пб"));

    private static bool IsPrimaryFire(string n)
    {
        if (Contains(n, "повтор") || Contains(n, "внеплан") || Contains(n, "целев"))
        {
            return false;
        }

        return Contains(n, "первичн")
               && Contains(n, "рабоч")
               && (Contains(n, "пожар") || Contains(n, "противопожар") || Contains(n, " по пб") || Contains(n, "по пожарной"));
    }

    private static bool IsPrimaryOt(string n)
    {
        if (Contains(n, "повтор") || Contains(n, "внеплан") || Contains(n, "целев"))
        {
            return false;
        }

        if (Contains(n, "пожар") || Contains(n, "противопожар") || Contains(n, " по пб"))
        {
            return false;
        }

        if (Contains(n, "первичн") && Contains(n, "рабоч"))
        {
            return true;
        }

        if (Contains(n, "первичн") && Contains(n, "охран"))
        {
            return true;
        }

        return n is "инструктаж на рабочем месте"
               || (Contains(n, "инструктаж") && Contains(n, "рабоч") && Contains(n, "мест") && !Contains(n, "вводн"));
    }

    private static bool IsBundleAbvSizPp(string n) =>
        Contains(n, "а+б+в+сиз+пп");

    private static bool IsBundleAbSizPp(string n) =>
        Contains(n, "а+б+сиз+пп");

    /// <summary>«Программа А» — не путать с «программа» + другая буква.</summary>
    private static bool IsProgramA(string n) =>
        Regex.IsMatch(n, @"программа\s*а(\b|[,\s(]|$)")
        || Contains(n, "общие вопросы охраны труда")
        || n == "обучение по охране труда";

    private static bool IsProgramB(string n) =>
        Regex.IsMatch(n, @"программа\s*б(\b|[,\s(]|$)")
        || Contains(n, "вредных и (или) опасных")
        || Contains(n, "вредных и/или опасных")
        || Contains(n, "обучение безопасным методам");

    private static bool IsProgramV(string n) =>
        Regex.IsMatch(n, @"программа\s*в(\b|[,\s(]|$)")
        || Contains(n, "повышенной опасности");

    private static bool IsSiz(string n) =>
        Regex.IsMatch(n, @"программа\s*сиз")
        || (Contains(n, "сиз") && !Contains(n, "инструктаж"))
        || Contains(n, "средств индивидуальной");

    private static bool IsFirstAid(string n) =>
        Regex.IsMatch(n, @"программа\s*пп(\b|[,\s(]|$)")
        || Contains(n, "первой помощ")
        || Contains(n, "первую помощ")
        || n is "пп" or "программа пп";

    /// <summary>Работы на высоте 1/2/3 гр. — без стропальщика и люльки (они в профобучении, п.11).</summary>
    private static bool IsHeight(string n) =>
        Contains(n, "высот")
        && !Contains(n, "стропаль")
        && !Contains(n, "люльк");

    private static int HeightGroupSubRank(string n)
    {
        if (Contains(n, "3 гр") || Contains(n, "3гр") || Contains(n, "группа 3") || Contains(n, "3 группа"))
        {
            return 3;
        }

        if (Contains(n, "2 гр") || Contains(n, "2гр") || Contains(n, "группа 2") || Contains(n, "2 группа"))
        {
            return 2;
        }

        if (Contains(n, "1 гр") || Contains(n, "1гр") || Contains(n, "группа 1") || Contains(n, "1 группа")
            || Contains(n, "в составе бригад"))
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// 11) Любое профобучение: стропальщик, рабочий люльки, арматурщик, каменщик и др.
    /// Ставится после высоты и до электробезопасности.
    /// </summary>
    private static bool IsProfTraining(string n)
    {
        // Не перехватывать курсы, у которых есть свой слот ниже/выше.
        if (IsElectro(n) || IsIndustrial(n) || IsFireSpecialist(n) || IsResponsiblePb(n)
            || IsConductingPbInstructions(n) || IsHeight(n)
            || IsIntroOt(n) || IsIntroFire(n) || IsPrimaryOt(n) || IsPrimaryFire(n)
            || IsRepeatOt(n) || IsRepeatFire(n)
            || IsProgramA(n) || IsProgramB(n) || IsProgramV(n) || IsSiz(n) || IsFirstAid(n)
            || IsBundleAbvSizPp(n) || IsBundleAbSizPp(n))
        {
            return false;
        }

        if (Contains(n, "стропаль") || Contains(n, "люльк") || Contains(n, "такелаж"))
        {
            return true;
        }

        if (Contains(n, "профобуч")
            || Contains(n, "проф. обуч")
            || Contains(n, "проф обуч")
            || (Contains(n, "профессиональн") && Contains(n, "обучен")))
        {
            return true;
        }

        if (Contains(n, "разряд") || Contains(n, "разр."))
        {
            return true;
        }

        // Типичные рабочие / строительные специальности (с учётом падежей: каменщик/каменщиков)
        if (Contains(n, "арматурщик")
            || Contains(n, "каменщик")
            || Contains(n, "бетонщик")
            || Contains(n, "штукатур")
            || Contains(n, "плотник")
            || Contains(n, "маляр")
            || Contains(n, "сварщик")
            || Contains(n, "монтажник")
            || Contains(n, "бурильщик")
            || Contains(n, "кровельщик")
            || Contains(n, "изолировщик")
            || Contains(n, "облицовщик")
            || Contains(n, "плиточник")
            || Contains(n, "газорезчик")
            || Contains(n, "газосварщик")
            || Contains(n, "электросварщик")
            || Contains(n, "трубоукладчик")
            || Contains(n, "крановщик")
            || Contains(n, "слесарь")
            || Contains(n, "токарь")
            || Contains(n, "фрезеровщик")
            || Contains(n, "водитель")
            || Contains(n, "формовщик")
            || Contains(n, "жестянщик")
            || Contains(n, "стекольщик")
            || Contains(n, "паркетчик")
            || Contains(n, "кузнец"))
        {
            return true;
        }

        if (Contains(n, "крановщ")
            || Contains(n, "газорез")
            || Contains(n, "газосваро")
            || Contains(n, "электросваро")
            || (Contains(n, "машинист") && !Contains(n, "электро")))
        {
            return true;
        }

        // Оставшиеся короткие названия без маркеров курса ОТ/ПБ — тоже профобучение
        // (в стандарт добавляют «Каменщик», «Рабочий люльки» и т.п.).
        if (HasCourseOtMarkers(n))
        {
            return false;
        }

        // 1–6 слов без кодов промбез (А.1 / Б.9.3 / Г.2.1 …)
        if (n.Length is >= 3 and <= 80
            && TrainingScheduleExcelParser.TryExtractIndustrialSafetyCode(n) is null
            && !Contains(n, "инструктаж")
            && !Contains(n, "программ"))
        {
            return true;
        }

        return false;
    }

    private static bool HasCourseOtMarkers(string n) =>
        Contains(n, "инструктаж")
        || Contains(n, "программ")
        || Contains(n, "проверк")
        || Contains(n, "аттестац")
        || Contains(n, "пожар")
        || Contains(n, "противопожар")
        || Contains(n, "электробез")
        || Contains(n, "высот")
        || Contains(n, "промышленн")
        || Contains(n, "промбез")
        || Contains(n, " по пб")
        || (Contains(n, "обучен") && !Contains(n, "проф") && !Contains(n, "стропаль")
            && !Contains(n, "люльк") && !Contains(n, "каменщик") && !Contains(n, "арматурщик"));

    private static bool IsElectro(string n) =>
        Contains(n, "электробез")
        || Contains(n, "группа по электро")
        || Contains(n, "эл. безопас")
        || Contains(n, "эл безопас");

    private static bool IsFireSpecialist(string n) =>
        Contains(n, "специалист по пожарной профилактике")
        || (Contains(n, "специалист") && Contains(n, "пожар") && Contains(n, "профилактик"));

    private static bool IsResponsiblePb(string n) =>
        Contains(n, "ответственн")
        && (Contains(n, "обеспечен") || Contains(n, "пожар") || Contains(n, " пб"));

    private static bool IsConductingPbInstructions(string n)
    {
        if (!Contains(n, "проведен") || !Contains(n, "инструктаж"))
        {
            return false;
        }

        return Contains(n, " по пб")
               || Contains(n, "пб ")
               || n.EndsWith("пб")
               || Contains(n, "противопожар")
               || Contains(n, "пожарной безопасности");
    }

    private static bool IsFireInstructions(string n)
    {
        if (IsIntroFire(n) || IsRepeatFire(n) || IsFireSpecialist(n) || IsResponsiblePb(n) || IsConductingPbInstructions(n) || IsPrimaryFire(n))
        {
            return false;
        }

        return Contains(n, "противопожар")
               || Contains(n, "пожарной безопасности")
               || Contains(n, "пожарная безопасность")
               || Contains(n, "обучение мерам пожарной")
               || Contains(n, "аттестация по пожарной")
               || (Contains(n, "инструктаж") && Contains(n, "пожар"));
    }

    private static bool IsRepeatOt(string n) =>
        Contains(n, "повтор")
        && !Contains(n, "пожар")
        && !Contains(n, "противопожар")
        && !Contains(n, " по пб");

    private static bool IsRepeatFire(string n) =>
        Contains(n, "повтор")
        && (Contains(n, "пожар") || Contains(n, "противопожар") || Contains(n, " по пб"));

    private static bool IsIndustrial(string n) =>
        Contains(n, "промышленн")
        || Contains(n, "промбез")
        || TrainingScheduleExcelParser.TryExtractIndustrialSafetyCode(n) is not null;
}


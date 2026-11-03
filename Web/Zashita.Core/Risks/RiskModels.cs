namespace Zashita.Core.Risks;

public sealed class WorkplaceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string Name { get; set; } = string.Empty;
    public string WorkplaceNumber { get; set; } = string.Empty;
    public string MainWorkplaceNumber { get; set; } = string.Empty;
    public string ConditionsClass { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public int WorkerCount { get; set; }
    public string PlanYear { get; set; } = string.Empty;
    /// <summary>Код по ОК (ручной ввод).</summary>
    public string OkCode { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public bool IsUsed { get; set; } = true;
    /// <summary>
    /// Объект-владелец карты. Пусто / org-wide — карта из «оценки по организации»;
    /// иначе создана только на указанном объекте и не видна в других вкладках.
    /// </summary>
    public string OwnerObjectId { get; set; } = string.Empty;
    /// <summary>
    /// Карта скрыта на вкладке «Оценка рисков по организации», но может оставаться
    /// в составе объектов, куда уже входила.
    /// </summary>
    public bool HiddenOnOrganizationWide { get; set; }
    /// <summary>Дата проведения оценки рисков (срок действия — 5 лет).</summary>
    public DateTime? RiskAssessmentDate { get; set; }
    /// <summary>Номер карты идентификации опасностей / оценки рисков.</summary>
    public string RiskCardNumber { get; set; } = string.Empty;
    /// <summary>Общий риск по карте (ручной ввод).</summary>
    public string OverallRisk { get; set; } = string.Empty;
    /// <summary>Периодичность пересмотра (текст; уходит в отчёт ОПР).</summary>
    public string RiskPeriodicity { get; set; } = string.Empty;
    /// <summary>Явно отмечено: оценка рисков не проводилась (попадает в перечень без оценки).</summary>
    public bool RiskAssessmentNotConducted { get; set; }
    /// <summary>Строки карты идентификации опасностей / оценки рисков по этому РМ.</summary>
    public List<WorkplaceRiskLine> RiskLines { get; set; } = [];
    /// <summary>Дата карт / утверждения СОУТ (срок действия — 5 лет). Если пусто — ориентир по ConditionsClass.</summary>
    public DateTime? SoutAssessmentDate { get; set; }
    /// <summary>№ отчёта / карты СОУТ.</summary>
    public string SoutReportNumber { get; set; } = string.Empty;
    /// <summary>РМ-источник карты СОУТ (выбор в отчёте, как стандарт в обучении).</summary>
    public string LinkedSoutCardWorkplaceId { get; set; } = string.Empty;
    /// <summary>Доп. поля карты СОУТ (форма по методике).</summary>
    public SoutCardDetails SoutCard { get; set; } = new();
    /// <summary>Подана декларация соответствия (для классов 1–2 → бессрочно).</summary>
    public bool DeclarationOfConformity { get; set; }
    /// <summary>Явно отмечено: специальная оценка не проводилась.</summary>
    public bool SoutAssessmentNotConducted { get; set; }
    /// <summary>Вручную: требуется внеплановая СОУТ.</summary>
    public bool RequiresUnscheduledSout { get; set; }
    /// <summary>
    /// Ручная пометка цвета на «Карты СОУТ»: пусто = авто; green / red / blue.
    /// </summary>
    public string SoutTrafficMark { get; set; } = string.Empty;
    /// <summary>
    /// Карта создана кнопкой «Создать карту» (не оболочка из штата 1С).
    /// Не схлопывается CollapseBlankProfessionShells.
    /// </summary>
    public bool IsManualSoutCard { get; set; }
    /// <summary>
    /// Ручная пометка цвета на «Карта оценки рисков»: пусто = авто; green / red / blue.
    /// </summary>
    public string RiskTrafficMark { get; set; } = string.Empty;
    /// <summary>
    /// Оценки рисков по объектам (кроме вкладки «по организации» —
    /// она хранится в полях Risk* выше).
    /// Ключ = RiskAssessmentObject.Id.
    /// </summary>
    public Dictionary<string, WorkplaceRiskAssessmentData> ObjectAssessments { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Ознакомление работников с картой оценки рисков (по ключу сотрудника).</summary>
    public List<RiskCardFamiliarization> RiskFamiliarizations { get; set; } = [];
    /// <summary>Ознакомление работников с картой СОУТ (по ключу сотрудника).</summary>
    public List<RiskCardFamiliarization> SoutFamiliarizations { get; set; } = [];
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Ознакомление сотрудника с картой ОПР на рабочем месте.</summary>
public sealed class RiskCardFamiliarization
{
    /// <summary>Ключ: СНИЛС, иначе ExternalId, иначе ФИО.</summary>
    public string EmployeeKey { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public bool IsFamiliarized { get; set; }
    /// <summary>Сотрудник числится в профессии, но на этом объекте не работает.</summary>
    public bool NotWorkingOnObject { get; set; }
}

/// <summary>
/// Поля карты специальной оценки условий труда (форма по методике).
/// </summary>
public sealed class SoutCardDetails
{
    /// <summary>Дата составления карты.</summary>
    public DateTime? PreparedDate { get; set; }

    /// <summary>Количество и номера аналогичных рабочих мест.</summary>
    public string AnalogousWorkplaces { get; set; } = string.Empty;

    /// <summary>020 — численность на всех аналогичных рабочих местах.</summary>
    public int AnalogousWorkerCount { get; set; }

    /// <summary>020 — из них женщин.</summary>
    public int WorkersWomen { get; set; }

    /// <summary>022 — лиц в возрасте до 18 лет.</summary>
    public int WorkersUnder18 { get; set; }

    /// <summary>023 — инвалидов, допущенных к работам на данном РМ.</summary>
    public int WorkersDisabled { get; set; }

    /// <summary>021 — сотрудники организации, закреплённые за картой.</summary>
    public List<string> EmployeeIds { get; set; } = [];

    /// <summary>Оборудование на рабочем месте.</summary>
    public string Equipment { get; set; } = string.Empty;

    /// <summary>Используемые материалы и сырьё.</summary>
    public string Materials { get; set; } = string.Empty;

    /// <summary>Итоговый класс (условия труда) — дублирует ConditionsClass при необходимости.</summary>
    public string FinalClass { get; set; } = string.Empty;

    /// <summary>Заключение по результатам СОУТ.</summary>
    public string Conclusion { get; set; } = string.Empty;

    /// <summary>Рекомендуемые мероприятия.</summary>
    public string Recommendations { get; set; } = string.Empty;

    /// <summary>Примечание / комментарий эксперта.</summary>
    public string ExpertNote { get; set; } = string.Empty;

    /// <summary>Свободные строки карты (код → значение), кроме 010 и 021.</summary>
    public Dictionary<string, string> ExtraLines { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Описание строк карты СОУТ для UI.</summary>
public static class SoutCardFormLines
{
    public static IReadOnlyList<(string Code, string Title)> VisibleLines { get; } =
    [
        ("011", "Код профессии (должности) по ОК"),
        ("020", "Численность работающих"),
        ("021", "Закрепление работников"),
        ("022", "Используемое оборудование, материалы и сырье"),
        ("030", "Оценка условий труда по вредным (опасным) факторам")
    ];
}

/// <summary>Строка карты идентификации опасностей / оценки рисков на рабочем месте.</summary>
public sealed class WorkplaceRiskLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string HazardCatalogId { get; set; } = string.Empty;
    public string HazardNumber { get; set; } = string.Empty;
    public string HazardTitle { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Consequences { get; set; } = string.Empty;
    public string WhoMaySuffer { get; set; } = string.Empty;
    public string InitialRisk { get; set; } = string.Empty;
    public string ControlMeasures { get; set; } = string.Empty;
    public string ResidualRisk { get; set; } = string.Empty;
    public string Responsible { get; set; } = string.Empty;
    public string Probability { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Significance { get; set; } = string.Empty;
}

/// <summary>Справочники тяжести и вероятности для карты оценки рисков (матрица 5×5).</summary>
public static class WorkplaceRiskScale
{
    public static IReadOnlyList<string> SeverityOptions { get; } =
    [
        "Микротравмы, без снижения работоспособности",
        "Потеря или снижение трудоспособности до 3-х суток",
        "Временная утрата трудоспособности от 3-х до 30-и суток",
        "Тяжелый НС, потеря трудоспособности на длительный срок",
        "Повреждения, угрожающие жизни, инвалидность, летальный исход"
    ];

    public static IReadOnlyList<string> ProbabilityOptions { get; } =
    [
        "Вряд ли возможно",
        "Маловероятно",
        "Нехарактерно, но возможно",
        "Очень вероятно",
        "Скорее всего произойдет"
    ];

    public static IReadOnlyList<string> ResidualRiskOptions { get; } =
    [
        "допустимый риск",
        "недопустимый риск"
    ];

    /// <summary>Зоны матрицы: 1–4 низкий, 5–10 средний, 11–25 высокий.</summary>
    public static string ZoneForScore(int score) =>
        score switch
        {
            <= 0 => string.Empty,
            <= 4 => "низкий",
            <= 10 => "средний",
            _ => "высокий"
        };

    public static string ZoneCssClass(int score) =>
        score switch
        {
            <= 0 => string.Empty,
            <= 4 => "risk-zone-low",
            <= 10 => "risk-zone-mid",
            _ => "risk-zone-high"
        };

    public static int? WeightOfSeverity(string? value)
    {
        var mapped = MapSeverity(value);
        return IndexWeight(mapped ?? value, SeverityOptions);
    }

    public static int? WeightOfProbability(string? value)
    {
        var mapped = MapProbability(value);
        return IndexWeight(mapped ?? value, ProbabilityOptions);
    }

    /// <summary>
    /// Приводит формулировку тяжести из Word/Excel к каноническому пункту шкалы.
    /// </summary>
    public static string? MapSeverity(string? raw)
    {
        var key = NormalizeScaleKey(raw);
        if (key.Length == 0)
        {
            return null;
        }

        foreach (var opt in SeverityOptions)
        {
            if (string.Equals(NormalizeScaleKey(opt), key, StringComparison.Ordinal))
            {
                return opt;
            }
        }

        // Известные варианты из карт Word / старых шаблонов.
        foreach (var (alias, index) in SeverityAliases)
        {
            if (key.Contains(alias, StringComparison.Ordinal))
            {
                return SeverityOptions[index];
            }
        }

        // Порядок важен: «до 30» раньше «до 3» (иначе «до30» ловится как «до3»).
        if (key.Contains("летальн", StringComparison.Ordinal)
            || key.Contains("инвалид", StringComparison.Ordinal)
            || key.Contains("угрожающжизн", StringComparison.Ordinal)
            || key.Contains("угрожаетжизн", StringComparison.Ordinal))
        {
            return SeverityOptions[4];
        }

        if (key.Contains("тяжелыйнс", StringComparison.Ordinal)
            || (key.Contains("длительн", StringComparison.Ordinal)
                && (key.Contains("трудоспособ", StringComparison.Ordinal) || key.Contains("нс", StringComparison.Ordinal))))
        {
            return SeverityOptions[3];
        }

        // «от 3 до 30», «до 30 суток», «временная … 30»
        if (key.Contains("до30", StringComparison.Ordinal)
            || key.Contains("от3до", StringComparison.Ordinal)
            || (key.Contains("30", StringComparison.Ordinal)
                && (key.Contains("временн", StringComparison.Ordinal)
                    || key.Contains("от3", StringComparison.Ordinal)
                    || key.Contains("оттрех", StringComparison.Ordinal))))
        {
            return SeverityOptions[2];
        }

        // «до 3 суток» — но не фрагмент «до 30»
        if (key.Contains("до3х", StringComparison.Ordinal)
            || key.Contains("дотрех", StringComparison.Ordinal)
            || key.Contains("до3сут", StringComparison.Ordinal)
            || (key.Contains("до3", StringComparison.Ordinal) && !key.Contains("до30", StringComparison.Ordinal)))
        {
            return SeverityOptions[1];
        }

        if (key.Contains("микротравм", StringComparison.Ordinal))
        {
            return SeverityOptions[0];
        }

        // Подстрока / общий префикс — только если однозначно длинное совпадение.
        string? best = null;
        var bestScore = 0;
        foreach (var opt in SeverityOptions)
        {
            var optKey = NormalizeScaleKey(opt);
            if (optKey.Length < 12)
            {
                continue;
            }

            if (key.Contains(optKey, StringComparison.Ordinal) || optKey.Contains(key, StringComparison.Ordinal))
            {
                var score = Math.Min(key.Length, optKey.Length);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = opt;
                }
            }
        }

        return bestScore >= 18 ? best : null;
    }

    /// <summary>Приводит формулировку вероятности из Word к каноническому пункту шкалы.</summary>
    public static string? MapProbability(string? raw)
    {
        var key = NormalizeScaleKey(raw);
        if (key.Length == 0)
        {
            return null;
        }

        foreach (var opt in ProbabilityOptions)
        {
            if (string.Equals(NormalizeScaleKey(opt), key, StringComparison.Ordinal))
            {
                return opt;
            }
        }

        foreach (var (alias, index) in ProbabilityAliases)
        {
            if (key.Contains(alias, StringComparison.Ordinal))
            {
                return ProbabilityOptions[index];
            }
        }

        if (key.Contains("скореевсего", StringComparison.Ordinal) || key.Contains("неизбежн", StringComparison.Ordinal))
        {
            return ProbabilityOptions[4];
        }

        if (key.Contains("оченьвероятн", StringComparison.Ordinal) || key.Contains("вероятно", StringComparison.Ordinal))
        {
            return ProbabilityOptions[3];
        }

        if (key.Contains("нехарактерн", StringComparison.Ordinal) || key.Contains("новозможн", StringComparison.Ordinal))
        {
            return ProbabilityOptions[2];
        }

        if (key.Contains("маловероятн", StringComparison.Ordinal))
        {
            return ProbabilityOptions[1];
        }

        if (key.Contains("врядли", StringComparison.Ordinal) || key.Contains("невозможн", StringComparison.Ordinal))
        {
            return ProbabilityOptions[0];
        }

        string? best = null;
        var bestScore = 0;
        foreach (var opt in ProbabilityOptions)
        {
            var optKey = NormalizeScaleKey(opt);
            if (key.Contains(optKey, StringComparison.Ordinal) || optKey.Contains(key, StringComparison.Ordinal))
            {
                var score = Math.Min(key.Length, optKey.Length);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = opt;
                }
            }
        }

        return bestScore >= 10 ? best : null;
    }

    private static readonly (string Alias, int Index)[] SeverityAliases =
    [
        ("поврежденияугрожающиежизни", 4),
        ("угрожающиежизни", 4),
        ("летальныйисход", 4),
        ("тяжелыйнс", 3),
        ("потерютрудоспособностинадлительный", 3),
        ("временнаяутрататрудоспособностиот3", 2),
        ("травмаилиутрататрудоспособностиот3", 2),
        ("утрататрудоспособностиот3до30", 2),
        ("утрататрудоспособностиот3хдо30", 2),
        ("потеряилиснижениетрудоспособностидо3", 1),
        ("снижениетрудоспособностидо3", 1),
        ("микротравмыбесснижения", 0),
        ("микротравмыбезутраты", 0),
        ("микротравмыбезоказания", 0),
        ("безсниженияработоспособности", 0)
    ];

    private static readonly (string Alias, int Index)[] ProbabilityAliases =
    [
        ("скореевсегопроизойдет", 4),
        ("оченьвероятно", 3),
        ("нехарактерноновозможно", 2),
        ("маловероятно", 1),
        ("врядливозможно", 0)
    ];

    private static string NormalizeScaleKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant().Replace('ё', 'е'))
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    public static int? ComputeScore(string? severity, string? probability)
    {
        var s = WeightOfSeverity(severity);
        var p = WeightOfProbability(probability);
        if (s is null || p is null)
        {
            return null;
        }

        return s.Value * p.Value;
    }

    /// <summary>«-» / «нет» — периодичности нет, срок пересмотра не считается.</summary>
    public static bool IsNoPeriodicity(string? periodicity)
    {
        var t = (periodicity ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        return t is "-" or "—" or "–" or "нет" or "нет периодичности" or "без периодичности";
    }

    /// <summary>Разбор текста периодичности в месяцы для отчёта ОПР. 0 — периодичности нет.</summary>
    public static int? ParsePeriodicityMonths(string? periodicity)
    {
        var t = (periodicity ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Length == 0)
        {
            return null;
        }

        if (IsNoPeriodicity(periodicity))
        {
            return 0;
        }

        // «1 раз в 5 лет», «раз в пять лет», «5 лет» — берём число лет, а не «1» из «1 раз».
        var years = TryParseYears(t);
        if (years is > 0)
        {
            return years.Value * 12;
        }

        if (t.Contains("ежемес", StringComparison.Ordinal) ||
            t.Contains("каждый месяц", StringComparison.Ordinal) ||
            t.Contains("1 раз в месяц", StringComparison.Ordinal))
        {
            return 1;
        }

        if (t.Contains("квартал", StringComparison.Ordinal) ||
            t.Contains("3 месяц", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(t, @"раз\s+в\s+3\b"))
        {
            return 3;
        }

        if (t.Contains("полугод", StringComparison.Ordinal) ||
            t.Contains("6 месяц", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(t, @"раз\s+в\s+6\b"))
        {
            return 6;
        }

        if (t.Contains("ежегод", StringComparison.Ordinal) ||
            t.Contains("12 месяц", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(t, @"раз\s+в\s+год\b") ||
            (t.Contains("год", StringComparison.Ordinal) && !t.Contains("лет", StringComparison.Ordinal) && !ContainsYearWord(t)))
        {
            return 12;
        }

        if (t.Contains("месяц", StringComparison.Ordinal) || t.Contains("мес", StringComparison.Ordinal))
        {
            var afterRaz = System.Text.RegularExpressions.Regex.Match(t, @"раз\s+в\s+(\d+)");
            if (afterRaz.Success && int.TryParse(afterRaz.Groups[1].Value, out var m1) && m1 is > 0 and <= 120)
            {
                return m1;
            }

            var digits = new string(t.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var m2) && m2 is > 0 and <= 120)
            {
                return m2;
            }

            return 12;
        }

        return null;
    }

    private static int? TryParseYears(string t)
    {
        var digitYears = System.Text.RegularExpressions.Regex.Match(
            t, @"(\d+)\s*(?:лет|года|год)\b");
        if (digitYears.Success &&
            int.TryParse(digitYears.Groups[1].Value, out var y1) &&
            y1 is > 0 and <= 20)
        {
            return y1;
        }

        var wordYears = System.Text.RegularExpressions.Regex.Match(
            t, @"(один|одна|одно|два|две|три|четыре|пять|шесть|семь|восемь|девять|десять)\s*(?:лет|года|год)\b");
        if (wordYears.Success)
        {
            return WordToNumber(wordYears.Groups[1].Value);
        }

        var razVWord = System.Text.RegularExpressions.Regex.Match(
            t, @"раз\s+в\s+(один|одна|одно|два|две|три|четыре|пять|шесть|семь|восемь|девять|десять)(?:\s|$)");
        if (razVWord.Success && (t.Contains("лет", StringComparison.Ordinal) || t.Contains("год", StringComparison.Ordinal)))
        {
            return WordToNumber(razVWord.Groups[1].Value);
        }

        var razVDigit = System.Text.RegularExpressions.Regex.Match(
            t, @"раз\s+в\s+(\d+)(?:\s|$)");
        if (razVDigit.Success &&
            (t.Contains("лет", StringComparison.Ordinal) || t.Contains("год", StringComparison.Ordinal)) &&
            int.TryParse(razVDigit.Groups[1].Value, out var y2) &&
            y2 is > 0 and <= 20)
        {
            return y2;
        }

        return null;
    }

    private static bool ContainsYearWord(string t) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            t, @"\b(один|одна|одно|два|две|три|четыре|пять|шесть|семь|восемь|девять|десять)\b");

    private static int? WordToNumber(string word) => word.Trim().ToLowerInvariant() switch
    {
        "один" or "одна" or "одно" => 1,
        "два" or "две" => 2,
        "три" => 3,
        "четыре" => 4,
        "пять" => 5,
        "шесть" => 6,
        "семь" => 7,
        "восемь" => 8,
        "девять" => 9,
        "десять" => 10,
        _ => null
    };

    private static int? IndexWeight(string? value, IReadOnlyList<string> options)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length == 0 || raw is "-" or "—" or "–")
        {
            return null;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], raw, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (raw.StartsWith(options[i], StringComparison.OrdinalIgnoreCase) ||
                options[i].StartsWith(raw, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return null;
    }
}

public sealed class WorkplaceAssignmentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string OperationType { get; set; } = "Закрепление";
    public string Department { get; set; } = string.Empty;
    public string Employee { get; set; } = string.Empty;
    public string WorkplaceId { get; set; } = string.Empty;
    public string WorkplaceName { get; set; } = string.Empty;
    public DateTime? AssignedFrom { get; set; }
    public DateTime? AssignedTo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class WorkplaceAssignmentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string OperationType { get; set; } = "Закрепление";
    public string Department { get; set; } = string.Empty;
    public bool IsPosted { get; set; }
    public string Comment { get; set; } = string.Empty;
    public List<WorkplaceAssignmentLine> Lines { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class WorkplaceAssignmentLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Employee { get; set; } = string.Empty;
    public string WorkplaceId { get; set; } = string.Empty;
    public string WorkplaceName { get; set; } = string.Empty;
    public string WorkplaceNumber { get; set; } = string.Empty;
    public DateTime? AssignedFrom { get; set; }
    public DateTime? AssignedTo { get; set; }
}

public sealed class SoutAnnualPlanDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime PeriodStart { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateTime PeriodEnd { get; set; } = new(DateTime.Today.Year, 12, 31);
    public int PlanYear { get; set; } = DateTime.Today.Year;
    public string PlanType { get; set; } = "Офис";
    public string Scenario { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Status { get; set; } = "В подготовке";
    public bool ApprovedByAuthor { get; set; }
    public bool IsPosted { get; set; }
    public List<SoutAnnualPlanGeneralRow> GeneralRows { get; set; } = [];
    public List<SoutAnnualPlanDetailRow> DetailRows { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SoutAnnualPlanGeneralRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssessmentType { get; set; } = "Специальная оценка условий труда";
    public string Contractor { get; set; } = string.Empty;
}

public sealed class SoutCatalogRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string InventoryNumber { get; set; } = string.Empty;
    public string RegistrationNumber { get; set; } = string.Empty;
    public string StatePlate { get; set; } = string.Empty;
    public string FactoryNumber { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public DateTime? InstallationDate { get; set; }
    public string Manufacturer { get; set; } = string.Empty;
    public string FactorsSet { get; set; } = string.Empty;
    /// <summary>Текстовая привязка (номер/название РМ) — совместимость со старыми данными.</summary>
    public string InstallationLocation { get; set; } = string.Empty;
    /// <summary>Явные Id рабочих мест, к которым прикреплено оборудование / материал.</summary>
    public List<string> WorkplaceIds { get; set; } = [];
    public string Instruction { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SoutCommissionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Краткий текст состава (ФИО через запятую) — для списка и старых данных.</summary>
    public string Composition { get; set; } = string.Empty;
    /// <summary>Структурированный состав: статус, должность, ФИО.</summary>
    public List<SoutCommissionMember> Members { get; set; } = [];
    public DateTime? OrderDate { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    /// <summary>Дата утверждения состава.</summary>
    public DateTime? CompositionApprovalDate { get; set; }
    /// <summary>Периодичность проверки состава, мес.</summary>
    public int? CompositionCheckPeriodMonths { get; set; }
    /// <summary>Из них аттестовано в Ростехнадзоре.</summary>
    public int? CertifiedInRostechnadzorCount { get; set; }
    /// <summary>Соответствие требованиям состава.</summary>
    public string CompositionCompliance { get; set; } = string.Empty;
    public bool IsInactive { get; set; }
    /// <summary>Правки печатной формы приказа (шапка, БИК и т.п.).</summary>
    public Dictionary<string, string> PrintOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public static class SoutCommissionListHelpers
{
    public static string ChairmanDisplay(SoutCommissionRecord record)
    {
        var chair = SoutCommissionMemberHelpers.FindChairman(record.Members);
        if (chair is null || string.IsNullOrWhiteSpace(chair.Name))
        {
            return "—";
        }

        var formatted = RiskCommissionResolver.FormatChairmanForOrder(chair);
        var pos = chair.Position?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(pos) && formatted == chair.Name.Trim()
            ? chair.Name.Trim()
            : formatted;
    }

    public static string MembersDisplay(SoutCommissionRecord record)
    {
        var lines = (record.Members ?? [])
            .Where(m => !SoutCommissionMemberHelpers.IsChairmanStatus(m.Status))
            .Select(m =>
            {
                var name = m.Name?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return string.Empty;
                }

                var pos = m.Position?.Trim() ?? string.Empty;
                return string.IsNullOrWhiteSpace(pos) ? name : $"{name} ({pos})";
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        if (lines.Count == 0 && !string.IsNullOrWhiteSpace(record.Composition))
        {
            return record.Composition;
        }

        return lines.Count == 0 ? "—" : string.Join(", ", lines);
    }

    public static int TotalMembers(SoutCommissionRecord record) =>
        (record.Members ?? []).Count(m => !string.IsNullOrWhiteSpace(m.Name));

    public static string OrderNumberAndDate(SoutCommissionRecord record)
    {
        var num = record.OrderNumber?.Trim() ?? string.Empty;
        var date = record.OrderDate?.ToString("dd.MM.yyyy") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(num) && string.IsNullOrWhiteSpace(date))
        {
            return "—";
        }

        if (string.IsNullOrWhiteSpace(num))
        {
            return date;
        }

        return string.IsNullOrWhiteSpace(date) ? $"№ {num}" : $"№ {num} от {date}";
    }

    public static (DateTime? Planned, int? DaysLeft, string Status, string Css) ComputeDeadlines(
        SoutCommissionRecord record,
        DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        DateTime? planned = null;
        if (record.CompositionApprovalDate.HasValue && record.CompositionCheckPeriodMonths is > 0)
        {
            planned = record.CompositionApprovalDate.Value.Date.AddMonths(record.CompositionCheckPeriodMonths.Value);
        }

        int? daysLeft = planned.HasValue ? (int)(planned.Value.Date - today).TotalDays : null;
        if (!daysLeft.HasValue)
        {
            return (planned, null, "—", "");
        }

        if (daysLeft.Value < 0)
        {
            return (planned, daysLeft, "Просрочено", "eb-status-overdue");
        }

        if (daysLeft.Value <= 30)
        {
            return (planned, daysLeft, "Скоро проверка", "eb-status-ready");
        }

        return (planned, daysLeft, "В норме", "eb-status-ok");
    }
}

/// <summary>Участник комиссии СОУТ.</summary>
public sealed class SoutCommissionMember
{
    /// <summary>Например: «Председатель комиссии по СОУТ», «Член комиссии 1».</summary>
    public string Status { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public static class SoutCommissionMemberHelpers
{
    public const string ChairmanStatus = "Председатель комиссии по СОУТ";
    public const string RiskChairmanStatus = "Председатель комиссии по оценке рисков";

    public static string MemberStatus(int number) => $"Член комиссии {number}";

    public static bool IsChairmanStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return string.Equals(status, ChairmanStatus, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, RiskChairmanStatus, StringComparison.OrdinalIgnoreCase)
               || status.Contains("Председатель комиссии", StringComparison.OrdinalIgnoreCase);
    }

    public static SoutCommissionMember? FindChairman(IEnumerable<SoutCommissionMember>? members) =>
        (members ?? []).FirstOrDefault(m => IsChairmanStatus(m.Status));

    public static IReadOnlyList<string> StatusOptions(int memberSlots = 12)
    {
        var list = new List<string> { ChairmanStatus, RiskChairmanStatus };
        for (var i = 1; i <= Math.Max(1, memberSlots); i++)
        {
            list.Add(MemberStatus(i));
        }

        return list;
    }

    public static string BuildCompositionText(IEnumerable<SoutCommissionMember>? members) =>
        string.Join(", ",
            (members ?? [])
                .Select(m => m.Name?.Trim() ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n)));

    public static void SyncComposition(SoutCommissionRecord record)
    {
        record.Members ??= [];
        if (record.Members.Count > 0)
        {
            record.Composition = BuildCompositionText(record.Members);
            return;
        }

        if (!string.IsNullOrWhiteSpace(record.Composition))
        {
            record.Members = MigrateFromCompositionText(record.Composition);
            record.Composition = BuildCompositionText(record.Members);
        }
    }

    public static List<SoutCommissionMember> MigrateFromCompositionText(string composition)
    {
        var names = (composition ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var members = new List<SoutCommissionMember>();
        for (var i = 0; i < names.Count; i++)
        {
            members.Add(new SoutCommissionMember
            {
                Status = i == 0 ? ChairmanStatus : MemberStatus(i),
                Name = names[i],
                Position = string.Empty
            });
        }

        return members;
    }

    public static string NextDefaultStatus(IReadOnlyList<SoutCommissionMember> existing)
    {
        if (existing.Count == 0 ||
            existing.All(m => !IsChairmanStatus(m.Status)))
        {
            return ChairmanStatus;
        }

        var maxMember = 0;
        foreach (var member in existing)
        {
            var status = member.Status?.Trim() ?? string.Empty;
            if (status.StartsWith("Член комиссии", StringComparison.OrdinalIgnoreCase))
            {
                var digits = new string(status.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n) && n > maxMember)
                {
                    maxMember = n;
                }
            }
        }

        return MemberStatus(maxMember + 1);
    }
}

public sealed class SoutImportColumnMapping
{
    public int ColumnNumber { get; set; }
    public string AttributeName { get; set; } = string.Empty;
}

public sealed class SoutImportSettings
{
    public string Organization { get; set; } = string.Empty;
    public string DepartmentForDocuments { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public DateTime? ApprovalDate { get; set; }
    public string PreparationDocumentId { get; set; } = string.Empty;
    public string ContractorOrganization { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = "Плановая";
    public int FirstDataRow { get; set; } = 2;
    public bool CreateDepartments { get; set; }
    public bool CreateWorkplaces { get; set; } = true;
    public bool MergeWorkplaceAnalogs { get; set; } = true;
    public bool CreateProfessions { get; set; }
    public bool CreateAssessmentCard { get; set; } = true;
    public bool CreatePpeAssessment { get; set; }
    public bool CreatePreparationDocument { get; set; }
    public bool CreateResultsDocument { get; set; } = true;
    public bool CreateMeasurementProtocol { get; set; }
    public List<SoutImportColumnMapping> ColumnMappings { get; set; } = [];
}

public sealed class RiskAssessmentStatusRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ParentId { get; set; }
    public bool IsGroup { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectCode { get; set; } = string.Empty;
    public string AssessmentResult { get; set; } = "Не проходило оценку рисков";
    public DateTime? CreatedDate { get; set; }
    public DateTime? ConductedDate { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Significance { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int WithoutAssessment { get; set; }
    public int WithAssessment { get; set; }
    public int MeasuresCurrent { get; set; }
    public int MeasuresFuture { get; set; }
    public bool IsHighlighted { get; set; }
}

public sealed class SoutAnnualPlanDetailRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Employee { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public DateTime? PreviousDate { get; set; }
    public DateTime? NextDate { get; set; }
    public DateTime? PlannedDate { get; set; }
    public string Contractor { get; set; } = string.Empty;
    public string WorkplaceId { get; set; } = string.Empty;
    public string WorkplaceNumber { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
}

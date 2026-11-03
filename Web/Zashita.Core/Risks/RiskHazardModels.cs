namespace Zashita.Core.Risks;

/// <summary>Строка отчёта «Состояние по управлению профессиональными рисками».</summary>
public sealed class RiskHazardStatusRow
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public int Level { get; set; }
    public bool IsGroup { get; set; }

    /// <summary>Структура / опасность (название узла или опасности).</summary>
    public string Title { get; set; } = "";

    public DateTime? EmploymentDate { get; set; }
    public string WhoMaySuffer { get; set; } = "";
    public string InitialRisk { get; set; } = "";
    public string ControlMeasures { get; set; } = "";
    public string ResidualRisk { get; set; } = "";
    /// <summary>Периодичность пересмотра (месяцев), ручной ввод.</summary>
    public int? ReviewPeriodMonths { get; set; }
    public DateTime? AssessmentDate { get; set; }
    public DateTime? ReviewDate { get; set; }
    public string Responsible { get; set; } = "";
    /// <summary>Авто: Проведена / Не проведена оценка рисков / Просрочено.</summary>
    public string Status { get; set; } = "";

    /// <summary>green / yellow / red / none</summary>
    public string Traffic { get; set; } = "none";

    public string Organization { get; set; } = "";
    public string Department { get; set; } = "";
    public string Profession { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string HazardKey { get; set; } = "";
}

/// <summary>Сохранённая оценка опасности (редактируемые поля отчёта).</summary>
public sealed class RiskHazardAssessmentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Organization { get; set; } = "";
    public string Department { get; set; } = "";
    public string Profession { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string HazardTitle { get; set; } = "";
    public string WhoMaySuffer { get; set; } = "";
    public string InitialRisk { get; set; } = "";
    public string ControlMeasures { get; set; } = "";
    public string ResidualRisk { get; set; } = "";
    public int? ReviewPeriodMonths { get; set; }
    public DateTime? AssessmentDate { get; set; }
    public DateTime? ReviewDate { get; set; }
    public string Responsible { get; set; } = "";
    /// <summary>Устарело: статус теперь считается автоматически.</summary>
    public string Status { get; set; } = "";
}

public sealed class RiskHazardAssessmentDocument
{
    public List<RiskHazardAssessmentRecord> Items { get; set; } = [];
}

/// <summary>Пункт справочника «Реестр опасностей».</summary>
public sealed class RiskHazardCatalogItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Номер опасности (произвольный код из реестра / локальный номер).</summary>
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string DefaultWhoMaySuffer { get; set; } = "работник";
    public string DefaultMeasures { get; set; } = "";
    public string DefaultInitialRisk { get; set; } = "Средний";
    public string DefaultResidualRisk { get; set; } = "Низкий";
    public string ProfessionHint { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class RiskHazardCatalogDocument
{
    public List<RiskHazardCatalogItem> Items { get; set; } = [];
}

/// <summary>Сравнение номеров опасностей вида 3.1, 27.1, 29.4 (по частям, численно).</summary>
public static class RiskHazardNumberComparer
{
    public static int CompareNumbers(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 && b.Length == 0)
        {
            return 0;
        }

        if (a.Length == 0)
        {
            return 1;
        }

        if (b.Length == 0)
        {
            return -1;
        }

        var aParts = SplitParts(a);
        var bParts = SplitParts(b);
        var max = Math.Max(aParts.Count, bParts.Count);
        for (var i = 0; i < max; i++)
        {
            if (i >= aParts.Count)
            {
                return -1;
            }

            if (i >= bParts.Count)
            {
                return 1;
            }

            var partCompare = ComparePart(aParts[i], bParts[i]);
            if (partCompare != 0)
            {
                return partCompare;
            }
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static List<RiskHazardCatalogItem> OrderByNumber(IEnumerable<RiskHazardCatalogItem> items) =>
        items
            .OrderBy(item => item, ItemComparer.Instance)
            .ToList();

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().TrimEnd('.');

    private static List<string> SplitParts(string value) =>
        value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static int ComparePart(string left, string right)
    {
        var leftIsNum = int.TryParse(left, out var leftNum);
        var rightIsNum = int.TryParse(right, out var rightNum);
        if (leftIsNum && rightIsNum)
        {
            return leftNum.CompareTo(rightNum);
        }

        if (leftIsNum)
        {
            return -1;
        }

        if (rightIsNum)
        {
            return 1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ItemComparer : IComparer<RiskHazardCatalogItem>
    {
        public static readonly ItemComparer Instance = new();

        public int Compare(RiskHazardCatalogItem? x, RiskHazardCatalogItem? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return 1;
            }

            if (y is null)
            {
                return -1;
            }

            var byNumber = CompareNumbers(x.Number, y.Number);
            return byNumber != 0
                ? byNumber
                : string.Compare(x.Title, y.Title, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}

public static class RiskHazardWhoOptions
{
    public static IReadOnlyList<string> All { get; } =
    [
        "работник",
        "работники рядом",
        "подрядчик",
        "третьи лица"
    ];

    public static IReadOnlyList<string> Parse(string? raw) =>
        (raw ?? "")
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string Join(IEnumerable<string>? values) =>
        string.Join(", ", (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
}

public static class RiskHazardStatuses
{
    public const string Done = "Проведена";
    public const string NeedsAssessment = "Не проведена оценка рисков";
    public const string Overdue = "Просрочено";
    public const string NotConducted = "Не проведена оценка рисков";

    public static IReadOnlyList<string> All { get; } = [Done, NotConducted, Overdue];
}

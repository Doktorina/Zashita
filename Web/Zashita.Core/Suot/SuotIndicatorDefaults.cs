namespace Zashita.Core.Suot;

public static class SuotIndicatorDefaults
{
    public static IReadOnlyList<string> IndicatorTypes { get; } =
        ["Количественный", "Коэффициент", "Денежный"];

    public static IReadOnlyList<string> Units { get; } =
        ["шт", "%", "руб.", "чел"];

    public static IReadOnlyList<string> ActivityDirections { get; } =
    [
        "Мероприятия",
        "Промышленная безопасность",
        "Пожарная безопасность",
        "Бюджет",
        "Риски",
        "Специальная оценка",
        "Медосмотры",
        "Обучение",
        "Несчастные случаи"
    ];

    public static List<SuotIndicator> CreateSeed()
    {
        var workersGroupId = NewId();
        var list = new List<SuotIndicator>
        {
            Item("000000001", "Аварийные ситуации", "Количественный", "шт", "Мероприятия"),
            Item("000000002", "Затраты на охрану труда", "Денежный", "руб.", "Бюджет"),
            Item("000000003", "Выполнение запланированных мероприятий", "Коэффициент", "%", "Мероприятия"),
            Group(workersGroupId, "000000010", "Количество работников по классам условий труда", "Специальная оценка"),
            Child(workersGroupId, "000000011", "Класс 1", "чел"),
            Child(workersGroupId, "000000012", "Класс 2", "чел"),
            Child(workersGroupId, "000000013", "Класс 3.1", "чел"),
            Child(workersGroupId, "000000014", "Класс 3.2", "чел"),
            Child(workersGroupId, "000000015", "Класс 3.3", "чел"),
            Child(workersGroupId, "000000016", "Класс 3.4", "чел"),
            Child(workersGroupId, "000000017", "Класс 4", "чел"),
            Item("000000020", "Количество работников по типам компенсаций", "Количественный", "чел", "Специальная оценка",
                "Дополнительный отпуск, досрочная пенсия, медосмотры и др.")
        };

        return list;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static SuotIndicator Item(string code, string name, string type, string unit, string direction, string? description = null) =>
        new()
        {
            Id = NewId(),
            Code = code,
            Name = name,
            ShortName = name,
            IndicatorType = type,
            Unit = unit,
            ActivityDirection = direction,
            Description = description ?? string.Empty
        };

    private static SuotIndicator Group(string id, string code, string name, string direction) =>
        new()
        {
            Id = id,
            Code = code,
            Name = name,
            ShortName = name,
            IsGroup = true,
            IndicatorType = "Количественный",
            Unit = "чел",
            ActivityDirection = direction
        };

    private static SuotIndicator Child(string parentId, string code, string name, string unit) =>
        new()
        {
            Id = NewId(),
            Code = code,
            Name = name,
            ShortName = name,
            ParentId = parentId,
            IndicatorType = "Количественный",
            Unit = unit,
            ActivityDirection = "Специальная оценка"
        };
}

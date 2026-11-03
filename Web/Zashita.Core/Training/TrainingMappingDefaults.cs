namespace Zashita.Core.Training;

internal static class TrainingMappingSeedBuilder
{
    internal static IEnumerable<TrainingProgramMappingLine> Lines(
        string source,
        params (string Kind, string Program)[] mappings)
    {
        var index = 0;
        foreach (var (kind, program) in mappings)
        {
            index++;
            yield return new TrainingProgramMappingLine
            {
                Id = $"map-{source.GetHashCode(StringComparison.Ordinal):x}-{index}",
                SourceProgram = source,
                TargetKind = kind,
                TargetProgram = program
            };
        }
    }
}

public static class TrainingWorkTypeMappingDefaults
{
    public static List<TrainingProgramMappingLine> CreateSeed() =>
    [
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения газоопасных работ",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение по охране труда"),
            ("Проверка знаний требований охраны труда", "Аттестация по охране труда")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения земляных работ",
            ("Обучение требованиям охраны труда (1 раз в год)", "В. Программа обучения безопасным методам и приемам работ (по земляным работам)")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения кровельных работ",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение по охране труда")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения работ на высоте",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение по охране труда"),
            ("Первичный инструктаж по охране труда на рабочем месте", "Инструктаж на рабочем месте")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения огневых работ",
            ("Обучение мерам пожарной безопасности", "Обучение мерам пожарной безопасности"),
            ("Первичный противопожарный инструктаж на рабочем месте", "Программа первичного противопожарного инструктажа на рабочем месте")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения работ в ограниченных и замкнутых пространствах",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение по охране труда"),
            ("Вводный инструктаж ССЕ", "Пройден")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения работ, связанных с эксплуатацией подъемных сооружений",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение по охране труда")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения работ при воздействии вредных и (или) опасных производственных факторов",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение безопасным методам и приёмам выполнения работ"))
    ];
}

public static class TrainingKindMappingDefaults
{
    public static List<TrainingProgramMappingLine> CreateSeed() =>
    [
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения газоопасных работ",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение по охране труда"),
            ("Проверка знаний требований охраны труда", "Аттестация по охране труда"),
            ("", "")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения работ на высоте",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение по охране труда"),
            ("Первичный инструктаж по охране труда на рабочем месте", "Инструктаж на рабочем месте"),
            ("", "")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения работ, связанных с эксплуатацией подъемных сооружений",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение по охране труда"),
            ("", "")),
        ..TrainingMappingSeedBuilder.Lines(
            "Безопасные методы и приемы выполнения работ при воздействии вредных и (или) опасных производственных факторов",
            ("Обучение требованиям охраны труда (1 раз в год)", "Обучение безопасным методам и приёмам выполнения работ"),
            ("", ""))
    ];
}

public static class TrainingAssistantsCatalog
{
    public static IReadOnlyList<TrainingAssistantEntry> Entries { get; } =
    [
        new()
        {
            Id = "train-plan",
            Title = "Помощник планирования обучения",
            Description = "Определяет работников, по которым необходимо запланировать обучение (не инструктажи) в соответствии с нормами.",
            RouteUrl = "/Training/Assistants/Wizard?kind=train-plan"
        },
        new()
        {
            Id = "instr-plan",
            Title = "Помощник планирования инструктажа",
            Description = "Определяет работников, по которым необходимо запланировать инструктажи в соответствии с установленными нормами обучения.",
            RouteUrl = "/Training/Assistants/Wizard?kind=plan"
        },
        new()
        {
            Id = "instr-conduct",
            Title = "Помощник проведения инструктажа",
            Description = "Определяет работников, для которых требуется отразить проведение ранее запланированных инструктажей.",
            RouteUrl = "/Training/Assistants/Wizard?kind=conduct"
        },
        new()
        {
            Id = "instr-direct",
            Title = "Проведение инструктажа без планирования",
            Description = "Определяет работников, по которым необходимо провести инструктажи в соответствии с установленными нормами обучения.",
            RouteUrl = "/Training/Assistants/Wizard?kind=direct"
        }
    ];
}

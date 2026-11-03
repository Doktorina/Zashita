namespace Zashita.Core.Training.Journals;

public static class TrainingJournalRegistry
{
    private static readonly IReadOnlyList<TrainingJournalDefinition> All =
    [
        Create(
            TrainingJournalKind.IntroductoryOccupationalSafety,
            "Журнал регистрации вводного инструктажа по охране труда",
            "Отчет предназначен для регистрации вводного инструктажа по охране труда",
            "ЖУРНАЛ\nрегистрации вводного инструктажа по охране труда",
            officialOtTitle: true,
            showResponsible: false,
            match: p => Contains(p, "вводн") && !Contains(p, "противопожар") && !Contains(p, "подряд")),
        Create(
            TrainingJournalKind.WorkplaceOccupationalSafety,
            "Журнал регистрации инструктажа по охране труда на рабочем месте",
            "Отчет предназначен для регистрации инструктажей по охране труда на рабочем месте",
            "ЖУРНАЛ\nрегистрации инструктажа по охране труда на рабочем месте",
            officialOtTitle: true,
            showResponsible: false,
            match: p => (Contains(p, "рабоч") || Contains(p, "на рабочем")) &&
                        !Contains(p, "вводн") && !Contains(p, "противопожар") && !Contains(p, "подряд")),
        Create(
            TrainingJournalKind.IntroductoryFireSafety,
            "Журнал регистрации вводного противопожарного инструктажа",
            "Отчет предназначен для регистрации вводного противопожарного инструктажа",
            "ЖУРНАЛ\nрегистрации вводного противопожарного инструктажа",
            officialOtTitle: true,
            showResponsible: false,
            match: p => Contains(p, "противопожар") && Contains(p, "вводн") && !Contains(p, "подряд")),
        Create(
            TrainingJournalKind.WorkplaceFireSafety,
            "Журнал регистрации противопожарных инструктажей на рабочем месте",
            "Отчет предназначен для регистрации противопожарных инструктажей на рабочем месте",
            "ЖУРНАЛ\nрегистрации противопожарных инструктажей\n(на рабочем месте)",
            officialOtTitle: true,
            showResponsible: false,
            match: p => Contains(p, "противопожар") && !Contains(p, "вводн") && !Contains(p, "подряд")),
        Create(
            TrainingJournalKind.IntroductoryOccupationalSafetyContractors,
            "Журнал регистрации вводного инструктажа по охране труда для подрядных организаций",
            "Отчет предназначен для регистрации вводного инструктажа по охране труда для представителей подрядных организаций",
            "ЖУРНАЛ\nрегистрации вводного инструктажа по охране труда\n(для подрядных организаций)",
            officialOtTitle: true,
            showResponsible: false,
            contractors: true,
            match: p => Contains(p, "вводн") && !Contains(p, "противопожар") &&
                        (Contains(p, "подряд") || Contains(p, "представит"))),
        Create(
            TrainingJournalKind.IntroductoryFireSafetyContractors,
            "Журнал регистрации вводного противопожарного инструктажа для подрядных организаций",
            "Отчет предназначен для регистрации вводного противопожарного инструктажа для представителей подрядных организаций",
            "ЖУРНАЛ\nрегистрации вводного противопожарного инструктажа\n(для подрядных организаций)",
            officialOtTitle: true,
            showResponsible: false,
            contractors: true,
            match: p => Contains(p, "противопожар") && Contains(p, "вводн") &&
                        (Contains(p, "подряд") || Contains(p, "представит"))),
        Create(
            TrainingJournalKind.ElectroSafetyGroupI,
            "Журнал регистрации присвоения групп I по электробезопасности неэлектротехническому персоналу",
            "Отчет предназначен для регистрации присвоения групп I по электробезопасности неэлектротехническому персоналу",
            "ЖУРНАЛ\nПрисвоения группы I по электробезопасности\nнеэлектротехническому\nперсоналу N ____",
            officialOtTitle: true,
            showResponsible: false,
            showJournalNumber: true,
            match: p => Contains(p, "электробез") || (Contains(p, "электр") && Contains(p, "групп")))
    ];

    public static IReadOnlyList<TrainingJournalDefinition> Journals => All;

    public static TrainingJournalDefinition Get(TrainingJournalKind kind) =>
        All.First(j => j.Kind == kind);

    public static bool TryGetByTitle(string title, out TrainingJournalDefinition? definition)
    {
        definition = All.FirstOrDefault(j =>
            title.Contains(j.Title, StringComparison.OrdinalIgnoreCase) ||
            j.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
        if (definition is not null)
        {
            return true;
        }

        definition = title switch
        {
            _ when title.Contains("вводного инструктажа по охране труда", StringComparison.OrdinalIgnoreCase)
                   && title.Contains("подряд", StringComparison.OrdinalIgnoreCase)
                => Get(TrainingJournalKind.IntroductoryOccupationalSafetyContractors),
            _ when title.Contains("вводного противопожарного", StringComparison.OrdinalIgnoreCase)
                   && title.Contains("подряд", StringComparison.OrdinalIgnoreCase)
                => Get(TrainingJournalKind.IntroductoryFireSafetyContractors),
            _ when title.Contains("вводного инструктажа", StringComparison.OrdinalIgnoreCase)
                => Get(TrainingJournalKind.IntroductoryOccupationalSafety),
            _ when title.Contains("на рабочем месте", StringComparison.OrdinalIgnoreCase)
                   && title.Contains("противопожар", StringComparison.OrdinalIgnoreCase)
                => Get(TrainingJournalKind.WorkplaceFireSafety),
            _ when title.Contains("на рабочем месте", StringComparison.OrdinalIgnoreCase)
                => Get(TrainingJournalKind.WorkplaceOccupationalSafety),
            _ when title.Contains("противопожар", StringComparison.OrdinalIgnoreCase)
                => Get(TrainingJournalKind.IntroductoryFireSafety),
            _ when title.Contains("электробез", StringComparison.OrdinalIgnoreCase)
                   || title.Contains("групп I", StringComparison.OrdinalIgnoreCase)
                => Get(TrainingJournalKind.ElectroSafetyGroupI),
            _ => null
        };

        return definition is not null;
    }

    private static TrainingJournalDefinition Create(
        TrainingJournalKind kind,
        string title,
        string description,
        string titlePageHeading,
        Func<string, bool> match,
        string? excel = null,
        string? word = null,
        bool contractors = false,
        string? titleSubCaption = null,
        bool officialOtTitle = false,
        bool showResponsible = false,
        bool showJournalNumber = false,
        string[]? titleAppendixLines = null,
        bool fireTableWithColumnNumbers = false) =>
        new()
        {
            Kind = kind,
            Title = title,
            Description = description,
            TitlePageHeading = titlePageHeading,
            ProgramMatcher = match,
            ExcelTemplateFile = excel,
            WordTemplateFile = word,
            ContractorsOnly = contractors,
            TitlePageSubCaption = titleSubCaption,
            UseOfficialOtTitlePage = officialOtTitle,
            ShowResponsibleBlock = showResponsible,
            ShowJournalNumberOnTitle = showJournalNumber,
            TitlePageAppendixLines = titleAppendixLines,
            FireTableWithColumnNumbers = fireTableWithColumnNumbers
        };

    private static bool Contains(string program, string fragment) =>
        program.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}

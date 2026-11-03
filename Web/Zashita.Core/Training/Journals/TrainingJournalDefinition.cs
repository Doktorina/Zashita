namespace Zashita.Core.Training.Journals;

using Export;

public sealed class TrainingJournalDefinition
{
    public required TrainingJournalKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string TitlePageHeading { get; init; }
    public required Func<string, bool> ProgramMatcher { get; init; }
    public string? ExcelTemplateFile { get; init; }
    public string? WordTemplateFile { get; init; }
    public bool ContractorsOnly { get; init; }
    public string? TitlePageSubCaption { get; init; }
    /// <summary>Титул ОТ: организация и подразделение с линиями (без подписей в скобках).</summary>
    public bool UseOfficialOtTitlePage { get; init; }
    public bool ShowResponsibleBlock { get; init; }
    public bool ShowJournalNumberOnTitle { get; init; }
    /// <summary>Блок «Приложение…» в правом верхнем углу титульного листа.</summary>
    public string[]? TitlePageAppendixLines { get; init; }
    /// <summary>Таблица с нумерацией колонок (форма МЧС для противопожарных журналов).</summary>
    public bool FireTableWithColumnNumbers { get; init; }

    public TrainingJournalTableKind TableKind => Kind.GetTableKind();

    public IReadOnlyList<string> TitlePageHeadingLines =>
        TitlePageHeading.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public TrainingJournalLayout Layout { get; init; } = TrainingJournalLayout.Standard;

    public string ExportFileNameStem =>
        Kind switch
        {
            TrainingJournalKind.IntroductoryOccupationalSafety => "Журнал_вводного_инструктажа_ОТ",
            TrainingJournalKind.WorkplaceOccupationalSafety => "Журнал_инструктажа_на_рабочем_месте_ОТ",
            TrainingJournalKind.IntroductoryFireSafety => "Журнал_вводного_противопожарного",
            TrainingJournalKind.WorkplaceFireSafety => "Журнал_противопожарных_на_РМ",
            TrainingJournalKind.IntroductoryOccupationalSafetyContractors => "Журнал_вводного_ОТ_подряд",
            TrainingJournalKind.IntroductoryFireSafetyContractors => "Журнал_вводного_ПБ_подряд",
            TrainingJournalKind.ElectroSafetyGroupI => "Журнал_группы_I_электробезопасность",
            _ => "Журнал"
        };

    public string? ResolveExcelTemplatePath(string templatesDirectory)
    {
        if (string.IsNullOrWhiteSpace(ExcelTemplateFile))
        {
            return null;
        }

        var path = Path.Combine(templatesDirectory, ExcelTemplateFile);
        return File.Exists(path) ? path : null;
    }

    public string? ResolveWordTemplatePath(string templatesDirectory)
    {
        if (string.IsNullOrWhiteSpace(WordTemplateFile))
        {
            return null;
        }

        var docxName = WordTemplateFile.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)
            && !WordTemplateFile.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(WordTemplateFile, ".docx")
            : WordTemplateFile;

        var path = Path.Combine(templatesDirectory, docxName);
        if (File.Exists(path))
        {
            return path;
        }

        path = Path.Combine(templatesDirectory, WordTemplateFile);
        return File.Exists(path) ? path : null;
    }
}

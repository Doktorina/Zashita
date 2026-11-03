using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Zashita.Core.Training.Journals.Export;

using Zashita.Core.Training.Journals;

public static class TrainingJournalWordGenerator
{
    private const string FireInstructorHeader =
        "Фамилия, имя, отчество (при наличии) инструктирующего, номер документа об образовании и (или) квалификации или документа об обучении инструктирующего";

    public static void ExportToWord(
        IReadOnlyList<TrainingJournalEntry> entries,
        TrainingJournalParams parameters,
        string outputDocxPath,
        TrainingJournalDefinition definition,
        string templatesDirectory)
    {
        using var doc = WordprocessingDocument.Create(outputDocxPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        var org = parameters.Organization;
        if (string.IsNullOrWhiteSpace(org) && entries.Count > 0)
        {
            org = entries[0].Organization;
        }

        var from = parameters.TitleStarted?.Date ?? parameters.DateFrom.Date;
        var to = parameters.TitleEnded?.Date ?? parameters.DateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // На титуле только явно выбранное подразделение; «не указано» — пусто.
        var department = parameters.Department?.Trim() ?? string.Empty;
        if (department.Contains("не указано", StringComparison.OrdinalIgnoreCase) ||
            department is "—" or "-")
        {
            department = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(department) &&
            entries.Any(e =>
                string.Equals(department, e.Profession?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(department, e.Workplace?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(e.Workplace?.Trim(), e.Profession?.Trim(), StringComparison.OrdinalIgnoreCase))))
        {
            department = string.Empty;
        }

        if (parameters.ShowTitlePage)
        {
            AppendTitlePage(body, definition, org ?? string.Empty, department, from, to);
            body.AppendChild(PageBreak());
        }

        var table = BuildJournalTable(definition, entries);
        body.AppendChild(table);

        if (definition.ShowResponsibleBlock)
        {
            AppendResponsibleBlock(body);
        }

        var sectionEnd = new Paragraph();
        sectionEnd.PrependChild(new ParagraphProperties(TrainingJournalWordTypography.CreateLandscapeSection()));
        body.AppendChild(sectionEnd);

        TrainingJournalWordTypography.RemoveTrailingBlankSections(body);

        mainPart.Document.Save();
    }

    private static void AppendTitlePage(
        Body body,
        TrainingJournalDefinition definition,
        string organization,
        string department,
        DateTime from,
        DateTime to)
    {
        if (definition.UseOfficialOtTitlePage)
        {
            AppendOtOfficialTitlePage(body, definition, organization, department, from, to);
            return;
        }

        if (definition.TitlePageAppendixLines is { Length: > 0 } appendix)
        {
            foreach (var line in appendix)
            {
                body.AppendChild(MakeParagraph(line, false, JustificationValues.Right, TrainingJournalWordTypography.TitleSecondaryHalfPoints));
            }

            body.AppendChild(MakeParagraph(string.Empty));
        }
        else
        {
            for (var i = 0; i < 3; i++)
            {
                body.AppendChild(MakeParagraph(string.Empty));
            }
        }

        if (!string.IsNullOrWhiteSpace(definition.TitlePageSubCaption))
        {
            if (!string.IsNullOrWhiteSpace(organization))
            {
                body.AppendChild(MakeParagraph(organization, true, JustificationValues.Center, TrainingJournalWordTypography.TitleSecondaryHalfPoints));
            }

            body.AppendChild(MakeParagraph(definition.TitlePageSubCaption, false, JustificationValues.Center, TrainingJournalWordTypography.TitleSecondaryHalfPoints));
            body.AppendChild(MakeParagraph(string.Empty));
        }
        else if (!string.IsNullOrWhiteSpace(organization))
        {
            body.AppendChild(MakeParagraph(organization, true, JustificationValues.Center, TrainingJournalWordTypography.TitleSecondaryHalfPoints));
            body.AppendChild(MakeParagraph(string.Empty));
        }

        if (definition.Kind == TrainingJournalKind.ElectroSafetyGroupI)
        {
            foreach (var line in definition.TitlePageHeadingLines)
            {
                body.AppendChild(MakeParagraph(line, true, JustificationValues.Center, TrainingJournalWordTypography.TitleHalfPoints));
            }
        }
        else
        {
            foreach (var line in definition.TitlePageHeadingLines)
            {
                body.AppendChild(MakeParagraph(line, true, JustificationValues.Center, TrainingJournalWordTypography.TitleHalfPoints));
            }
        }

        for (var i = 0; i < 6; i++)
        {
            body.AppendChild(MakeParagraph(string.Empty));
        }

        AppendPeriodLines(body, from, to, useFullCalendar: false);
    }

    private static void AppendOtOfficialTitlePage(
        Body body,
        TrainingJournalDefinition definition,
        string organization,
        string department,
        DateTime from,
        DateTime to)
    {
        for (var i = 0; i < 2; i++)
        {
            body.AppendChild(MakeParagraph(string.Empty));
        }

        if (!string.IsNullOrWhiteSpace(organization))
        {
            body.AppendChild(MakeParagraph(organization, true, JustificationValues.Center, TrainingJournalWordTypography.TitleSecondaryHalfPoints));
        }

        if (definition.Kind == TrainingJournalKind.IntroductoryOccupationalSafety)
        {
            body.AppendChild(MakeParagraph(
                "(наименование организации)",
                false,
                JustificationValues.Center,
                TrainingJournalWordTypography.TitleSecondaryHalfPoints));

            body.AppendChild(MakeParagraph(string.Empty));
            if (!string.IsNullOrWhiteSpace(department))
            {
                body.AppendChild(MakeParagraph(
                    $"Подразделение: {department}",
                    false,
                    JustificationValues.Center,
                    TrainingJournalWordTypography.TitleSecondaryHalfPoints));
            }
        }

        if (definition.Kind == TrainingJournalKind.IntroductoryOccupationalSafetyContractors ||
            definition.Kind == TrainingJournalKind.IntroductoryFireSafetyContractors ||
            definition.Kind == TrainingJournalKind.IntroductoryFireSafety)
        {
            for (var i = 0; i < 4; i++)
            {
                body.AppendChild(MakeParagraph(string.Empty));
            }

            foreach (var line in definition.TitlePageHeadingLines)
            {
                body.AppendChild(MakeParagraph(line, true, JustificationValues.Center, TrainingJournalWordTypography.TitleHalfPoints));
            }

            if (definition.Kind != TrainingJournalKind.IntroductoryFireSafety)
            {
                body.AppendChild(MakeParagraph(string.Empty));
                if (!string.IsNullOrWhiteSpace(department))
                {
                    body.AppendChild(MakeParagraph(department, false, JustificationValues.Center, TrainingJournalWordTypography.TitleSecondaryHalfPoints));
                }
            }

            for (var i = 0; i < 5; i++)
            {
                body.AppendChild(MakeParagraph(string.Empty));
            }

            AppendPeriodLines(body, from, to, useFullCalendar: true);
            return;
        }

        if (definition.Kind == TrainingJournalKind.ElectroSafetyGroupI)
        {
            for (var i = 0; i < 4; i++)
            {
                body.AppendChild(MakeParagraph(string.Empty));
            }

            foreach (var line in definition.TitlePageHeadingLines)
            {
                body.AppendChild(MakeParagraph(line, true, JustificationValues.Center, TrainingJournalWordTypography.TitleHalfPoints));
            }

            for (var i = 0; i < 5; i++)
            {
                body.AppendChild(MakeParagraph(string.Empty));
            }

            AppendPeriodLines(body, from, to, useFullCalendar: true);
            return;
        }

        if (definition.Kind == TrainingJournalKind.WorkplaceOccupationalSafety ||
            definition.Kind == TrainingJournalKind.WorkplaceFireSafety)
        {
            // Титул с подразделением/местом, без подчёркиваний.
            for (var i = 0; i < 4; i++)
            {
                body.AppendChild(MakeParagraph(string.Empty));
            }

            foreach (var line in definition.TitlePageHeadingLines)
            {
                body.AppendChild(MakeParagraph(line, true, JustificationValues.Center, TrainingJournalWordTypography.TitleHalfPoints));
            }

            for (var i = 0; i < 3; i++)
            {
                body.AppendChild(MakeParagraph(string.Empty));
            }

            if (!string.IsNullOrWhiteSpace(department))
            {
                body.AppendChild(MakeParagraph(department, false, JustificationValues.Center, TrainingJournalWordTypography.TitleSecondaryHalfPoints));
            }

            for (var i = 0; i < 4; i++)
            {
                body.AppendChild(MakeParagraph(string.Empty));
            }

            AppendPeriodLines(body, from, to, useFullCalendar: true);
            return;
        }

        if (!string.IsNullOrWhiteSpace(department) &&
            definition.Kind != TrainingJournalKind.IntroductoryOccupationalSafety)
        {
            body.AppendChild(MakeParagraph(department, false, JustificationValues.Center, TrainingJournalWordTypography.TitleSecondaryHalfPoints));
        }

        for (var i = 0; i < 4; i++)
        {
            body.AppendChild(MakeParagraph(string.Empty));
        }

        foreach (var line in definition.TitlePageHeadingLines)
        {
            body.AppendChild(MakeParagraph(line, true, JustificationValues.Center, TrainingJournalWordTypography.TitleHalfPoints));
        }

        for (var i = 0; i < 5; i++)
        {
            body.AppendChild(MakeParagraph(string.Empty));
        }

        AppendPeriodLines(body, from, to, useFullCalendar: true);
    }

    private static void AppendPeriodLines(Body body, DateTime from, DateTime to, bool useFullCalendar)
    {
        var started = useFullCalendar ? RussianDateText.FullCalendar(from) : RussianDateText.Short(from);
        var ended = useFullCalendar ? RussianDateText.FullCalendar(to) : RussianDateText.Short(to);
        body.AppendChild(MakePeriodParagraph("Начат", started));
        body.AppendChild(MakePeriodParagraph("Окончен", ended));
    }

    private static Paragraph MakePeriodParagraph(string label, string dateText)
    {
        var paragraph = new Paragraph();
        var props = new ParagraphProperties(
            new Indentation { FirstLine = "5500" },
            new Justification { Val = JustificationValues.Left });
        paragraph.AppendChild(props);
        paragraph.AppendChild(new Run(
            TrainingJournalWordTypography.CreateArialRun(TrainingJournalWordTypography.TitleSecondaryHalfPoints),
            new Text($"{label}\t") { Space = SpaceProcessingModeValues.Preserve }));
        paragraph.AppendChild(new Run(
            TrainingJournalWordTypography.CreateArialRun(TrainingJournalWordTypography.TitleSecondaryHalfPoints),
            new Text(dateText) { Space = SpaceProcessingModeValues.Preserve }));
        return paragraph;
    }

    private static void AppendResponsibleBlock(Body body)
    {
        body.AppendChild(MakeParagraph(string.Empty));
        body.AppendChild(MakeParagraph(
            "Ответственный:",
            false,
            JustificationValues.Left,
            TrainingJournalWordTypography.TitleSecondaryHalfPoints));

        var line = new Paragraph(new ParagraphProperties(new Indentation { Left = "2200" }));
        line.AppendChild(new Run(
            TrainingJournalWordTypography.CreateArialRun(TrainingJournalWordTypography.TitleSecondaryHalfPoints),
            new Text("________________    ________________    ________________")
            {
                Space = SpaceProcessingModeValues.Preserve
            }));
        body.AppendChild(line);

        var captions = new Paragraph(new ParagraphProperties(new Indentation { Left = "2200" }));
        captions.AppendChild(new Run(
            TrainingJournalWordTypography.CreateArialRun(TrainingJournalWordTypography.TableHalfPoints),
            new Text("     (должность)              (подпись)         (расшифровка подписи)")
            {
                Space = SpaceProcessingModeValues.Preserve
            }));
        body.AppendChild(captions);
    }

    private static Table BuildJournalTable(
        TrainingJournalDefinition definition,
        IReadOnlyList<TrainingJournalEntry> entries)
    {
        var kind = definition.TableKind;
        if (kind is TrainingJournalTableKind.FireSafety11 or TrainingJournalTableKind.FireSafetyContractors12)
        {
            if (kind == TrainingJournalTableKind.FireSafetyContractors12)
            {
                return BuildFireContractorIntroTable(entries);
            }

            if (definition.Kind == TrainingJournalKind.IntroductoryFireSafety)
            {
                return BuildFireIntroTable(entries);
            }

            if (definition.Kind == TrainingJournalKind.WorkplaceFireSafety)
            {
                return BuildWorkplaceFireTable(entries);
            }

            return definition.FireTableWithColumnNumbers
                ? BuildFireOfficialTable(entries, contractors: false)
                : BuildFireSimpleTable(entries, contractors: false);
        }

        return kind switch
        {
            TrainingJournalTableKind.OccupationalSafetyIntro9 => BuildIntroductoryOtTable(entries),
            TrainingJournalTableKind.OccupationalSafety9 => BuildWorkplaceOtTable(entries),
            TrainingJournalTableKind.ContractorOccupationalSafety9 => BuildContractorOtTable(entries),
            TrainingJournalTableKind.ElectroGroupI7 => BuildElectroTable(entries),
            _ => BuildOccupationalSafetyTable(entries)
        };
    }

    private static Table BuildIntroductoryOtTable(IReadOnlyList<TrainingJournalEntry> entries)
    {
        const int cols = 8;
        var table = new Table(
            BaseTableProperties(),
            TableGridEqual(cols, "1925"));

        AppendIntroductoryOtHeaderRows(table);
        foreach (var e in entries.Where(HasIntroRowContent))
        {
            table.AppendChild(MakeIntroductoryOtDataRow(e));
        }

        return table;
    }

    private static bool HasIntroRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.Department) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        e.BriefingDate != default;

    private static void AppendIntroductoryOtHeaderRows(Table table)
    {
        var row1 = new TableRow();
        row1.Append(
            MakeHeaderCell("Дата проведения вводного инструктажа по охране труда"),
            MakeHeaderCell("Фамилия, имя, отчество (при наличии) работника, прошедшего вводный инструктаж по охране труда"),
            MakeHeaderCell("Профессия (должность) работника, прошедшего вводный инструктаж по охране труда"),
            MakeHeaderCell("Число, месяц, год рождения работника, прошедшего вводный инструктаж по охране труда"),
            MakeHeaderCell("Наименование подразделения, в котором будет осуществлять трудовую деятельность работник, прошедший вводный инструктаж по охране труда"),
            MakeHeaderCell("Фамилия, имя, отчество (при наличии), профессия (должность) работника, проводившего вводный инструктаж по охране труда"),
            MakeHeaderCell("Подпись работника, проводившего инструктаж по охране труда"),
            MakeHeaderCell("Подпись работника, прошедшего вводный инструктаж по охране труда"));

        var row2 = new TableRow();
        for (var n = 1; n <= 8; n++)
        {
            row2.Append(MakeHeaderCell(n.ToString()));
        }

        table.AppendChild(row1);
        table.AppendChild(row2);
    }

    private static TableRow MakeIntroductoryOtDataRow(TrainingJournalEntry e) =>
        new(
            MakeCell(RussianDateText.Short(e.BriefingDate)),
            MakeCell(e.EmployeeName),
            MakeCell(e.Profession),
            MakeCell(RussianDateText.Short(e.BirthDate)),
            MakeCell(e.Department),
            MakeCell(FormatInstructor(e)),
            MakeCell(string.Empty),
            MakeCell(string.Empty));

    private static TableRow MakeEmptyIntroductoryOtRow() =>
        new(
            MakeCell(string.Empty),
            MakeCell(string.Empty),
            MakeCell(string.Empty),
            MakeCell(string.Empty),
            MakeCell(string.Empty),
            MakeCell(string.Empty),
            MakeCell(string.Empty),
            MakeCell(string.Empty));

    private static Table BuildWorkplaceOtTable(IReadOnlyList<TrainingJournalEntry> entries)
    {
        var widths = new[]
        {
            "1251", "1947", "1529", "1251", "1112", "1808", "1808", "1808", "1251", "1539"
        };
        var grid = new TableGrid();
        foreach (var w in widths)
        {
            grid.AppendChild(new GridColumn { Width = w });
        }

        var table = new Table(BaseTableProperties(), grid);
        AppendWorkplaceOtHeaderRows(table);
        foreach (var e in entries.Where(HasWorkplaceRowContent))
        {
            table.AppendChild(MakeWorkplaceOtDataRow(e));
        }

        return table;
    }

    private static bool HasWorkplaceRowContent(TrainingJournalEntry e) =>
        HasIntroRowContent(e) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        !string.IsNullOrWhiteSpace(e.BriefingReason) ||
        !string.IsNullOrWhiteSpace(e.LocalActName) ||
        !string.IsNullOrWhiteSpace(e.Workplace);

    private static void AppendWorkplaceOtHeaderRows(Table table)
    {
        var row1 = new TableRow();
        row1.Append(
            MakeHeaderCell("Дата проведения инструктажа по охране труда"),
            MakeHeaderCell("Фамилия, имя, отчество (при наличии) работника, прошедшего инструктаж по охране труда"),
            MakeHeaderCell("Профессия (должность) работника, прошедшего инструктаж по охране труда"),
            MakeHeaderCell("Число, месяц, год рождения работника, прошедшего инструктаж по охране труда"),
            MakeHeaderCell("Вид инструктажа по охране труда"),
            MakeHeaderCell("Причина проведения инструктажа по охране труда (для внепланового или целевого инструктажа по охране труда)"),
            MakeHeaderCell("Фамилия, имя, отчество (при наличии), профессия (должность) работника, проводившего инструктаж по охране труда"),
            MakeHeaderCell("Наименование локального акта (локальных актов), в объеме требований которого проведен инструктаж по охране труда"),
            MakeHeaderCell("Подпись работника, проводившего инструктаж по охране труда"),
            MakeHeaderCell("Подпись работника, прошедшего инструктаж по охране труда"));

        var row2 = new TableRow();
        for (var n = 1; n <= 10; n++)
        {
            row2.Append(MakeHeaderCell(n.ToString()));
        }

        table.AppendChild(row1);
        table.AppendChild(row2);
    }

    private static TableRow MakeWorkplaceOtDataRow(TrainingJournalEntry e)
    {
        var localAct = string.IsNullOrWhiteSpace(e.LocalActName) ? e.Workplace : e.LocalActName;
        return new TableRow(
            MakeCell(RussianDateText.Short(e.BriefingDate)),
            MakeCell(e.EmployeeName),
            MakeCell(e.Profession),
            MakeCell(RussianDateText.Short(e.BirthDate)),
            MakeCell(e.BriefingType),
            MakeCell(e.BriefingReason),
            MakeCell(FormatInstructor(e)),
            MakeCell(localAct),
            MakeCell(string.Empty),
            MakeCell(string.Empty));
    }

    private static Table BuildOccupationalSafetyTable(IReadOnlyList<TrainingJournalEntry> entries)
    {
        const int cols = 8;
        var table = new Table(
            BaseTableProperties(),
            TableGridEqual(cols, "1925"));

        AppendOtHeaderRows(table, contractor: false);
        foreach (var e in entries)
        {
            table.AppendChild(MakeOtDataRow(e, contractor: false));
        }

        return table;
    }

    private static Table BuildContractorOtTable(IReadOnlyList<TrainingJournalEntry> entries)
    {
        var widths = new[] { "1611", "1805", "1852", "1879", "2480", "2169", "1898", "1734" };
        var grid = new TableGrid();
        foreach (var w in widths)
        {
            grid.AppendChild(new GridColumn { Width = w });
        }

        var table = new Table(BaseTableProperties(), grid);
        AppendContractorOtHeaderRows(table);
        foreach (var e in entries.Where(HasContractorRowContent))
        {
            table.AppendChild(MakeContractorOtDataRow(e));
        }

        return table;
    }

    private static bool HasContractorRowContent(TrainingJournalEntry e) =>
        HasIntroRowContent(e) || !string.IsNullOrWhiteSpace(e.LegalEntity);

    private static void AppendContractorOtHeaderRows(Table table)
    {
        var row1 = new TableRow();
        row1.Append(
            MakeHeaderCell("Дата проведения вводного инструктажа по охране труда"),
            MakeHeaderCell("ФИО (при наличии) работника прошедшего вводный инструктаж по охране труда"),
            MakeHeaderCell("Профессия (должность) работника, прошедшего вводный инструктаж по охране труда"),
            MakeHeaderCell("Число, месяц, год рождения работника прошедшего вводный инструктаж по охране труда"),
            MakeHeaderCell("Наименование подразделения, в котором будет осуществлять трудовую деятельность работник, прошедший вводный инструктаж"),
            MakeHeaderCell("ФИО (при наличии), профессия (должность) работника, проводившего вводный инструктаж по охране труда"),
            MakeHeaderCell("Подпись работника, проводившего вводный инструктаж по охране труда"),
            MakeHeaderCell("Подпись работника, прошедшего вводный инструктаж по охране труда"));

        var row2 = new TableRow();
        for (var n = 1; n <= 8; n++)
        {
            row2.Append(MakeHeaderCell(n.ToString()));
        }

        table.AppendChild(row1);
        table.AppendChild(row2);
    }

    private static TableRow MakeContractorOtDataRow(TrainingJournalEntry e)
    {
        var dept = !string.IsNullOrWhiteSpace(e.Department) ? e.Department : e.LegalEntity;
        return new TableRow(
            MakeCell(RussianDateText.Short(e.BriefingDate)),
            MakeCell(e.EmployeeName),
            MakeCell(e.Profession),
            MakeCell(RussianDateText.Short(e.BirthDate)),
            MakeCell(dept),
            MakeCell(FormatInstructor(e)),
            MakeCell(string.Empty),
            MakeCell(string.Empty));
    }

    private static void AppendOtHeaderRows(Table table, bool contractor)
    {
        if (contractor)
        {
            AppendContractorOtHeaderRows(table);
            return;
        }

        var r1 = new TableRow();
        r1.Append(
            MakeHeaderCell("Дата проведения инструктажа", mergeDown: true),
            MakeHeaderCell("Фамилия, имя, отчество (при наличии) работника, прошедшего инструктаж", mergeDown: true),
            MakeHeaderCell("Число, месяц, год рождения работника, прошедшего инструктаж", mergeDown: true),
            MakeHeaderCell("Профессия (должность) работника, прошедшего инструктаж", mergeDown: true),
            MakeHeaderCell(
                "Наименование подразделения, в котором будет осуществлять трудовую деятельность работник, прошедший инструктаж",
                mergeDown: true),
            MakeHeaderCell(
                "Фамилия, имя, отчество (при наличии), профессия (должность) проводившего инструктаж",
                mergeDown: true),
            MakeHeaderCell("Подпись", columnSpan: 2));

        var r2 = new TableRow();
        r2.Append(
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("работника, проводившего инструктаж"),
            MakeHeaderCell("работника, прошедшего инструктаж"));

        table.AppendChild(r1);
        table.AppendChild(r2);
    }

    private static TableRow MakeOtDataRow(TrainingJournalEntry e, bool contractor)
    {
        var instructor = FormatInstructor(e);
        if (contractor)
        {
            return new TableRow(
                MakeCell(RussianDateText.Short(e.BriefingDate)),
                MakeCell(e.EmployeeName),
                MakeCell(RussianDateText.Short(e.BirthDate)),
                MakeCell(e.Profession),
                MakeCell(e.LegalEntity),
                MakeCell(e.Department),
                MakeCell(instructor),
                MakeCell(string.Empty),
                MakeCell(string.Empty));
        }

        return new TableRow(
            MakeCell(RussianDateText.Short(e.BriefingDate)),
            MakeCell(e.EmployeeName),
            MakeCell(RussianDateText.Short(e.BirthDate)),
            MakeCell(e.Profession),
            MakeCell(e.Department),
            MakeCell(instructor),
            MakeCell(string.Empty),
            MakeCell(string.Empty));
    }

    private static Table BuildElectroTable(IReadOnlyList<TrainingJournalEntry> entries)
    {
        var widths = new[] { "2863", "2835", "2127", "1701", "1559", "2410", "1842" };
        var grid = new TableGrid();
        foreach (var w in widths)
        {
            grid.AppendChild(new GridColumn { Width = w });
        }

        var table = new Table(BaseTableProperties(), grid);

        table.AppendChild(new TableRow(
            MakeHeaderCell("Фамилия, имя, отчество", mergeDown: true),
            MakeHeaderCell("Наименование подразделения", mergeDown: true),
            MakeHeaderCell("Должность (профессия)", mergeDown: true),
            MakeHeaderCell("Дата предыдущего присвоения", mergeDown: true),
            MakeHeaderCell("Дата присвоения", mergeDown: true),
            MakeHeaderCell("Подпись", columnSpan: 2)));

        table.AppendChild(new TableRow(
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("проверяемого"),
            MakeHeaderCell("проверяющего")));

        var rNum = new TableRow();
        for (var n = 1; n <= 7; n++)
        {
            rNum.Append(MakeHeaderCell(n.ToString()));
        }

        table.AppendChild(rNum);

        foreach (var e in entries.Where(HasElectroRowContent))
        {
            table.AppendChild(new TableRow(
                MakeCell(e.EmployeeName),
                MakeCell(e.Department),
                MakeCell(e.Profession),
                MakeCell(e.PreviousAssignmentDate is { } p ? RussianDateText.Short(p) : string.Empty),
                MakeCell(RussianDateText.Short(e.BriefingDate)),
                MakeCell(string.Empty),
                MakeCell(string.Empty)));
        }

        return table;
    }

    private static bool HasElectroRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Department) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        e.PreviousAssignmentDate is not null ||
        e.BriefingDate != default;

    private static Table BuildFireOfficialTable(IReadOnlyList<TrainingJournalEntry> entries, bool contractors)
    {
        var colCount = contractors ? 12 : 11;
        var table = new Table(
            BaseTableProperties(),
            contractors
                ? TableGridEqual(colCount, (15400 / colCount).ToString())
                : FireIntroTableGrid());

        AppendFireHeaderRows(table, contractors);
        var hNum = new TableRow();
        for (var i = 1; i <= colCount; i++)
        {
            hNum.Append(MakeHeaderCell(i.ToString()));
        }

        table.AppendChild(hNum);

        foreach (var e in entries)
        {
            table.AppendChild(MakeFireDataRow(e, contractors));
        }

        return table;
    }

    private static Table BuildWorkplaceFireTable(IReadOnlyList<TrainingJournalEntry> entries)
    {
        var widths = new[] { "564", "1439", "1027", "1306", "2724", "1182", "1187", "564", "2051", "1569", "1815" };
        var grid = new TableGrid();
        foreach (var w in widths)
        {
            grid.AppendChild(new GridColumn { Width = w });
        }

        var table = new Table(BaseTableProperties(), grid);
        AppendWorkplaceFireHeaderRows(table);
        foreach (var e in entries.Where(HasWorkplaceFireRowContent))
        {
            table.AppendChild(MakeWorkplaceFireDataRow(e));
        }

        return table;
    }

    private static bool HasWorkplaceFireRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        e.BriefingDate != default;

    private static void AppendWorkplaceFireHeaderRows(Table table)
    {
        const string instructorHeader =
            "ФИО (при наличии) инструктирующего, номер документа об образовании и (или) квалификации или документа об обучении инструктирующего";

        table.AppendChild(new TableRow(
            MakeHeaderCell("Дата", mergeDown: true),
            MakeHeaderCell("Вид проводимого инструктажа", mergeDown: true),
            MakeHeaderCell("Инструктируемый", mergeDown: true, columnSpan: 2),
            MakeHeaderCell("Теоретическая часть", columnSpan: 3),
            MakeHeaderCell("Дата", mergeDown: true),
            MakeHeaderCell("Практическая часть", columnSpan: 3)));

        table.AppendChild(new TableRow(
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true, columnSpan: 2),
            MakeHeaderCell(instructorHeader, mergeDown: true),
            MakeHeaderCell("Подпись", columnSpan: 2),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(instructorHeader, mergeDown: true),
            MakeHeaderCell("Подпись", columnSpan: 2)));

        table.AppendChild(new TableRow(
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("ФИО (при наличии)"),
            MakeHeaderCell("Профессия, должность"),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("инструктируемого"),
            MakeHeaderCell("инструктирующего"),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("инструктируемого"),
            MakeHeaderCell("инструктирующего")));

        var rNum = new TableRow();
        for (var n = 1; n <= 11; n++)
        {
            rNum.Append(MakeHeaderCell(n.ToString()));
        }

        table.AppendChild(rNum);
    }

    private static TableRow MakeWorkplaceFireDataRow(TrainingJournalEntry e)
    {
        var instructor = FormatInstructor(e);
        var practiceDate = RussianDateText.Short(e.PracticeDate ?? e.TheoryDate ?? e.BriefingDate);
        return new(
            MakeCell(RussianDateText.Short(e.BriefingDate)),
            MakeCell(e.BriefingType),
            MakeCell(e.EmployeeName),
            MakeCell(e.Profession),
            MakeCell(instructor),
            MakeCell(string.Empty),
            MakeCell(string.Empty),
            MakeCell(practiceDate),
            MakeCell(instructor),
            MakeCell(string.Empty),
            MakeCell(string.Empty));
    }

    private static Table BuildFireContractorIntroTable(IReadOnlyList<TrainingJournalEntry> entries)
    {
        var widths = new[] { "565", "1324", "1415", "2404", "2214", "3095", "1967", "2444" };
        var grid = new TableGrid();
        foreach (var w in widths)
        {
            grid.AppendChild(new GridColumn { Width = w });
        }

        var table = new Table(BaseTableProperties(), grid);
        AppendFireContractorHeaderRows(table);
        foreach (var e in entries.Where(HasFireContractorRowContent))
        {
            table.AppendChild(MakeFireContractorDataRow(e));
        }

        return table;
    }

    private static Table BuildFireIntroTable(IReadOnlyList<TrainingJournalEntry> entries)
    {
        var widths = new[] { "565", "1324", "3819", "2214", "3095", "1967", "2444" };
        var grid = new TableGrid();
        foreach (var w in widths)
        {
            grid.AppendChild(new GridColumn { Width = w });
        }

        var table = new Table(BaseTableProperties(), grid);
        AppendFireIntroHeaderRows(table);
        foreach (var e in entries.Where(HasFireIntroRowContent))
        {
            table.AppendChild(MakeFireIntroDataRow(e));
        }

        return table;
    }

    private static bool HasFireIntroRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        e.BriefingDate != default;

    private static void AppendFireIntroHeaderRows(Table table)
    {
        const string instructorHeader =
            "Фамилия, имя отчество (при наличии) инструктирующего, номер документа об образовании и (или) квалификации или документа об обучении инструктирующего";

        table.AppendChild(new TableRow(
            MakeHeaderCell("Дата", mergeDown: true),
            MakeHeaderCell("Вид проводимого инструктажа", mergeDown: true),
            MakeHeaderCell("Инструктируемый", mergeDown: true, columnSpan: 2),
            MakeHeaderCell("Теоретическая часть", columnSpan: 3)));

        table.AppendChild(new TableRow(
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true, columnSpan: 2),
            MakeHeaderCell(instructorHeader, mergeDown: true),
            MakeHeaderCell("Подпись", columnSpan: 2)));

        table.AppendChild(new TableRow(
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("Фамилия, имя отчество (при наличии)"),
            MakeHeaderCell("Профессия, должность"),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("инструктируемого"),
            MakeHeaderCell("инструктирующего")));

        var rNum = new TableRow();
        for (var n = 1; n <= 7; n++)
        {
            rNum.Append(MakeHeaderCell(n.ToString()));
        }

        table.AppendChild(rNum);
    }

    private static TableRow MakeFireIntroDataRow(TrainingJournalEntry e) =>
        new(
            MakeCell(RussianDateText.Short(e.BriefingDate)),
            MakeCell(e.BriefingType),
            MakeCell(e.EmployeeName),
            MakeCell(e.Profession),
            MakeCell(FormatInstructor(e)),
            MakeCell(string.Empty),
            MakeCell(string.Empty));

    private static bool HasFireContractorRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.LegalEntity) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        e.BriefingDate != default;

    private static void AppendFireContractorHeaderRows(Table table)
    {
        const string instructorHeader =
            "Фамилия, имя отчество (при наличии) инструктирующего, номер документа об образовании и (или) квалификации или документа об обучении инструктирующего";

        table.AppendChild(new TableRow(
            MakeHeaderCell("Дата", mergeDown: true),
            MakeHeaderCell("Вид проводимого инструктажа", mergeDown: true),
            MakeHeaderCell("Инструктируемый", mergeDown: true, columnSpan: 3),
            MakeHeaderCell("Теоретическая часть", columnSpan: 3)));

        table.AppendChild(new TableRow(
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true, columnSpan: 3),
            MakeHeaderCell(instructorHeader, mergeDown: true),
            MakeHeaderCell("Подпись", columnSpan: 2)));

        table.AppendChild(new TableRow(
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("Наименование подрядной организации"),
            MakeHeaderCell("Фамилия, имя отчество (при наличии)"),
            MakeHeaderCell("Профессия, должность"),
            MakeHeaderCell(string.Empty, mergeDownContinue: true),
            MakeHeaderCell("инструктируемого"),
            MakeHeaderCell("инструктирующего")));

        var rNum = new TableRow();
        for (var n = 1; n <= 8; n++)
        {
            rNum.Append(MakeHeaderCell(n.ToString()));
        }

        table.AppendChild(rNum);
    }

    private static TableRow MakeFireContractorDataRow(TrainingJournalEntry e) =>
        new(
            MakeCell(RussianDateText.Short(e.BriefingDate)),
            MakeCell(e.BriefingType),
            MakeCell(e.LegalEntity),
            MakeCell(e.EmployeeName),
            MakeCell(e.Profession),
            MakeCell(FormatInstructor(e)),
            MakeCell(string.Empty),
            MakeCell(string.Empty));

    private static Table BuildFireSimpleTable(IReadOnlyList<TrainingJournalEntry> entries, bool contractors)
    {
        var colCount = contractors ? 12 : 11;
        var table = new Table(
            BaseTableProperties(),
            TableGridEqual(colCount, (15400 / colCount).ToString()));

        AppendFireHeaderRows(table, contractors);

        foreach (var e in entries)
        {
            table.AppendChild(MakeFireDataRow(e, contractors));
        }

        return table;
    }

    private static void AppendFireHeaderRows(Table table, bool contractors)
    {
        var h1 = new TableRow();
        h1.Append(MakeHeaderCell("Дата", mergeDown: true));
        h1.Append(MakeHeaderCell("Вид проводимого инструктажа", mergeDown: true));
        h1.Append(MakeHeaderCell("Инструктируемый", columnSpan: contractors ? 3 : 2));
        h1.Append(MakeHeaderCell("Теоретическая часть", columnSpan: 3));
        h1.Append(MakeHeaderCell("Дата", mergeDown: true));
        h1.Append(MakeHeaderCell("Практическая часть", columnSpan: 3));

        var h2 = new TableRow();
        h2.Append(MakeHeaderCell(string.Empty, mergeDownContinue: true));
        h2.Append(MakeHeaderCell(string.Empty, mergeDownContinue: true));
        h2.Append(MakeHeaderCell("Фамилия, имя, отчество (при наличии)", mergeDown: true));
        h2.Append(MakeHeaderCell("Профессия, должность", mergeDown: true));
        if (contractors)
        {
            h2.Append(MakeHeaderCell("Юридическое лицо, работником которого является инструктируемый", mergeDown: true));
        }

        h2.Append(MakeHeaderCell(FireInstructorHeader, mergeDown: true));
        h2.Append(MakeHeaderCell("Подпись инструктируемого"));
        h2.Append(MakeHeaderCell("Подпись инструктирующего"));
        h2.Append(MakeHeaderCell(string.Empty, mergeDownContinue: true));
        h2.Append(MakeHeaderCell(FireInstructorHeader, mergeDown: true));
        h2.Append(MakeHeaderCell("Подпись инструктируемого"));
        h2.Append(MakeHeaderCell("Подпись инструктирующего"));

        table.AppendChild(h1);
        table.AppendChild(h2);
    }

    private static TableRow MakeFireDataRow(TrainingJournalEntry e, bool contractors)
    {
        var instructor = FormatInstructor(e);
        var practiceDate = RussianDateText.Short(e.PracticeDate ?? e.TheoryDate ?? e.BriefingDate);

        if (contractors)
        {
            return new TableRow(
                MakeCell(RussianDateText.Short(e.BriefingDate)),
                MakeCell(e.BriefingType),
                MakeCell(e.EmployeeName),
                MakeCell(e.Profession),
                MakeCell(e.LegalEntity),
                MakeCell(instructor),
                MakeCell(string.Empty),
                MakeCell(string.Empty),
                MakeCell(practiceDate),
                MakeCell(instructor),
                MakeCell(string.Empty),
                MakeCell(string.Empty));
        }

        return new TableRow(
            MakeCell(RussianDateText.Short(e.BriefingDate)),
            MakeCell(e.BriefingType),
            MakeCell(e.EmployeeName),
            MakeCell(e.Profession),
            MakeCell(instructor),
            MakeCell(string.Empty),
            MakeCell(string.Empty),
            MakeCell(practiceDate),
            MakeCell(instructor),
            MakeCell(string.Empty),
            MakeCell(string.Empty));
    }

    private static TableGrid FireIntroTableGrid() =>
        new(
            new GridColumn { Width = "1296" },
            new GridColumn { Width = "1301" },
            new GridColumn { Width = "1061" },
            new GridColumn { Width = "1191" },
            new GridColumn { Width = "2479" },
            new GridColumn { Width = "1083" },
            new GridColumn { Width = "1087" },
            new GridColumn { Width = "1226" },
            new GridColumn { Width = "2479" },
            new GridColumn { Width = "1086" },
            new GridColumn { Width = "1089" });

    private static string FormatInstructor(TrainingJournalEntry e) =>
        string.IsNullOrWhiteSpace(e.InstructorQualification)
            ? e.InstructorName
            : $"{e.InstructorName}, {e.InstructorQualification}";

    private static TableProperties BaseTableProperties() =>
        new(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            TrainingJournalWordTypography.DefaultTableCellMargin(),
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 8 },
                new BottomBorder { Val = BorderValues.Single, Size = 8 },
                new LeftBorder { Val = BorderValues.Single, Size = 8 },
                new RightBorder { Val = BorderValues.Single, Size = 8 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 8 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 8 }));

    private static TableGrid TableGridEqual(int columns, string widthTwipsEach)
    {
        var grid = new TableGrid();
        for (var i = 0; i < columns; i++)
        {
            grid.AppendChild(new GridColumn { Width = widthTwipsEach });
        }

        return grid;
    }

    private static TableCellBorders UniformCellBorders() =>
        new(
            new TopBorder { Val = BorderValues.Single, Size = 8 },
            new BottomBorder { Val = BorderValues.Single, Size = 8 },
            new LeftBorder { Val = BorderValues.Single, Size = 8 },
            new RightBorder { Val = BorderValues.Single, Size = 8 });

    private static TableCell MakeHeaderCell(
        string text,
        bool mergeDown = false,
        bool mergeDownContinue = false,
        int columnSpan = 1)
    {
        var props = new TableCellProperties(
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center },
            TrainingJournalWordTypography.CellPadding(),
            UniformCellBorders());

        if (mergeDown)
        {
            props.Append(new VerticalMerge { Val = MergedCellValues.Restart });
        }
        else if (mergeDownContinue)
        {
            props.Append(new VerticalMerge { Val = MergedCellValues.Continue });
        }

        if (columnSpan > 1)
        {
            props.Append(new GridSpan { Val = columnSpan });
        }

        return new TableCell(
            props,
            new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(
                    TrainingJournalWordTypography.CreateArialRun(
                        TrainingJournalWordTypography.TableHalfPoints,
                        bold: false,
                        boldCs: true),
                    new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
    }

    private static TableCell MakeCell(string text)
    {
        var props = new TableCellProperties(
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center },
            TrainingJournalWordTypography.CellPadding(),
            UniformCellBorders());

        return new TableCell(
            props,
            new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Left }),
                new Run(
                    TrainingJournalWordTypography.CreateArialRun(TrainingJournalWordTypography.TableHalfPoints),
                    new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
    }

    private static Paragraph PageBreak() =>
        new(new ParagraphProperties(), new Run(new Break { Type = BreakValues.Page }));

    private static Paragraph MakeParagraph(string text) =>
        MakeParagraph(text, false, JustificationValues.Left, TrainingJournalWordTypography.TitleSecondaryHalfPoints);

    private static Paragraph MakeParagraph(
        string text,
        bool bold,
        JustificationValues align,
        string fontHalfPoints)
    {
        return new Paragraph(
            new ParagraphProperties(new Justification { Val = align }),
            new Run(
                TrainingJournalWordTypography.CreateArialRun(fontHalfPoints, bold, boldCs: bold),
                new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }
}



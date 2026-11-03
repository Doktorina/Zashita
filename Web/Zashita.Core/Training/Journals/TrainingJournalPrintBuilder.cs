using System.Globalization;
using System.Net;
using System.Text;

namespace Zashita.Core.Training.Journals;

public static class TrainingJournalPrintBuilder
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Ширины колонок как в бланке DOCX (dxa → %).</summary>
    private static readonly double[] IntroColPercents = [8.2, 10.4, 10.3, 10.5, 17.6, 16.8, 14.0, 12.2];

    private static readonly string[] IntroHeaders =
    [
        "Дата проведения вводного инструктажа по охране труда",
        "Фамилия, имя, отчество (при наличии) работника, прошедшего вводный инструктаж по охране труда",
        "Профессия (должность) работника, прошедшего вводный инструктаж по охране труда",
        "Число, месяц, год рождения работника, прошедшего вводный инструктаж по охране труда",
        "Наименование подразделения, в котором будет осуществлять трудовую деятельность работник, прошедший вводный инструктаж по охране труда",
        "Фамилия, имя, отчество (при наличии), профессия (должность) работника, проводившего вводный инструктаж по охране труда",
        "Подпись работника, проводившего инструктаж по охране труда",
        "Подпись работника, прошедшего вводный инструктаж по охране труда"
    ];

    /// <summary>Ширины 10 граф бланка на рабочем месте (dxa → %).</summary>
    private static readonly double[] WorkplaceColPercents =
        [8.12, 12.64, 9.92, 8.12, 7.22, 11.74, 11.74, 11.74, 8.12, 9.99];

    private static readonly string[] WorkplaceHeaders =
    [
        "Дата проведения инструктажа по охране труда",
        "Фамилия, имя, отчество (при наличии) работника, прошедшего инструктаж по охране труда",
        "Профессия (должность) работника, прошедшего инструктаж по охране труда",
        "Число, месяц, год рождения работника, прошедшего инструктаж по охране труда",
        "Вид инструктажа по охране труда",
        "Причина проведения инструктажа по охране труда (для внепланового или целевого инструктажа по охране труда)",
        "Фамилия, имя, отчество (при наличии), профессия (должность) работника, проводившего инструктаж по охране труда",
        "Наименование локального акта (локальных актов), в объеме требований которого проведен инструктаж по охране труда",
        "Подпись работника, проводившего инструктаж по охране труда",
        "Подпись работника, прошедшего инструктаж по охране труда"
    ];

    /// <summary>Ширины 8 граф бланка для подрядных (dxa → %).</summary>
    private static readonly double[] ContractorColPercents =
        [10.44, 11.70, 12.00, 12.18, 16.07, 14.06, 12.30, 11.24];

    private static readonly string[] ContractorHeaders =
    [
        "Дата проведения вводного инструктажа по охране труда",
        "ФИО (при наличии) работника прошедшего вводный инструктаж по охране труда",
        "Профессия (должность) работника, прошедшего вводный инструктаж по охране труда",
        "Число, месяц, год рождения работника прошедшего вводный инструктаж по охране труда",
        "Наименование подразделения, в котором будет осуществлять трудовую деятельность работник, прошедший вводный инструктаж",
        "ФИО (при наличии), профессия (должность) работника, проводившего вводный инструктаж по охране труда",
        "Подпись работника, проводившего вводный инструктаж по охране труда",
        "Подпись работника, прошедшего вводный инструктаж по охране труда"
    ];

    public static string BuildHtmlDocument(
        TrainingJournalDefinition definition,
        IReadOnlyList<TrainingJournalEntry> entries,
        TrainingJournalParams parameters,
        bool showTitlePage) =>
        BuildBody(definition, entries, parameters, showTitlePage, editable: false, overrides: null, fullDocument: true);

    public static string BuildPreviewBody(
        TrainingJournalDefinition definition,
        IReadOnlyList<TrainingJournalEntry> entries,
        TrainingJournalParams parameters,
        bool showTitlePage,
        IReadOnlyDictionary<string, string>? overrides = null) =>
        BuildBody(definition, entries, parameters, showTitlePage, editable: true, overrides, fullDocument: false);

    private static string BuildBody(
        TrainingJournalDefinition definition,
        IReadOnlyList<TrainingJournalEntry> entries,
        TrainingJournalParams parameters,
        bool showTitlePage,
        bool editable,
        IReadOnlyDictionary<string, string>? overrides,
        bool fullDocument)
    {
        var ov = overrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var org = ResolveOrg(parameters, entries);
        var department = ResolveDepartment(parameters, entries);
        var (from, to) = ResolvePeriod(parameters);

        var sb = new StringBuilder();
        if (fullDocument)
        {
        sb.Append("""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
              <meta charset="utf-8" />
              <title>
            """);
        sb.Append(E(definition.Title));
        sb.Append("</title>");
        sb.Append(Zashita.Core.Brand.SiteIconHtml.HeadLinks);
        sb.Append("""
              <link rel="stylesheet" href="/css/training-journal-print.css" />
            </head>
            <body class="journal-print-body" onload="window.print()">
            """);
        }
        else
        {
            sb.Append("<div class=\"journal-print-body journal-preview-sheet\">");
        }

        if (definition.TableKind == TrainingJournalTableKind.OccupationalSafetyIntro9)
        {
            AppendIntroductoryDocument(sb, org, department, from, to, entries, showTitlePage, editable, ov);
        }
        else if (definition.TableKind == TrainingJournalTableKind.OccupationalSafety9)
        {
            AppendWorkplaceDocument(sb, org, department, from, to, entries, showTitlePage, editable, ov);
        }
        else if (definition.TableKind == TrainingJournalTableKind.ContractorOccupationalSafety9)
        {
            AppendContractorDocument(sb, org, department, from, to, entries, showTitlePage, editable, ov);
        }
        else         if (definition.Kind == TrainingJournalKind.IntroductoryFireSafety)
        {
            AppendFireIntroDocument(sb, org, department, from, to, entries, showTitlePage, editable, ov);
        }
        else if (definition.Kind == TrainingJournalKind.WorkplaceFireSafety)
        {
            AppendWorkplaceFireDocument(sb, org, department, from, to, entries, showTitlePage, editable, ov);
        }
        else if (definition.TableKind == TrainingJournalTableKind.FireSafetyContractors12)
        {
            AppendFireContractorDocument(sb, org, department, from, to, entries, showTitlePage, editable, ov);
        }
        else if (definition.Kind == TrainingJournalKind.ElectroSafetyGroupI)
        {
            AppendElectroDocument(sb, org, from, to, entries, showTitlePage, editable, ov);
        }
        else
        {
            if (showTitlePage)
            {
                AppendGenericTitlePage(sb, definition, org, department, from, to, editable, ov);
                sb.Append("<div class=\"journal-page-break\"></div>");
            }

            AppendGenericTable(sb, definition, entries, editable, ov);
            if (definition.ShowResponsibleBlock)
            {
                AppendResponsible(sb, editable, ov);
            }
        }

        sb.Append(fullDocument ? "</body></html>" : "</div>");
        return sb.ToString();
    }

    /// <summary>Бланк «Журнал регистрации вводного инструктажа» — 1в1 по DOCX, поля из наших данных.</summary>
    private static void AppendIntroductoryDocument(
        StringBuilder sb,
        string organization,
        string department,
        DateTime from,
        DateTime to,
        IReadOnlyList<TrainingJournalEntry> entries,
        bool showTitlePage,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        if (showTitlePage)
        {
            sb.Append("<section class=\"journal-intro-title\">");
            sb.Append("<div class=\"journal-intro-title-spacer\"></div>");

            var orgFallback = string.IsNullOrWhiteSpace(organization)
                ? string.Empty
                : organization;
            var orgText = O(ov, "org", orgFallback);
            if (string.IsNullOrWhiteSpace(orgText) ||
                orgText.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase))
            {
                orgText = orgFallback;
            }

            sb.Append("<div class=\"journal-intro-org\">");
            sb.Append(Editable(editable, "org", string.IsNullOrWhiteSpace(orgText) ? "\u00A0" : orgText));
            sb.Append("</div>");

            sb.Append("<div class=\"journal-intro-title-mid\">");
            sb.Append("<div class=\"journal-intro-h1\">ЖУРНАЛ</div>");
            sb.Append("<div class=\"journal-intro-h2\">регистрации вводного инструктажа по охране труда</div>");
            sb.Append("</div>");

            var objectFallback = string.IsNullOrWhiteSpace(department) ? string.Empty : department;
            var objectText = SanitizeTitleObject(O(ov, "object", objectFallback), objectFallback, entries);

            sb.Append("<div class=\"journal-intro-object\">");
            sb.Append("<span class=\"journal-intro-object-label\">Подразделение:&nbsp;</span>");
            sb.Append(Editable(editable, "object", string.IsNullOrWhiteSpace(objectText) ? "\u00A0" : objectText));
            sb.Append("</div>");

            // На титуле даты берутся из периода журнала (можно править вручную в предпросмотре).
            var started = O(ov, "started", ShortDate(from));
            var ended = O(ov, "ended", ShortDate(to));
            if (started.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                started = ShortDate(from);
            }

            if (ended.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                ended = ShortDate(to);
            }

            sb.Append("<div class=\"journal-intro-period\">");
            sb.Append("<div>Начат:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "started", started));
            sb.Append("</div><div>Окончен:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "ended", ended));
            sb.Append("</div></div>");

            sb.Append("</section>");
            sb.Append("<div class=\"journal-page-break\" aria-hidden=\"true\"></div>");
        }

        sb.Append("<section class=\"journal-intro-table-wrap journal-intro-page-2\">");
        sb.Append("<table class=\"journal-intro-table\">");
        sb.Append("<colgroup>");
        foreach (var pct in IntroColPercents)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{pct:0.##}%\" />");
        }

        sb.Append("</colgroup><thead><tr>");
        foreach (var header in IntroHeaders)
        {
            sb.Append("<th>").Append(E(header)).Append("</th>");
        }

        sb.Append("</tr><tr class=\"journal-intro-nums\">");
        for (var n = 1; n <= 8; n++)
        {
            sb.Append("<th>").Append(n).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");

        var dataRows = entries
            .Select((e, index) => (Entry: e, Index: index))
            .Where(x => HasIntroRowContent(x.Entry))
            .ToList();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var e = dataRows[i].Entry;
            var p = $"r{dataRows[i].Index}-";
            sb.Append("<tr>");
            AppendIntroTd(sb, editable, ov, p + "date", ShortDate(e.BriefingDate));
            AppendIntroTd(sb, editable, ov, p + "fio", e.EmployeeName);
            AppendIntroTd(sb, editable, ov, p + "prof", e.Profession);
            AppendIntroTd(sb, editable, ov, p + "birth", e.BirthDate is DateTime bd ? ShortDate(bd) : "");
            AppendIntroTd(sb, editable, ov, p + "dept", e.Department);
            AppendIntroTd(sb, editable, ov, p + "instr", FormatInstructor(e));
            AppendIntroTd(sb, editable, ov, p + "sign1", "");
            AppendIntroTd(sb, editable, ov, p + "sign2", "");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></section>");
    }

    private static bool HasIntroRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.Department) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        e.BriefingDate != default;

    /// <summary>Бланк «Журнал регистрации инструктажа на рабочем месте» — 1в1 по DOCX.</summary>
    private static void AppendWorkplaceDocument(
        StringBuilder sb,
        string organization,
        string department,
        DateTime from,
        DateTime to,
        IReadOnlyList<TrainingJournalEntry> entries,
        bool showTitlePage,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        if (showTitlePage)
        {
            sb.Append("<section class=\"journal-intro-title\">");
            sb.Append("<div class=\"journal-intro-title-spacer\"></div>");

            var orgFallback = string.IsNullOrWhiteSpace(organization) ? string.Empty : organization;
            var orgText = O(ov, "org", orgFallback);
            if (string.IsNullOrWhiteSpace(orgText) ||
                orgText.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase))
            {
                orgText = orgFallback;
            }

            sb.Append("<div class=\"journal-intro-org\">");
            sb.Append(Editable(editable, "org", string.IsNullOrWhiteSpace(orgText) ? "\u00A0" : orgText));
            sb.Append("</div>");

            sb.Append("<div class=\"journal-intro-title-mid\">");
            sb.Append("<div class=\"journal-intro-h1\">ЖУРНАЛ</div>");
            sb.Append("<div class=\"journal-intro-h2\">регистрации инструктажа по охране труда на рабочем месте</div>");
            sb.Append("</div>");

            var objectFallback = string.IsNullOrWhiteSpace(department) ? string.Empty : department;
            var objectText = SanitizeTitleObject(O(ov, "object", objectFallback), objectFallback, entries);

            sb.Append("<div class=\"journal-intro-object\">");
            sb.Append(Editable(editable, "object", string.IsNullOrWhiteSpace(objectText) ? "\u00A0" : objectText));
            sb.Append("</div>");

            var started = O(ov, "started", ShortDate(from));
            var ended = O(ov, "ended", ShortDate(to));
            if (started.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                started = ShortDate(from);
            }

            if (ended.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                ended = ShortDate(to);
            }

            sb.Append("<div class=\"journal-intro-period\">");
            sb.Append("<div>Начат:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "started", started));
            sb.Append("</div><div>Окончен:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "ended", ended));
            sb.Append("</div></div>");

            sb.Append("</section>");
            sb.Append("<div class=\"journal-page-break\" aria-hidden=\"true\"></div>");
        }

        sb.Append("<section class=\"journal-intro-table-wrap journal-intro-page-2\">");
        sb.Append("<table class=\"journal-intro-table journal-workplace-table\">");
        sb.Append("<colgroup>");
        foreach (var pct in WorkplaceColPercents)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{pct:0.##}%\" />");
        }

        sb.Append("</colgroup><thead><tr>");
        foreach (var header in WorkplaceHeaders)
        {
            sb.Append("<th>").Append(E(header)).Append("</th>");
        }

        sb.Append("</tr><tr class=\"journal-intro-nums\">");
        for (var n = 1; n <= 10; n++)
        {
            sb.Append("<th>").Append(n).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");

        var dataRows = entries
            .Select((e, index) => (Entry: e, Index: index))
            .Where(x => HasWorkplaceRowContent(x.Entry))
            .ToList();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var e = dataRows[i].Entry;
            var p = $"r{dataRows[i].Index}-";
            var localAct = string.IsNullOrWhiteSpace(e.LocalActName) ? e.Workplace : e.LocalActName;
            sb.Append("<tr>");
            AppendIntroTd(sb, editable, ov, p + "date", ShortDate(e.BriefingDate));
            AppendIntroTd(sb, editable, ov, p + "fio", e.EmployeeName);
            AppendIntroTd(sb, editable, ov, p + "prof", e.Profession);
            AppendIntroTd(sb, editable, ov, p + "birth", e.BirthDate is DateTime bd ? ShortDate(bd) : "");
            AppendIntroTd(sb, editable, ov, p + "type", e.BriefingType);
            AppendIntroTd(sb, editable, ov, p + "reason", e.BriefingReason);
            AppendIntroTd(sb, editable, ov, p + "instr", FormatInstructor(e));
            AppendIntroTd(sb, editable, ov, p + "act", localAct);
            AppendIntroTd(sb, editable, ov, p + "sign1", "");
            AppendIntroTd(sb, editable, ov, p + "sign2", "");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></section>");
    }

    private static bool HasWorkplaceRowContent(TrainingJournalEntry e) =>
        HasIntroRowContent(e) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        !string.IsNullOrWhiteSpace(e.BriefingReason) ||
        !string.IsNullOrWhiteSpace(e.LocalActName) ||
        !string.IsNullOrWhiteSpace(e.Workplace);

    /// <summary>Бланк «Журнал регистрации вводного инструктажа… для подрядных» — 1в1 по DOCX.</summary>
    private static void AppendContractorDocument(
        StringBuilder sb,
        string organization,
        string department,
        DateTime from,
        DateTime to,
        IReadOnlyList<TrainingJournalEntry> entries,
        bool showTitlePage,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        if (showTitlePage)
        {
            sb.Append("<section class=\"journal-intro-title journal-contractor-title\">");
            sb.Append("<div class=\"journal-intro-title-spacer\"></div>");

            var orgFallback = string.IsNullOrWhiteSpace(organization) ? string.Empty : organization;
            var orgText = O(ov, "org", orgFallback);
            if (string.IsNullOrWhiteSpace(orgText) ||
                orgText.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase) ||
                orgText.Contains("Наименование организации", StringComparison.OrdinalIgnoreCase))
            {
                orgText = orgFallback;
            }

            sb.Append("<div class=\"journal-intro-org\">");
            sb.Append(Editable(editable, "org", string.IsNullOrWhiteSpace(orgText) ? "\u00A0" : orgText));
            sb.Append("</div>");

            var objectFallback = string.IsNullOrWhiteSpace(department) ? string.Empty : department;
            var objectText = SanitizeTitleObject(O(ov, "object", objectFallback), objectFallback, entries);

            sb.Append("<div class=\"journal-intro-title-mid journal-contractor-mid\">");
            sb.Append("<div class=\"journal-intro-h1\">ЖУРНАЛ</div>");
            sb.Append("<div class=\"journal-intro-h2\">регистрации вводного инструктажа по охране труда</div>");
            sb.Append("<div class=\"journal-intro-h3\">(для подрядных организаций)</div>");
            sb.Append("<div class=\"journal-contractor-object\">");
            sb.Append(Editable(editable, "object", string.IsNullOrWhiteSpace(objectText) ? "\u00A0" : objectText));
            sb.Append("</div>");
            sb.Append("</div>");

            var started = O(ov, "started", ShortDate(from));
            var ended = O(ov, "ended", ShortDate(to));
            if (started.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                started = ShortDate(from);
            }

            if (ended.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                ended = ShortDate(to);
            }

            sb.Append("<div class=\"journal-intro-period\">");
            sb.Append("<div>Начат:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "started", started));
            sb.Append("</div><div>Окончен:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "ended", ended));
            sb.Append("</div></div>");

        sb.Append("</section>");
            sb.Append("<div class=\"journal-page-break\" aria-hidden=\"true\"></div>");
        }

        sb.Append("<section class=\"journal-intro-table-wrap journal-intro-page-2\">");
        sb.Append("<table class=\"journal-intro-table\">");
        sb.Append("<colgroup>");
        foreach (var pct in ContractorColPercents)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{pct:0.##}%\" />");
        }

        sb.Append("</colgroup><thead><tr>");
        foreach (var header in ContractorHeaders)
        {
            sb.Append("<th>").Append(E(header)).Append("</th>");
        }

        sb.Append("</tr><tr class=\"journal-intro-nums\">");
        for (var n = 1; n <= 8; n++)
        {
            sb.Append("<th>").Append(n).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");

        var dataRows = entries
            .Select((e, index) => (Entry: e, Index: index))
            .Where(x => HasContractorRowContent(x.Entry))
            .ToList();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var e = dataRows[i].Entry;
            var p = $"r{dataRows[i].Index}-";
            var dept = ResolveContractorDepartment(e);
            sb.Append("<tr>");
            AppendIntroTd(sb, editable, ov, p + "date", ShortDate(e.BriefingDate));
            AppendIntroTd(sb, editable, ov, p + "fio", e.EmployeeName);
            AppendIntroTd(sb, editable, ov, p + "prof", e.Profession);
            AppendIntroTd(sb, editable, ov, p + "birth", e.BirthDate is DateTime bd ? ShortDate(bd) : "");
            AppendIntroTd(sb, editable, ov, p + "dept", dept);
            AppendIntroTd(sb, editable, ov, p + "instr", FormatInstructor(e));
            AppendIntroTd(sb, editable, ov, p + "sign1", "");
            AppendIntroTd(sb, editable, ov, p + "sign2", "");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></section>");
    }

    private static bool HasContractorRowContent(TrainingJournalEntry e) =>
        HasIntroRowContent(e) || !string.IsNullOrWhiteSpace(e.LegalEntity);

    private static string ResolveContractorDepartment(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.Department)
            ? e.Department
            : e.LegalEntity;

    /// <summary>Бланк «Журнал регистрации противопожарных инструктажей на рабочем месте» — 11 граф по DOCX.</summary>
    private static void AppendWorkplaceFireDocument(
        StringBuilder sb,
        string organization,
        string department,
        DateTime from,
        DateTime to,
        IReadOnlyList<TrainingJournalEntry> entries,
        bool showTitlePage,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        if (showTitlePage)
        {
            sb.Append("<section class=\"journal-intro-title\">");
            sb.Append("<div class=\"journal-intro-title-spacer\"></div>");

            var orgFallback = string.IsNullOrWhiteSpace(organization) ? string.Empty : organization;
            var orgText = O(ov, "org", orgFallback);
            if (string.IsNullOrWhiteSpace(orgText) ||
                orgText.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase) ||
                orgText.Contains("Наименование организации", StringComparison.OrdinalIgnoreCase))
            {
                orgText = orgFallback;
            }

            sb.Append("<div class=\"journal-intro-org\">");
            sb.Append(Editable(editable, "org", string.IsNullOrWhiteSpace(orgText) ? "\u00A0" : orgText));
            sb.Append("</div>");

            sb.Append("<div class=\"journal-intro-title-mid\">");
            sb.Append("<div class=\"journal-intro-h1\">ЖУРНАЛ</div>");
            sb.Append("<div class=\"journal-intro-h2\">регистрации противопожарных инструктажей</div>");
            sb.Append("<div class=\"journal-intro-h3\">(на рабочем месте)</div>");
            sb.Append("</div>");

            var objectFallback = string.IsNullOrWhiteSpace(department) ? string.Empty : department;
            var objectText = SanitizeTitleObject(O(ov, "object", objectFallback), objectFallback, entries);

            sb.Append("<div class=\"journal-intro-object\">");
            sb.Append(Editable(editable, "object", string.IsNullOrWhiteSpace(objectText) ? "\u00A0" : objectText));
            sb.Append("</div>");

            var started = O(ov, "started", ShortDate(from));
            var ended = O(ov, "ended", ShortDate(to));
            if (started.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                started = ShortDate(from);
            }

            if (ended.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                ended = ShortDate(to);
            }

            sb.Append("<div class=\"journal-intro-period\">");
            sb.Append("<div>Начат:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "started", started));
            sb.Append("</div><div>Окончен:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "ended", ended));
            sb.Append("</div></div>");

            sb.Append("</section>");
            sb.Append("<div class=\"journal-page-break\" aria-hidden=\"true\"></div>");
        }

        // 564,1439,1027,1306,2724,1182,1187,564,2051,1569,1815
        var colPct = new[] { 3.66, 9.33, 6.66, 8.47, 17.67, 7.67, 7.70, 3.66, 13.30, 10.18, 11.77 };
        const string instructorHeader =
            "ФИО (при наличии) инструктирующего, номер документа об образовании и (или) квалификации или документа об обучении инструктирующего";

        sb.Append("<section class=\"journal-intro-table-wrap journal-intro-page-2\">");
        sb.Append("<table class=\"journal-intro-table journal-fire-contractor-table\">");
        sb.Append("<colgroup>");
        foreach (var pct in colPct)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{pct:0.##}%\" />");
        }

        sb.Append("</colgroup><thead>");
        sb.Append("<tr>");
        sb.Append("<th rowspan=\"3\">Дата</th>");
        sb.Append("<th rowspan=\"3\">Вид проводимого инструктажа</th>");
        sb.Append("<th colspan=\"2\" rowspan=\"2\">Инструктируемый</th>");
        sb.Append("<th colspan=\"3\">Теоретическая часть</th>");
        sb.Append("<th rowspan=\"3\">Дата</th>");
        sb.Append("<th colspan=\"3\">Практическая часть</th>");
        sb.Append("</tr><tr>");
        sb.Append("<th rowspan=\"2\">").Append(E(instructorHeader)).Append("</th>");
        sb.Append("<th colspan=\"2\">Подпись</th>");
        sb.Append("<th rowspan=\"2\">").Append(E(instructorHeader)).Append("</th>");
        sb.Append("<th colspan=\"2\">Подпись</th>");
        sb.Append("</tr><tr>");
        sb.Append("<th>ФИО (при наличии)</th>");
        sb.Append("<th>Профессия, должность</th>");
        sb.Append("<th>инструктируемого</th>");
        sb.Append("<th>инструктирующего</th>");
        sb.Append("<th>инструктируемого</th>");
        sb.Append("<th>инструктирующего</th>");
        sb.Append("</tr><tr class=\"journal-intro-nums\">");
        for (var n = 1; n <= 11; n++)
        {
            sb.Append("<th>").Append(n).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");

        var dataRows = entries
            .Select((e, index) => (Entry: e, Index: index))
            .Where(x => HasWorkplaceFireRowContent(x.Entry))
            .ToList();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var e = dataRows[i].Entry;
            var p = $"r{dataRows[i].Index}-";
            var practiceDate = ShortDate(e.PracticeDate ?? e.TheoryDate ?? e.BriefingDate);
            sb.Append("<tr>");
            AppendIntroTd(sb, editable, ov, p + "date", ShortDate(e.BriefingDate));
            AppendIntroTd(sb, editable, ov, p + "type", e.BriefingType);
            AppendIntroTd(sb, editable, ov, p + "fio", e.EmployeeName);
            AppendIntroTd(sb, editable, ov, p + "prof", e.Profession);
            AppendIntroTd(sb, editable, ov, p + "instr", FormatInstructor(e));
            AppendIntroTd(sb, editable, ov, p + "sign1", "");
            AppendIntroTd(sb, editable, ov, p + "sign2", "");
            AppendIntroTd(sb, editable, ov, p + "pdate", practiceDate);
            AppendIntroTd(sb, editable, ov, p + "pinstr", FormatInstructor(e));
            AppendIntroTd(sb, editable, ov, p + "sign3", "");
            AppendIntroTd(sb, editable, ov, p + "sign4", "");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></section>");
    }

    private static bool HasWorkplaceFireRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        e.BriefingDate != default;

    /// <summary>Бланк «Журнал регистрации присвоения группы I по электробезопасности» — 7 граф по DOCX.</summary>
    private static void AppendElectroDocument(
        StringBuilder sb,
        string organization,
        DateTime from,
        DateTime to,
        IReadOnlyList<TrainingJournalEntry> entries,
        bool showTitlePage,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        if (showTitlePage)
        {
            sb.Append("<section class=\"journal-intro-title\">");
            sb.Append("<div class=\"journal-intro-title-spacer\"></div>");

            var orgFallback = string.IsNullOrWhiteSpace(organization) ? string.Empty : organization;
            var orgText = O(ov, "org", orgFallback);
            if (string.IsNullOrWhiteSpace(orgText) ||
                orgText.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase) ||
                orgText.Contains("Наименование организации", StringComparison.OrdinalIgnoreCase))
            {
                orgText = orgFallback;
            }

            sb.Append("<div class=\"journal-intro-org\">");
            sb.Append(Editable(editable, "org", string.IsNullOrWhiteSpace(orgText) ? "\u00A0" : orgText));
            sb.Append("</div>");

            sb.Append("<div class=\"journal-intro-title-mid\">");
            sb.Append("<div class=\"journal-intro-h1\">ЖУРНАЛ</div>");
            sb.Append("<div class=\"journal-intro-h2\">Присвоения группы I по электробезопасности</div>");
            sb.Append("<div class=\"journal-intro-h3\">неэлектротехническому</div>");
            var journalNo = O(ov, "journalNo", "персоналу N ____");
            if (journalNo.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                journalNo = "персоналу N ____";
            }

            sb.Append("<div class=\"journal-intro-h3\">");
            sb.Append(Editable(editable, "journalNo", journalNo));
            sb.Append("</div>");
            sb.Append("</div>");

            var started = O(ov, "started", ShortDate(from));
            var ended = O(ov, "ended", ShortDate(to));
            if (started.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                started = ShortDate(from);
            }

            if (ended.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                ended = ShortDate(to);
            }

            sb.Append("<div class=\"journal-intro-period\">");
            sb.Append("<div>Начат:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "started", started));
            sb.Append("</div><div>Окончен:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "ended", ended));
            sb.Append("</div></div>");

            sb.Append("</section>");
            sb.Append("<div class=\"journal-page-break\" aria-hidden=\"true\"></div>");
        }

        // 2863,2835,2127,1701,1559,2410,1842
        var colPct = new[] { 18.67, 18.48, 13.87, 11.09, 10.17, 15.71, 12.01 };
        sb.Append("<section class=\"journal-intro-table-wrap journal-intro-page-2\">");
        sb.Append("<table class=\"journal-intro-table journal-workplace-table\">");
        sb.Append("<colgroup>");
        foreach (var pct in colPct)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{pct:0.##}%\" />");
        }

        sb.Append("</colgroup><thead>");
        sb.Append("<tr>");
        sb.Append("<th rowspan=\"2\">Фамилия, имя, отчество</th>");
        sb.Append("<th rowspan=\"2\">Наименование подразделения</th>");
        sb.Append("<th rowspan=\"2\">Должность (профессия)</th>");
        sb.Append("<th rowspan=\"2\">Дата предыдущего присвоения</th>");
        sb.Append("<th rowspan=\"2\">Дата присвоения</th>");
        sb.Append("<th colspan=\"2\">Подпись</th>");
        sb.Append("</tr><tr>");
        sb.Append("<th>проверяемого</th>");
        sb.Append("<th>проверяющего</th>");
        sb.Append("</tr><tr class=\"journal-intro-nums\">");
        for (var n = 1; n <= 7; n++)
        {
            sb.Append("<th>").Append(n).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");

        var dataRows = entries
            .Select((e, index) => (Entry: e, Index: index))
            .Where(x => HasElectroRowContent(x.Entry))
            .ToList();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var e = dataRows[i].Entry;
            var p = $"r{dataRows[i].Index}-";
            sb.Append("<tr>");
            AppendIntroTd(sb, editable, ov, p + "fio", e.EmployeeName);
            AppendIntroTd(sb, editable, ov, p + "dept", e.Department);
            AppendIntroTd(sb, editable, ov, p + "prof", e.Profession);
            AppendIntroTd(sb, editable, ov, p + "prev", e.PreviousAssignmentDate is DateTime pd ? ShortDate(pd) : "");
            AppendIntroTd(sb, editable, ov, p + "date", ShortDate(e.BriefingDate));
            AppendIntroTd(sb, editable, ov, p + "sign1", "");
            AppendIntroTd(sb, editable, ov, p + "sign2", "");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></section>");
    }

    private static bool HasElectroRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Department) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        e.PreviousAssignmentDate is not null ||
        e.BriefingDate != default;

    /// <summary>Бланк «Журнал регистрации вводного противопожарного инструктажа» — 7 граф по DOCX.</summary>
    private static void AppendFireIntroDocument(
        StringBuilder sb,
        string organization,
        string department,
        DateTime from,
        DateTime to,
        IReadOnlyList<TrainingJournalEntry> entries,
        bool showTitlePage,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        if (showTitlePage)
        {
            sb.Append("<section class=\"journal-intro-title\">");
            sb.Append("<div class=\"journal-intro-title-spacer\"></div>");

            var orgFallback = string.IsNullOrWhiteSpace(organization) ? string.Empty : organization;
            var orgText = O(ov, "org", orgFallback);
            if (string.IsNullOrWhiteSpace(orgText) ||
                orgText.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase) ||
                orgText.Contains("Наименование организации", StringComparison.OrdinalIgnoreCase))
            {
                orgText = orgFallback;
            }

            sb.Append("<div class=\"journal-intro-org\">");
            sb.Append(Editable(editable, "org", string.IsNullOrWhiteSpace(orgText) ? "\u00A0" : orgText));
            sb.Append("</div>");

            sb.Append("<div class=\"journal-intro-title-mid\">");
            sb.Append("<div class=\"journal-intro-h1\">ЖУРНАЛ</div>");
            sb.Append("<div class=\"journal-intro-h2\">регистрации вводного противопожарного инструктажа</div>");
            sb.Append("</div>");

            var objectFallback = string.IsNullOrWhiteSpace(department) ? string.Empty : department;
            var objectText = SanitizeTitleObject(O(ov, "object", objectFallback), objectFallback, entries);

            sb.Append("<div class=\"journal-intro-object\">");
            sb.Append("<span class=\"journal-intro-object-label\">Подразделение:&nbsp;</span>");
            sb.Append(Editable(editable, "object", string.IsNullOrWhiteSpace(objectText) ? "\u00A0" : objectText));
            sb.Append("</div>");

            var started = O(ov, "started", ShortDate(from));
            var ended = O(ov, "ended", ShortDate(to));
            if (started.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                started = ShortDate(from);
            }

            if (ended.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                ended = ShortDate(to);
            }

            sb.Append("<div class=\"journal-intro-period\">");
            sb.Append("<div>Начат:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "started", started));
            sb.Append("</div><div>Окончен:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "ended", ended));
            sb.Append("</div></div>");

            sb.Append("</section>");
            sb.Append("<div class=\"journal-page-break\" aria-hidden=\"true\"></div>");
        }

        // 565,1324,3819,2214,3095,1967,2444
        var colPct = new[] { 3.66, 8.58, 24.75, 14.35, 20.06, 12.75, 15.84 };
        sb.Append("<section class=\"journal-intro-table-wrap journal-intro-page-2\">");
        sb.Append("<table class=\"journal-intro-table journal-fire-contractor-table\">");
        sb.Append("<colgroup>");
        foreach (var pct in colPct)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{pct:0.##}%\" />");
        }

        sb.Append("</colgroup><thead>");
        sb.Append("<tr>");
        sb.Append("<th rowspan=\"3\">Дата</th>");
        sb.Append("<th rowspan=\"3\">Вид проводимого инструктажа</th>");
        sb.Append("<th colspan=\"2\" rowspan=\"2\">Инструктируемый</th>");
        sb.Append("<th colspan=\"3\">Теоретическая часть</th>");
        sb.Append("</tr><tr>");
        sb.Append("<th rowspan=\"2\">Фамилия, имя отчество (при наличии) инструктирующего, номер документа об образовании и (или) квалификации или документа об обучении инструктирующего</th>");
        sb.Append("<th colspan=\"2\">Подпись</th>");
        sb.Append("</tr><tr>");
        sb.Append("<th>Фамилия, имя отчество (при наличии)</th>");
        sb.Append("<th>Профессия, должность</th>");
        sb.Append("<th>инструктируемого</th>");
        sb.Append("<th>инструктирующего</th>");
        sb.Append("</tr><tr class=\"journal-intro-nums\">");
        for (var n = 1; n <= 7; n++)
        {
            sb.Append("<th>").Append(n).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");

        var dataRows = entries
            .Select((e, index) => (Entry: e, Index: index))
            .Where(x => HasFireIntroRowContent(x.Entry))
            .ToList();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var e = dataRows[i].Entry;
            var p = $"r{dataRows[i].Index}-";
            sb.Append("<tr>");
            AppendIntroTd(sb, editable, ov, p + "date", ShortDate(e.BriefingDate));
            AppendIntroTd(sb, editable, ov, p + "type", e.BriefingType);
            AppendIntroTd(sb, editable, ov, p + "fio", e.EmployeeName);
            AppendIntroTd(sb, editable, ov, p + "prof", e.Profession);
            AppendIntroTd(sb, editable, ov, p + "instr", FormatInstructor(e));
            AppendIntroTd(sb, editable, ov, p + "sign1", "");
            AppendIntroTd(sb, editable, ov, p + "sign2", "");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></section>");
    }

    private static bool HasFireIntroRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        e.BriefingDate != default;

    /// <summary>Бланк «Журнал регистрации вводного противопожарного… для подрядных» — 8 граф по DOCX.</summary>
    private static void AppendFireContractorDocument(
        StringBuilder sb,
        string organization,
        string department,
        DateTime from,
        DateTime to,
        IReadOnlyList<TrainingJournalEntry> entries,
        bool showTitlePage,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        if (showTitlePage)
        {
            sb.Append("<section class=\"journal-intro-title journal-contractor-title\">");
            sb.Append("<div class=\"journal-intro-title-spacer\"></div>");

            var orgFallback = string.IsNullOrWhiteSpace(organization) ? string.Empty : organization;
            var orgText = O(ov, "org", orgFallback);
            if (string.IsNullOrWhiteSpace(orgText) ||
                orgText.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase) ||
                orgText.Contains("Наименование организации", StringComparison.OrdinalIgnoreCase))
            {
                orgText = orgFallback;
            }

            sb.Append("<div class=\"journal-intro-org\">");
            sb.Append(Editable(editable, "org", string.IsNullOrWhiteSpace(orgText) ? "\u00A0" : orgText));
            sb.Append("</div>");

            var objectFallback = string.IsNullOrWhiteSpace(department) ? string.Empty : department;
            var objectText = SanitizeTitleObject(O(ov, "object", objectFallback), objectFallback, entries);

            sb.Append("<div class=\"journal-intro-title-mid journal-contractor-mid\">");
            sb.Append("<div class=\"journal-intro-h1\">ЖУРНАЛ</div>");
            sb.Append("<div class=\"journal-intro-h2\">регистрации вводного противопожарного инструктажа</div>");
            sb.Append("<div class=\"journal-intro-h3\">(для подрядных организаций)</div>");
            sb.Append("<div class=\"journal-contractor-object\">");
            sb.Append(Editable(editable, "object", string.IsNullOrWhiteSpace(objectText) ? "\u00A0" : objectText));
            sb.Append("</div>");
            sb.Append("</div>");

            var started = O(ov, "started", ShortDate(from));
            var ended = O(ov, "ended", ShortDate(to));
            if (started.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                started = ShortDate(from);
            }

            if (ended.Contains("поле которое", StringComparison.OrdinalIgnoreCase))
            {
                ended = ShortDate(to);
            }

            sb.Append("<div class=\"journal-intro-period\">");
            sb.Append("<div>Начат:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "started", started));
            sb.Append("</div><div>Окончен:&nbsp;&nbsp;");
            sb.Append(Editable(editable, "ended", ended));
            sb.Append("</div></div>");

            sb.Append("</section>");
            sb.Append("<div class=\"journal-page-break\" aria-hidden=\"true\"></div>");
        }

        var colPct = new[] { 3.66, 8.58, 9.17, 15.58, 14.35, 20.06, 12.75, 15.84 };
        sb.Append("<section class=\"journal-intro-table-wrap journal-intro-page-2\">");
        sb.Append("<table class=\"journal-intro-table journal-fire-contractor-table\">");
        sb.Append("<colgroup>");
        foreach (var pct in colPct)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{pct:0.##}%\" />");
        }

        sb.Append("</colgroup><thead>");
        sb.Append("<tr>");
        sb.Append("<th rowspan=\"3\">Дата</th>");
        sb.Append("<th rowspan=\"3\">Вид проводимого инструктажа</th>");
        sb.Append("<th colspan=\"3\" rowspan=\"2\">Инструктируемый</th>");
        sb.Append("<th colspan=\"3\">Теоретическая часть</th>");
        sb.Append("</tr><tr>");
        sb.Append("<th rowspan=\"2\">Фамилия, имя отчество (при наличии) инструктирующего, номер документа об образовании и (или) квалификации или документа об обучении инструктирующего</th>");
        sb.Append("<th colspan=\"2\">Подпись</th>");
        sb.Append("</tr><tr>");
        sb.Append("<th>Наименование подрядной организации</th>");
        sb.Append("<th>Фамилия, имя отчество (при наличии)</th>");
        sb.Append("<th>Профессия, должность</th>");
        sb.Append("<th>инструктируемого</th>");
        sb.Append("<th>инструктирующего</th>");
        sb.Append("</tr><tr class=\"journal-intro-nums\">");
        for (var n = 1; n <= 8; n++)
        {
            sb.Append("<th>").Append(n).Append("</th>");
        }

        sb.Append("</tr></thead><tbody>");

        var dataRows = entries
            .Select((e, index) => (Entry: e, Index: index))
            .Where(x => HasFireContractorRowContent(x.Entry))
            .ToList();

        for (var i = 0; i < dataRows.Count; i++)
        {
            var e = dataRows[i].Entry;
            var p = $"r{dataRows[i].Index}-";
            sb.Append("<tr>");
            AppendIntroTd(sb, editable, ov, p + "date", ShortDate(e.BriefingDate));
            AppendIntroTd(sb, editable, ov, p + "type", e.BriefingType);
            AppendIntroTd(sb, editable, ov, p + "legal", e.LegalEntity);
            AppendIntroTd(sb, editable, ov, p + "fio", e.EmployeeName);
            AppendIntroTd(sb, editable, ov, p + "prof", e.Profession);
            AppendIntroTd(sb, editable, ov, p + "instr", FormatInstructor(e));
            AppendIntroTd(sb, editable, ov, p + "sign1", "");
            AppendIntroTd(sb, editable, ov, p + "sign2", "");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table></section>");
    }

    private static bool HasFireContractorRowContent(TrainingJournalEntry e) =>
        !string.IsNullOrWhiteSpace(e.EmployeeName) ||
        !string.IsNullOrWhiteSpace(e.Profession) ||
        !string.IsNullOrWhiteSpace(e.LegalEntity) ||
        !string.IsNullOrWhiteSpace(e.InstructorName) ||
        !string.IsNullOrWhiteSpace(e.BriefingType) ||
        e.BriefingDate != default;

    private static void AppendIntroTd(
        StringBuilder sb,
        bool editable,
        IReadOnlyDictionary<string, string> ov,
        string key,
        string value)
    {
        var text = O(ov, key, value);
        if (editable)
        {
            sb.Append("<td contenteditable=\"true\" data-override-key=\"").Append(E(key)).Append("\">");
            sb.Append(string.IsNullOrEmpty(text) ? "" : E(text));
            sb.Append("</td>");
        }
        else
        {
            sb.Append("<td>").Append(string.IsNullOrEmpty(text) ? "&nbsp;" : E(text)).Append("</td>");
        }
    }

    private static void AppendGenericTitlePage(
        StringBuilder sb,
        TrainingJournalDefinition definition,
        string organization,
        string department,
        DateTime from,
        DateTime to,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        sb.Append("<section class=\"journal-title-page\">");

        if (definition.UseOfficialOtTitlePage)
        {
            sb.Append("<div class=\"journal-title-org\">");
            sb.Append(Editable(editable, "org", O(ov, "org", organization)));
            sb.Append("</div>");
            sb.Append("<div class=\"journal-title-line\"></div>");
            sb.Append("<div class=\"journal-title-caption\">(наименование организации)</div>");

            if (!string.IsNullOrWhiteSpace(department))
            {
                sb.Append("<div class=\"journal-title-dept\">");
                sb.Append(Editable(editable, "dept", O(ov, "dept", department)));
                sb.Append("</div>");
                sb.Append("<div class=\"journal-title-line\"></div>");
            }

            foreach (var line in definition.TitlePageHeadingLines)
            {
                sb.Append($"<div class=\"journal-title-heading\">{E(line)}</div>");
            }

            AppendPeriod(sb, from, to, editable, ov);
        }
        else
    {
        if (definition.TitlePageAppendixLines is { Length: > 0 } appendix)
        {
            sb.Append("<div class=\"journal-appendix\">");
            foreach (var line in appendix)
            {
                sb.Append($"<div>{E(line)}</div>");
            }

            sb.Append("</div>");
        }

            if (!string.IsNullOrWhiteSpace(organization))
            {
                sb.Append("<div class=\"journal-title-org\">");
                sb.Append(Editable(editable, "org", O(ov, "org", organization)));
                sb.Append("</div>");
            }

            if (!string.IsNullOrWhiteSpace(definition.TitlePageSubCaption))
            {
            sb.Append($"<div class=\"journal-title-caption\">{E(definition.TitlePageSubCaption)}</div>");
            }

        foreach (var line in definition.TitlePageHeadingLines)
        {
            sb.Append($"<div class=\"journal-title-heading\">{E(line)}</div>");
        }

            AppendPeriod(sb, from, to, editable, ov);
        }

        sb.Append("</section>");
    }

    private static void AppendPeriod(
        StringBuilder sb,
        DateTime from,
        DateTime to,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        sb.Append("<div class=\"journal-period\">");
        sb.Append("<div>Начат&nbsp;&nbsp;");
        sb.Append(Editable(editable, "started", O(ov, "started", ShortDate(from))));
        sb.Append("</div><div>Окончен&nbsp;&nbsp;");
        sb.Append(Editable(editable, "ended", O(ov, "ended", ShortDate(to))));
        sb.Append("</div></div>");
    }

    private static void AppendGenericTable(
        StringBuilder sb,
        TrainingJournalDefinition definition,
        IReadOnlyList<TrainingJournalEntry> entries,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        var columns = TrainingJournalColumns.GetColumns(definition.Kind);
        sb.Append("<section class=\"journal-table-section\">");
        sb.Append("<table class=\"journal-table\">");
        sb.Append("<thead><tr>");
        foreach (var col in columns)
        {
            sb.Append($"<th>{E(col.Title)}</th>");
        }

        sb.Append("</tr></thead><tbody>");

        if (entries.Count == 0)
        {
            sb.Append("<tr><td colspan=\"").Append(columns.Count).Append("\">&nbsp;</td></tr>");
        }
        else
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                sb.Append("<tr>");
                foreach (var col in columns)
                {
                    var value = TrainingJournalColumns.GetCellValue(entry, col.Key);
                    var key = $"r{i}-{col.Key}";
                    sb.Append("<td>");
                    sb.Append(Editable(editable, key, O(ov, key, value)));
                    sb.Append("</td>");
                }

                sb.Append("</tr>");
            }
        }

        sb.Append("</tbody></table></section>");
    }

    private static void AppendResponsible(
        StringBuilder sb,
        bool editable,
        IReadOnlyDictionary<string, string> ov)
    {
        sb.Append("""
            <div class="journal-responsible">
              <div>Ответственный:</div>
              <div class="journal-responsible-line">
            """);
        sb.Append(Editable(editable, "resp-line", O(ov, "resp-line", "________________      ________________      ________________")));
        sb.Append("""
              </div>
              <div class="journal-responsible-hint">(должность) &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; (подпись) &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; (расшифровка подписи)</div>
            </div>
            """);
    }

    private static string Editable(bool editable, string key, string value)
    {
        var text = E(value);
        if (!editable)
        {
            return string.IsNullOrEmpty(text) ? "&nbsp;" : text;
        }

        return $"<span contenteditable=\"true\" data-override-key=\"{E(key)}\">{text}</span>";
    }

    private static string O(IReadOnlyDictionary<string, string> ov, string key, string fallback) =>
        ov.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string ResolveOrg(TrainingJournalParams parameters, IReadOnlyList<TrainingJournalEntry> entries)
    {
        var org = parameters.Organization;
        if (string.IsNullOrWhiteSpace(org) && entries.Count > 0)
        {
            org = entries[0].Organization;
        }

        return org ?? string.Empty;
    }

    private static string ResolveDepartment(TrainingJournalParams parameters, IReadOnlyList<TrainingJournalEntry> entries)
    {
        // На титуле только явно выбранное подразделение. «— не указано —» = пусто.
        // Не подставляем профессию / «рабочее место» из строк журнала.
        var department = CleanDepartment(parameters.Department, parameters.Organization);
        if (string.IsNullOrWhiteSpace(department))
        {
            return string.Empty;
        }

        if (IsProfessionPollution(department, entries))
        {
            return string.Empty;
        }

        return department;
    }

    /// <summary>
    /// Значение похоже на профессию из строк журнала (раньше ошибочно писали должность в Workplace и на титул).
    /// </summary>
    private static bool IsProfessionPollution(string? value, IReadOnlyList<TrainingJournalEntry> entries)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || entries.Count == 0)
        {
            return false;
        }

        foreach (var e in entries)
        {
            if (!string.IsNullOrWhiteSpace(e.Profession) &&
                string.Equals(text, e.Profession.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Workplace раньше заполняли профессией — тоже отсекаем.
            if (!string.IsNullOrWhiteSpace(e.Workplace) &&
                string.Equals(text, e.Workplace.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(e.Department) ||
                 !string.Equals(text, e.Department.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                if (string.Equals(e.Workplace.Trim(), e.Profession?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string SanitizeTitleObject(
        string? candidate,
        string fallback,
        IReadOnlyList<TrainingJournalEntry> entries)
    {
        var text = (candidate ?? string.Empty).Trim();
        if (text.Length == 0 ||
            text.Contains("поле которое заполняется", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, "Объект", StringComparison.OrdinalIgnoreCase) ||
            IsProfessionPollution(text, entries))
        {
            return fallback;
        }

        return text;
    }

    private static string CleanDepartment(string? department, string? organization)
    {
        var dept = (department ?? string.Empty).Trim();
        if (dept.Length == 0 ||
            dept is "—" or "-" ||
            dept.Contains("не указано", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(organization))
        {
            var org = organization.Trim();
            if (string.Equals(dept, org, StringComparison.OrdinalIgnoreCase) ||
                (dept.Length >= 8 && org.StartsWith(dept, StringComparison.OrdinalIgnoreCase)) ||
                (org.Length >= 8 && dept.StartsWith(org, StringComparison.OrdinalIgnoreCase)))
            {
                return string.Empty;
            }
        }

        return dept;
    }

    private static (DateTime From, DateTime To) ResolvePeriod(TrainingJournalParams parameters)
    {
        var from = parameters.TitleStarted?.Date ?? parameters.DateFrom.Date;
        var to = parameters.TitleEnded?.Date ?? parameters.DateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        return (from, to);
    }

    private static string FormatInstructor(TrainingJournalEntry entry) =>
        string.IsNullOrWhiteSpace(entry.InstructorQualification)
            ? entry.InstructorName
            : $"{entry.InstructorName}, {entry.InstructorQualification}";

    private static string ShortDate(DateTime date) => date.ToString("dd.MM.yyyy", Ru);

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}

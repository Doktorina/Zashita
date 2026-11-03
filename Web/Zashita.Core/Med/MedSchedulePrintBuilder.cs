using System.Net;
using System.Text;

namespace Zashita.Core.Med;

public static class MedSchedulePrintBuilder
{
    public static string Build(MedScheduleDocument document, string mode)
    {
        var normalized = MedSchedulePrintModes.Normalize(mode);
        return normalized switch
        {
            MedSchedulePrintModes.Acknowledgement => BuildAcknowledgement(document),
            MedSchedulePrintModes.PsychiatricDirection => BuildPsychiatricDirections(document),
            _ => BuildSchedule(document)
        };
    }

    private static string BuildSchedule(MedScheduleDocument document)
    {
        var sb = new StringBuilder();
        var org = string.IsNullOrWhiteSpace(document.Organization) ? "________________" : document.Organization.Trim();
        var year = document.Date.Year.ToString();
        var title = $"ГРАФИК периодического медицинского осмотра\nв {year} году по {org}";

        sb.Append("""
            <div class="med-print-head med-print-schedule-head">
            """);
        sb.Append(TextBlock(document, "doc-title", title, "med-print-schedule-title"));
        sb.Append("</div>");

        sb.Append("""
            <table class="ppe-doc-table med-print-table med-print-schedule-table">
              <thead>
                <tr>
                  <th>№ п/п</th>
                  <th>Дата проведения</th>
                  <th>ФИО</th>
                  <th>Дата рождения</th>
                  <th>Профессия (должность)</th>
                </tr>
              </thead>
              <tbody>
            """);

        var lines = PrepareLines(document);
        var n = 0;
        foreach (var group in lines.GroupBy(l =>
                     string.IsNullOrWhiteSpace(l.Department)
                         ? (string.IsNullOrWhiteSpace(document.Department) ? "—" : document.Department.Trim())
                         : l.Department.Trim(),
                 StringComparer.CurrentCultureIgnoreCase))
        {
            sb.Append($"""
                <tr class="med-print-group">
                  <td colspan="5"><b>{Web(group.Key)}</b></td>
                </tr>
                """);

            foreach (var line in group)
            {
                n++;
                var i = n - 1;
                var birth = line.BirthDate is { Year: > 1900 } d
                    ? d.ToString("dd.MM.yyyy")
                    : string.Empty;
                var examDate = string.IsNullOrWhiteSpace(line.EmployeeName)
                    ? string.Empty
                    : line.ExamDate.ToString("dd.MM.yyyy");

                sb.Append("<tr>");
                sb.Append(Cell(document, $"line{i}-n", n.ToString(), "med-c"));
                sb.Append(Cell(document, $"line{i}-date", examDate, "med-c"));
                sb.Append(Cell(document, $"line{i}-name", FormatName(line.EmployeeName)));
                sb.Append(Cell(document, $"line{i}-birth", birth, "med-c"));
                sb.Append(Cell(document, $"line{i}-prof", line.Profession));
                sb.Append("</tr>");
            }
        }

        if (n == 0)
        {
            for (var i = 0; i < 5; i++)
            {
                sb.Append("<tr>");
                sb.Append(Cell(document, $"line{i}-n", (i + 1).ToString(), "med-c"));
                sb.Append(Cell(document, $"line{i}-date", string.Empty, "med-c"));
                sb.Append(Cell(document, $"line{i}-name", string.Empty));
                sb.Append(Cell(document, $"line{i}-birth", string.Empty, "med-c"));
                sb.Append(Cell(document, $"line{i}-prof", string.Empty));
                sb.Append("</tr>");
            }
        }

        sb.Append("</tbody></table>");
        AppendFooter(sb, document);
        return sb.ToString();
    }

    private static string BuildAcknowledgement(MedScheduleDocument document)
    {
        var sb = new StringBuilder();
        var org = string.IsNullOrWhiteSpace(document.Organization) ? "________________" : document.Organization.Trim();
        var year = document.Date.Year.ToString();
        sb.Append("""
            <div class="med-print-head med-print-schedule-head">
            """);
        sb.Append(TextBlock(
            document,
            "doc-title",
            $"Лист ознакомления работников\nс графиком периодического медицинского осмотра\nв {year} году по {org}",
            "med-print-schedule-title"));
        sb.Append("</div>");

        sb.Append("""
            <table class="ppe-doc-table med-print-table med-print-schedule-table">
              <thead>
                <tr>
                  <th>№ п/п</th>
                  <th>ФИО</th>
                  <th>Профессия (должность)</th>
                  <th>Дата ознакомления</th>
                  <th>Подпись</th>
                </tr>
              </thead>
              <tbody>
            """);

        var lines = PrepareLines(document);
        for (var i = 0; i < Math.Max(lines.Count, 1); i++)
        {
            var line = i < lines.Count ? lines[i] : new MedScheduleLine();
            sb.Append("<tr>");
            sb.Append(Cell(document, $"ack{i}-n", (i + 1).ToString(), "med-c"));
            sb.Append(Cell(document, $"ack{i}-name", FormatName(line.EmployeeName)));
            sb.Append(Cell(document, $"ack{i}-prof", line.Profession));
            sb.Append(Cell(document, $"ack{i}-date", string.Empty, "med-c"));
            sb.Append(Cell(document, $"ack{i}-sign", string.Empty, "med-c"));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendFooter(sb, document);
        return sb.ToString();
    }

    private static string BuildPsychiatricDirections(MedScheduleDocument document)
    {
        var lines = PrepareLines(document);
        if (lines.Count == 0)
        {
            lines = [new MedScheduleLine()];
        }

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                sb.Append("""<div class="med-print-page-break"></div>""");
            }

            AppendPsychiatricDirectionSheet(sb, document, lines[i], i);
        }

        sb.Append("""
            <p class="med-print-pd-note ppe-no-print">
              Поля ассоциированные с персональными данными заполняются вручную: ФИО, дата рождения
            </p>
            """);
        return sb.ToString();
    }

    private static void AppendPsychiatricDirectionSheet(
        StringBuilder sb,
        MedScheduleDocument document,
        MedScheduleLine line,
        int index)
    {
        var org = string.IsNullOrWhiteSpace(document.Organization) ? "________________" : document.Organization.Trim();
        var ownership = string.IsNullOrWhiteSpace(document.EmployerOwnership)
            ? "РОССИЙСКАЯ СОБСТВЕННОСТЬ"
            : document.EmployerOwnership.Trim();
        var addressRaw = document.EmployerAddress?.Trim() ?? string.Empty;
        var address = string.IsNullOrWhiteSpace(addressRaw)
            ? "адрес: ________________"
            : (addressRaw.StartsWith("адрес", StringComparison.OrdinalIgnoreCase)
                ? addressRaw
                : $"адрес: {addressRaw}");
        var inn = document.EmployerInn?.Trim() ?? string.Empty;
        var okved = document.EmployerOkved?.Trim() ?? string.Empty;
        var innOkved = string.IsNullOrWhiteSpace(inn) && string.IsNullOrWhiteSpace(okved)
            ? "ИНН: ________________, ОКВЭД: ________"
            : $"ИНН: {(string.IsNullOrWhiteSpace(inn) ? "________________" : inn)}, ОКВЭД: {(string.IsNullOrWhiteSpace(okved) ? "________" : okved)}";
        var directionNo = $"{index + 1:00000000}";
        var longDate = FormatRussianLongDate(document.Date);
        var birth = line.BirthDate is { Year: > 1900 } d ? d.ToString("dd.MM.yyyy") : string.Empty;
        var gender = string.IsNullOrWhiteSpace(line.Gender) ? string.Empty : line.Gender.Trim();
        var workplace = string.IsNullOrWhiteSpace(line.Department)
            ? (document.Department?.Trim() ?? string.Empty)
            : line.Department.Trim();
        var mo = document.MedicalInstitution?.Trim() ?? string.Empty;

        sb.Append("""<div class="med-psych-dir-sheet">""");

        sb.Append("""<div class="med-psych-dir-org">""");
        sb.Append(TextBlock(document, $"psych{index}-org-name", org, "med-psych-dir-org-name"));
        sb.Append(TextBlock(document, $"psych{index}-org-own", ownership, "med-psych-dir-org-own"));
        sb.Append(TextBlock(document, $"psych{index}-org-address", address, "med-psych-dir-org-line"));
        sb.Append(TextBlock(document, $"psych{index}-org-inn", innOkved, "med-psych-dir-org-line"));
        sb.Append("""<div class="med-psych-dir-org-rule"></div>""");
        sb.Append("</div>");

        sb.Append(TextBlock(
            document,
            $"psych{index}-title",
            $"НАПРАВЛЕНИЕ N  {directionNo}\nна обязательное психиатрическое освидетельствование работника\nот  {longDate}",
            "med-psych-dir-title"));

        sb.Append("""<div class="med-psych-dir-fields">""");
        AppendPsychField(
            sb, document, $"psych{index}-to", "Направляется в", mo,
            "(наименование медицинской организации, адрес регистрации, код по ОГРН)");
        AppendPsychField(sb, document, $"psych{index}-fio", "Ф.И.О.", line.EmployeeName?.Trim() ?? string.Empty);
        AppendPsychField(sb, document, $"psych{index}-birth", "Дата рождения", birth);
        AppendPsychField(sb, document, $"psych{index}-gender", "Пол работника", gender);
        AppendPsychField(sb, document, $"psych{index}-workplace", "Место работы", workplace);
        AppendPsychField(sb, document, $"psych{index}-prof", "Занимаемая должность (профессия)", line.Profession?.Trim() ?? string.Empty);
        AppendPsychBlankField(
            sb, document, $"psych{index}-activity", line.ActivityKinds?.Trim() ?? string.Empty,
            "(вид (виды) деятельности, осуществляемый работником)");
        AppendPsychBlankField(
            sb, document, $"psych{index}-prior", line.PriorConclusions?.Trim() ?? string.Empty,
            "(сведения о заключениях, выданных по результатам обязательных предварительных и (или) периодических медицинских осмотров работников)");
        sb.Append("</div>");

        sb.Append(TextBlock(
            document,
            $"psych{index}-legal",
            "В соответствии со ст. 220 Трудового кодекса Российской Федерации работник направляется на обязательное психиатрическое освидетельствование с целью определения его пригодности по состоянию психического здоровья к осуществлению отдельных видов деятельности, утвержденных приказом Минздрава России 20 мая 2022 N 342н Об утверждении порядка прохождения обязательного психиатрического освидетельствования работниками, осуществляющими отдельные виды деятельности, его периодичности, а также видов деятельности, при осуществлении которых проводится психиатрическое освидетельствование",
            "med-psych-dir-legal"));

        sb.Append("</div>");
    }

    private static void AppendPsychField(
        StringBuilder sb,
        MedScheduleDocument document,
        string key,
        string label,
        string value,
        string? hint = null)
    {
        sb.Append("""<div class="med-psych-dir-row">""");
        sb.Append($"""<div class="med-psych-dir-label">{Web(label)}</div>""");
        sb.Append("""<div class="med-psych-dir-value-wrap">""");
        sb.Append(TextBlock(document, key, value, "med-psych-dir-value"));
        if (!string.IsNullOrWhiteSpace(hint))
        {
            sb.Append($"""<div class="med-psych-dir-hint">{Web(hint)}</div>""");
        }

        sb.Append("</div></div>");
    }

    private static void AppendPsychBlankField(
        StringBuilder sb,
        MedScheduleDocument document,
        string key,
        string value,
        string hint)
    {
        sb.Append("""<div class="med-psych-dir-blank">""");
        sb.Append(TextBlock(document, key, value, "med-psych-dir-value"));
        sb.Append($"""<div class="med-psych-dir-hint">{Web(hint)}</div>""");
        sb.Append("</div>");
    }

    private static string FormatRussianLongDate(DateTime date)
    {
        var months = new[]
        {
            "", "января", "февраля", "марта", "апреля", "мая", "июня",
            "июля", "августа", "сентября", "октября", "ноября", "декабря"
        };
        return $"«{date.Day}» {months[date.Month]} {date.Year} г.";
    }

    private static void AppendFooter(StringBuilder sb, MedScheduleDocument document)
    {
        sb.Append("""
            <div class="med-print-footer med-print-order-footer">
              <div class="med-print-signatures">
            """);
        sb.Append(TextBlock(document, "sign-role", "Специалист по охране труда", "med-print-signatures-title"));
        sb.Append("""
               <div class="med-print-signatures-row">
                 <div class="med-sign-cell">
            """);
        sb.Append(TextBlock(document, "sign-signature", string.Empty, "med-sign-line"));
        sb.Append("""
                   <div class="med-sign-label">(подпись)</div>
                 </div>
                 <div class="med-sign-cell">
            """);
        sb.Append(TextBlock(document, "sign-name", string.Empty, "med-sign-line"));
        sb.Append("""
                   <div class="med-sign-label">(Ф.И.О.)</div>
                 </div>
               </div>
             </div>
            </div>
            """);
    }

    private static List<MedScheduleLine> PrepareLines(MedScheduleDocument document)
    {
        return document.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.EmployeeName))
            .OrderBy(l => string.IsNullOrWhiteSpace(l.Department) ? document.Department : l.Department,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.ExamDate)
            .ThenBy(l => l.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string FormatName(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return text.EndsWith(',') ? text : text + ",";
    }

    private static string Cell(MedScheduleDocument document, string key, string value, string? cssClass = null)
    {
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = document.PrintOverrides.TryGetValue(key, out var overrideValue) && !string.IsNullOrWhiteSpace(overrideValue)
            ? overrideValue
            : value;
        var classAttr = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        return $"<td{classAttr} contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</td>";
    }

    private static string TextBlock(MedScheduleDocument document, string key, string value, string? cssClass = null)
    {
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = document.PrintOverrides.TryGetValue(key, out var overrideValue)
            ? overrideValue
            : value;
        var classAttr = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        var html = Web(text).Replace("\n", "<br/>", StringComparison.Ordinal);
        return $"<div{classAttr} contenteditable=\"true\" data-override-key=\"{Web(key)}\">{html}</div>";
    }

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

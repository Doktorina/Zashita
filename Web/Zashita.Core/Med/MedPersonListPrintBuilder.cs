using System.Net;
using System.Text;

namespace Zashita.Core.Med;

public static class MedPersonListPrintBuilder
{
    public static string Build(
        MedPersonListRecord document,
        string mode,
        IReadOnlyDictionary<string, MedFactorNode>? factorsByItem = null)
    {
        var normalized = MedPersonListPrintModes.Normalize(mode);
        return normalized switch
        {
            MedPersonListPrintModes.DirectionSverdlovsk => BuildDirection(document),
            _ => BuildPersonList(document, normalized, factorsByItem)
        };
    }

    private static string BuildPersonList(
        MedPersonListRecord document,
        string mode,
        IReadOnlyDictionary<string, MedFactorNode>? factorsByItem)
    {
        if (mode is MedPersonListPrintModes.Psychiatric)
        {
            return BuildPsychiatricList(document, factorsByItem);
        }

        var sb = new StringBuilder();
        AppendOrderHead(sb, document, mode);

        sb.Append("""
            <table class="ppe-doc-table med-print-table med-print-order-table">
              <thead>
                <tr>
                  <th>№ п/п</th>
                  <th>Цех, участок</th>
                  <th>Профессия</th>
                  <th>Номер рабочего места</th>
                  <th>Вредный производственный фактор (согласно Приказу МЗ РФ от 28.01.21 г. № 29н)</th>
                  <th>№ пункта по Приказу МЗ РФ № 29н</th>
                  <th>Периодичность (согласно Приказу МЗ РФ № 29н)</th>
                </tr>
              </thead>
              <tbody>
            """);

        var lines = PrepareLines(document, mode);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            sb.Append("<tr>");
            sb.Append(Cell(document, $"line{i}-n", (i + 1).ToString(), "med-c"));
            sb.Append(Cell(document, $"line{i}-dept", line.Workplace, "med-c"));
            sb.Append(Cell(document, $"line{i}-prof", line.Profession));
            sb.Append(Cell(document, $"line{i}-wpno", string.Empty, "med-c"));
            sb.Append(Cell(document, $"line{i}-factor", ResolveFactorsText(document, line, mode, factorsByItem)));
            sb.Append(Cell(document, $"line{i}-item", MedItemNumber.Display(line.ItemNumber), "med-c"));
            sb.Append(Cell(document, $"line{i}-period", MedPeriodicity.Normalize(line.Periodicity), "med-c"));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendOrderFooter(sb, document);
        return sb.ToString();
    }

    private static string BuildPsychiatricList(
        MedPersonListRecord document,
        IReadOnlyDictionary<string, MedFactorNode>? factorsByItem)
    {
        var sb = new StringBuilder();
        AppendOrderHead(sb, document, MedPersonListPrintModes.Psychiatric);

        sb.Append("""
            <table class="ppe-doc-table med-print-table med-print-psych-table">
              <thead>
                <tr>
                  <th>№ п/п</th>
                  <th>Цех, участок</th>
                  <th>Ф.И.О.</th>
                  <th>Год рождения</th>
                  <th>Профессия</th>
                  <th>Вид деятельности(Приказ Минздрава РФ от 20.05.22 N 342 н)</th>
                  <th>Дата прохождения психиатрического освидетельствования</th>
                </tr>
              </thead>
              <tbody>
            """);

        var lines = PrepareLines(document, MedPersonListPrintModes.Psychiatric);
        if (!string.IsNullOrWhiteSpace(document.Organization) || !string.IsNullOrWhiteSpace(document.Department))
        {
            var header = string.IsNullOrWhiteSpace(document.Department)
                ? document.Organization
                : $"{document.Organization}/{document.Department}";
            sb.Append($"<tr class=\"med-print-group\"><td colspan=\"7\"><b>{Web(header)}</b></td></tr>");
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var yearOfBirth = line.BirthDate?.Year > 1900 ? line.BirthDate?.ToString("dd.MM.yyyy") ?? string.Empty : string.Empty;
            sb.Append("<tr>");
            sb.Append(Cell(document, $"line{i}-n", (i + 1).ToString(), "med-c"));
            sb.Append(Cell(document, $"line{i}-dept", line.Workplace, "med-c"));
            sb.Append(Cell(document, $"line{i}-name", line.EmployeeName));
            sb.Append(Cell(document, $"line{i}-birth", yearOfBirth, "med-c"));
            sb.Append(Cell(document, $"line{i}-prof", line.Profession, "med-c"));
            sb.Append(Cell(document, $"line{i}-activity", ResolvePsychiatricActivity(document, line, factorsByItem)));
            sb.Append(Cell(document, $"line{i}-psychDate", string.Empty, "med-c"));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendOrderFooter(sb, document);
        return sb.ToString();
    }

    private static List<MedPersonListLine> PrepareLines(MedPersonListRecord document, string mode)
    {
        var lines = (document.Lines.Count > 0
                ? document.Lines
                : [new MedPersonListLine()])
            .Select(l => new MedPersonListLine
            {
                Id = l.Id,
                EmployeeName = l.EmployeeName,
                BirthDate = l.BirthDate,
                Profession = l.Profession,
                Workplace = l.Workplace,
                ItemNumber = MedItemNumber.Normalize(l.ItemNumber),
                Periodicity = MedPeriodicity.Normalize(l.Periodicity)
            })
            .ToList();

        if (mode is MedPersonListPrintModes.ByDepartment or MedPersonListPrintModes.WorkplacesByDepartment)
        {
            return lines
                .OrderBy(l => string.IsNullOrWhiteSpace(l.Workplace) ? "\uFFFF" : l.Workplace, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(l => l.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return lines
            .OrderBy(l => l.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ResolveFactorsText(
        MedPersonListRecord document,
        MedPersonListLine line,
        string mode,
        IReadOnlyDictionary<string, MedFactorNode>? factorsByItem)
    {
        if (mode is MedPersonListPrintModes.Psychiatric)
        {
            return string.IsNullOrWhiteSpace(document.Factors)
                ? "Психиатрическое освидетельствование"
                : document.Factors;
        }

        if (mode is MedPersonListPrintModes.Dispensary)
        {
            return string.IsNullOrWhiteSpace(document.Factors)
                ? "Диспансеризация"
                : document.Factors;
        }

        if (mode is MedPersonListPrintModes.FullFactors &&
            factorsByItem is not null &&
            !string.IsNullOrWhiteSpace(line.ItemNumber))
        {
            var key = MedItemNumber.Normalize(line.ItemNumber);
            if (factorsByItem.TryGetValue(key, out var factor) && !string.IsNullOrWhiteSpace(factor.Name))
            {
                var order = string.IsNullOrWhiteSpace(factor.OrderNumber) ? document.Factors : factor.OrderNumber;
                return string.IsNullOrWhiteSpace(order)
                    ? $"{MedItemNumber.Display(factor.ItemNumber)} {factor.Name}".Trim()
                    : $"{order}: {MedItemNumber.Display(factor.ItemNumber)} {factor.Name}".Trim();
            }
        }

        if (factorsByItem is not null && !string.IsNullOrWhiteSpace(line.ItemNumber))
        {
            var key = MedItemNumber.Normalize(line.ItemNumber);
            if (factorsByItem.TryGetValue(key, out var factor) && !string.IsNullOrWhiteSpace(factor.Name))
            {
                return factor.Name;
            }
        }

        return document.Factors;
    }

    private static string ResolvePsychiatricActivity(
        MedPersonListRecord document,
        MedPersonListLine line,
        IReadOnlyDictionary<string, MedFactorNode>? factorsByItem)
    {
        if (factorsByItem is not null && !string.IsNullOrWhiteSpace(line.ItemNumber))
        {
            var key = MedItemNumber.Normalize(line.ItemNumber);
            if (factorsByItem.TryGetValue(key, out var factor) && !string.IsNullOrWhiteSpace(factor.Name))
            {
                return factor.Name;
            }
        }

        return string.IsNullOrWhiteSpace(document.Factors) ? string.Empty : document.Factors;
    }

    private static string BuildDirection(MedPersonListRecord document)
    {
        var sb = new StringBuilder();
        AppendDirectionHead(sb, document);
        sb.Append("<div class=\"med-print-direction\">");
        sb.Append(CellBlock(document, "dir-org", document.Organization));
        sb.Append(CellBlock(document, "dir-dept", document.Department));
        sb.Append(CellBlock(document, "dir-type", document.ExamType));
        sb.Append(CellBlock(document, "dir-factors", document.Factors));
        sb.Append(CellBlock(document, "dir-persons", document.ContentSummary));
        sb.Append("</div>");
        AppendDirectionFooter(sb, document);
        return sb.ToString();
    }

    private static void AppendDirectionHead(StringBuilder sb, MedPersonListRecord document)
    {
        sb.Append("<div class=\"med-print-head\">");
        sb.Append($"<h2>{Web(MedPersonListPrintModes.Title(MedPersonListPrintModes.DirectionSverdlovsk))}</h2>");
        sb.Append($"<p>Документ: {Web(document.Number)} от {document.Date:dd.MM.yyyy HH:mm:ss}</p>");
        sb.Append($"<p>Организация: {Web(document.Organization)}</p>");
        sb.Append($"<p>Подразделение: {Web(document.Department)}</p>");
        sb.Append($"<p>Вид медосмотра: {Web(document.ExamType)}</p>");
        sb.Append("</div>");
    }

    private static void AppendDirectionFooter(StringBuilder sb, MedPersonListRecord document)
    {
        sb.Append("<div class=\"med-print-footer\">");
        sb.Append($"<p>Факторы медосмотра: {Web(document.Factors)}</p>");
        if (!string.IsNullOrWhiteSpace(document.Comment))
        {
            sb.Append($"<p>Комментарий: {Web(document.Comment)}</p>");
        }

        sb.Append("</div>");
    }

    private static void AppendOrderHead(StringBuilder sb, MedPersonListRecord document, string mode)
    {
        var org = string.IsNullOrWhiteSpace(document.Organization) ? "________________" : document.Organization;
        var year = document.Date.Year.ToString();
        var examType = document.ExamType?.Trim() ?? string.Empty;
        var title = mode switch
        {
            MedPersonListPrintModes.Psychiatric => "Список лиц, подлежащих психиатрическому освидетельствованию",
            MedPersonListPrintModes.Dispensary => "Список лиц, подлежащих диспансеризации",
            _ when examType.Contains("предвар", StringComparison.OrdinalIgnoreCase) =>
                "Список лиц, поступающих на работу\nподлежащих предварительному медицинскому осмотру",
            _ => "Список лиц\nподлежащих периодическому медицинскому осмотру"
        };

        sb.Append("""
            <div class="med-print-head med-print-order-head">
              <div class="med-print-approval">
            """);
        sb.Append(TextBlock(document, "approve-title", "УТВЕРЖДАЮ:"));
        sb.Append(TextBlock(document, "approve-role", "Руководитель организации"));
        sb.Append(TextBlock(document, "approve-name", string.Empty, "med-sign-line"));
        sb.Append(TextBlock(document, "approve-date", "«___» ______________ 20__ г."));
        sb.Append($"""
              </div>
              {TextBlock(document, "doc-title", title, "med-print-order-title")}
              {TextBlock(document, "doc-org", $"в {year} году по {org}.", "med-print-order-org")}
            </div>
            """);
    }

    private static void AppendOrderFooter(StringBuilder sb, MedPersonListRecord document)
    {
        sb.Append("<div class=\"med-print-footer med-print-order-footer\">");
        sb.Append(TextBlock(document, "compose-date", $"Дата составления: {document.Date:dd MMMM yyyy} г.", "med-compose-date"));
        sb.Append("""
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
            """);
        sb.Append("</div>");
    }

    private static string Cell(MedPersonListRecord document, string key, string value, string? cssClass = null)
    {
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = document.PrintOverrides.TryGetValue(key, out var overrideValue) && !string.IsNullOrWhiteSpace(overrideValue)
            ? overrideValue
            : value;
        var classAttr = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        return $"<td{classAttr} contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</td>";
    }

    private static string CellBlock(MedPersonListRecord document, string key, string value)
    {
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = document.PrintOverrides.TryGetValue(key, out var overrideValue) && !string.IsNullOrWhiteSpace(overrideValue)
            ? overrideValue
            : value;
        return $"<p contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</p>";
    }

    private static string TextBlock(MedPersonListRecord document, string key, string value, string? cssClass = null)
    {
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = document.PrintOverrides.TryGetValue(key, out var overrideValue)
            ? overrideValue
            : value;
        var classAttr = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        return $"<div{classAttr} contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</div>";
    }

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

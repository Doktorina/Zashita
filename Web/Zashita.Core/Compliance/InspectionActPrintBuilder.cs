using System.Net;
using System.Text;

namespace Zashita.Core.Compliance;

public static class InspectionActPrintBuilder
{
    public static string Build(InspectionActRecord record, bool autoPrint = true)
    {
        var sb = new StringBuilder();
        AppendDocHead(sb, autoPrint);
        sb.Append(BuildBody(record));
        AppendDocFoot(sb);
        return sb.ToString();
    }

    public static string BuildBody(InspectionActRecord record)
    {
        var sb = new StringBuilder();
        var org = Resolve(record, "org-name", record.Organization);
        var number = Resolve(record, "act-number", record.Number);
        var titleNumber = FormatTitleNumber(number);

        sb.Append("""
            <section class="ot-act-page">
              <div class="ot-act-header">
                <div class="ot-act-brand">
                  <div class="ot-act-logo" aria-hidden="true">ВИКТОРИЯ</div>
                  <div class="ot-act-org">
            """);
        sb.Append(Editable(record, "org-name", org));
        sb.Append("""
                  </div>
                  <div class="ot-act-dept">СЛУЖБА ОХРАНЫ ТРУДА</div>
                </div>
                <div class="ot-act-doc-title">
            """);
        sb.Append($"Акт проверки № {E(titleNumber)} соблюдения требований охраны труда, промышленной и пожарной безопасности");
        sb.Append("""
                </div>
              </div>

              <table class="ot-act-meta">
                <thead>
                  <tr>
                    <th>Акт проверки №</th>
                    <th>Дата</th>
                    <th>Объект строительства</th>
                    <th>Представитель</th>
                    <th>Составитель</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
            """);
        sb.Append($"<td>{EditableInline(record, "act-number", number)}</td>");
        sb.Append($"<td>{EditableInline(record, "act-date", record.Date.ToString("dd.MM.yyyy"))}</td>");
        sb.Append($"<td>{EditableInline(record, "construction-object", record.ConstructionObject)}</td>");
        sb.Append("<td>");
        sb.Append(EditableInline(record, "representative-title", record.RepresentativeTitle));
        sb.Append("<br />");
        sb.Append(EditableInline(record, "representative-name", record.RepresentativeName));
        sb.Append("</td><td>");
        sb.Append(EditableInline(record, "inspector-title", record.InspectorTitle));
        sb.Append("<br />");
        sb.Append(EditableInline(record, "inspector-name", record.InspectorName));
        sb.Append("""
                  </td>
                  </tr>
                </tbody>
              </table>

              <p class="ot-act-intro">
            """);
        sb.Append("На объекте строительства: ");
        sb.Append(EditableInline(record, "intro-object", record.ConstructionObject));
        sb.Append(", подрядчиком: ");
        sb.Append(EditableInline(record, "intro-contractor", record.Contractor));
        sb.Append(", специалистом службы охраны труда ");
        sb.Append(EditableInline(record, "intro-inspector", record.InspectorName));
        sb.Append(" проведена проверка соблюдения требований охраны труда, промышленной и пожарной безопасности и установлено:");
        sb.Append("""
              </p>

              <table class="ot-act-violations">
                <thead>
                  <tr>
                    <th class="ot-act-col-no">№</th>
                    <th>Наименование нарушений</th>
                    <th>Нарушенные НПА</th>
                    <th>Место нарушения</th>
                    <th>Срок устранения</th>
                  </tr>
                </thead>
                <tbody>
            """);

        var lines = record.Violations.Count > 0
            ? record.Violations
            : [new InspectionActViolationLine()];

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            sb.Append("<tr>");
            sb.Append($"<td class=\"ot-act-col-no\">{i + 1}</td>");
            sb.Append($"<td>{EditableBlock(record, $"violation-desc-{i}", line.Description)}</td>");
            sb.Append($"<td>{EditableBlock(record, $"violation-npa-{i}", line.Regulations)}</td>");
            sb.Append($"<td>{EditableBlock(record, $"violation-place-{i}", line.Location)}</td>");
            sb.Append($"<td>{EditableBlock(record, $"violation-deadline-{i}", FormatDeadline(line.Deadline))}</td>");
            sb.Append("</tr>");
        }

        sb.Append("""
                </tbody>
              </table>

              <div class="ot-act-footer">
                <p class="ot-act-attachment">
            """);
        sb.Append("Приложение: фотографии ");
        sb.Append(EditableInline(record, "photo-count", record.PhotoCount.ToString()));
        sb.Append(" шт.");
        sb.Append("""
                </p>
                <p class="ot-act-issued">
            """);
        sb.Append(EditableInline(record, "issued-line",
            $"{record.InspectorTitle} {org} — {record.InspectorName}"));
        sb.Append("""
                </p>
              </div>
            </section>

            <section class="ot-act-page ot-act-sign-page">
              <table class="ot-act-sign-table">
                <thead>
                  <tr>
                    <th>(должность)</th>
                    <th>(дата)</th>
                    <th>(подпись)</th>
                    <th>(инициалы, фамилия)</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>&nbsp;</td>
                    <td>&nbsp;</td>
                    <td>&nbsp;</td>
                    <td>&nbsp;</td>
                  </tr>
                </tbody>
              </table>
            </section>
            """);

        return sb.ToString();
    }

    private static string FormatTitleNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return "_____";
        }

        return number.Trim();
    }

    private static string FormatDeadline(DateTime? deadline) =>
        deadline?.ToString("dd.MM.yyyy, HH:mm") ?? string.Empty;

    private static string Resolve(InspectionActRecord record, string key, string fallback) =>
        record.PrintOverrides.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string Editable(InspectionActRecord record, string key, string value) =>
        $"<span contenteditable=\"true\" data-override-key=\"{E(key)}\">{Nl(value)}</span>";

    private static string EditableInline(InspectionActRecord record, string key, string value) =>
        $"<span contenteditable=\"true\" data-override-key=\"{E(key)}\">{E(value)}</span>";

    private static string EditableBlock(InspectionActRecord record, string key, string value) =>
        $"<div class=\"ot-act-multiline\" contenteditable=\"true\" data-override-key=\"{E(key)}\">{Nl(value)}</div>";

    private static string Nl(string value) =>
        E(value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal);

    private static void AppendDocHead(StringBuilder sb, bool autoPrint)
    {
        sb.Append("""
            <!DOCTYPE html><html lang="ru"><head><meta charset="utf-8"/>
            <title> </title>
            """);
        sb.Append(Zashita.Core.Brand.SiteIconHtml.HeadLinks);
        sb.Append("""
            <link rel="stylesheet" href="/css/ot-inspection-act-print.css" />
            """);

        if (!autoPrint)
        {
            sb.Append("""
              <style>
                .ot-act-preview-print-btn {
                  position: fixed; top: 14px; right: 14px; z-index: 1000;
                  padding: 8px 14px; border: 1px solid #888; background: #fff; cursor: pointer;
                }
              </style>
              """);
        }

        sb.Append("</head><body>");
        if (!autoPrint)
        {
            sb.Append("<button type=\"button\" class=\"ot-act-preview-print-btn\" onclick=\"window.print()\">Печать</button>");
        }
    }

    private static void AppendDocFoot(StringBuilder sb) => sb.Append("</body></html>");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

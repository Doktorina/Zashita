using System.Net;
using System.Text;

namespace Zashita.Core.Med;

/// <summary>Печатная форма приказа об отстранении от работы (ограничения физ. лиц).</summary>
public static class MedRestrictionPrintBuilder
{
    public static string Build(MedRestrictionDocument document)
    {
        var sb = new StringBuilder();
        var employee = (document.EmployeeName ?? string.Empty).Trim();
        var profession = string.IsNullOrWhiteSpace(document.Profession) ? "________________" : document.Profession.Trim();
        var fromDate = (document.SuspensionFrom ?? document.Date).ToString("dd.MM.yyyy");
        var city = string.IsNullOrWhiteSpace(document.City) ? "________________" : document.City.Trim();
        var orderNumber = string.IsNullOrWhiteSpace(document.Number) ? "___________" : document.Number.Trim();
        var orderDate = document.Date.ToString("dd.MM.yyyy");

        sb.Append("""
            <div class="med-print-head med-print-order-doc-head">
            """);
        sb.Append(TextBlock(document, "order-title", "П Р И К А З", "med-print-order-doc-title"));
        sb.Append("""
              <div class="med-print-order-meta">
            """);
        sb.Append(TextBlock(document, "order-date-line", "От «___» _________ 20___ г.", "med-print-order-meta-left"));
        sb.Append(TextBlock(document, "order-number-line", $"№ {orderNumber}", "med-print-order-meta-right"));
        sb.Append("</div>");
        sb.Append(TextBlock(document, "order-city", $"город {city}", "med-print-order-city"));
        sb.Append(TextBlock(document, "order-subject", "Об отстранении от работы", "med-print-order-subject"));
        sb.Append("</div>");

        sb.Append("""
            <div class="med-print-order-body">
            """);
        sb.Append(TextBlock(
            document,
            "order-preamble",
            "На основании ст. 76 ТК РФ в соответствии с медицинским заключением и при отсутствии в штатном расписании вакантных должностей",
            "med-print-order-preamble"));
        sb.Append(TextBlock(document, "order-command", "ПРИКАЗЫВАЮ:", "med-print-order-command"));

        var point1 =
            $"1. Отстранить работника {employee} от работы с {fromDate} по профессии {profession}.";
        sb.Append(TextBlock(document, "order-point1", point1, "med-print-order-point"));
        sb.Append(TextBlock(
            document,
            "order-point2",
            "2. Бухгалтерии в период отстранения от работы не начислять заработную плату.",
            "med-print-order-point"));

        var basisText = string.IsNullOrWhiteSpace(document.Basis)
            ? "Основание: ________________________________________________"
            : $"Основание: {document.Basis.Trim()}";
        sb.Append(TextBlock(document, "order-basis", basisText, "med-print-order-basis"));
        sb.Append("</div>");

        sb.Append("""
            <table class="ppe-doc-table med-print-table med-print-order-sign-table">
              <thead>
                <tr>
                  <th>Руководитель организации</th>
                  <th>Подпись</th>
                  <th>Дата</th>
                </tr>
              </thead>
              <tbody>
                <tr>
            """);
        sb.Append(Cell(document, "boss-role", string.Empty));
        sb.Append(Cell(document, "boss-sign", string.Empty, "med-c"));
        sb.Append(Cell(document, "boss-date", string.Empty, "med-c"));
        sb.Append("""
                </tr>
              </tbody>
            </table>
            """);

        sb.Append(TextBlock(
            document,
            "ack-title",
            $"С приказом № {orderNumber} от {orderDate} ознакомлены:",
            "med-print-order-ack-title"));

        sb.Append("""
            <table class="ppe-doc-table med-print-table med-print-order-sign-table">
              <thead>
                <tr>
                  <th>Ф.И.О.</th>
                  <th>Подпись</th>
                  <th>Дата</th>
                </tr>
              </thead>
              <tbody>
                <tr>
            """);
        sb.Append(Cell(document, "ack-name", employee));
        sb.Append(Cell(document, "ack-sign", string.Empty, "med-c"));
        sb.Append(Cell(document, "ack-date", string.Empty, "med-c"));
        sb.Append("""
                </tr>
              </tbody>
            </table>
            """);

        return sb.ToString();
    }

    private static string Cell(MedRestrictionDocument document, string key, string value, string? cssClass = null)
    {
        document.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = document.PrintOverrides.TryGetValue(key, out var overrideValue) && !string.IsNullOrWhiteSpace(overrideValue)
            ? overrideValue
            : value;
        var classAttr = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        return $"<td{classAttr} contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</td>";
    }

    private static string TextBlock(MedRestrictionDocument document, string key, string value, string? cssClass = null)
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

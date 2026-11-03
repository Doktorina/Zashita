using System.Globalization;
using System.Net;
using System.Text;

namespace Zashita.Core.Ppe;

/// <summary>Печатная форма «Нормы выдачи СИЗ» — как в 1С, с данными организации.</summary>
public static class PpeNormsRegisterPrintBuilder
{
    public static string BuildBody(
        string organization,
        IEnumerable<PpeReportBuilder.NormsRow> rows,
        IReadOnlyDictionary<string, string>? overrides = null,
        DateTime? documentDate = null)
    {
        var org = string.IsNullOrWhiteSpace(organization) ? "Организация" : organization.Trim();
        var rowList = rows.ToList();
        var ov = overrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var date = documentDate ?? DateTime.Today;
        var approveDate = O(
            ov,
            "approve-date",
            date.ToString("d MMMM yyyy", new CultureInfo("ru-RU")));

        var sb = new StringBuilder();
        sb.Append("<div class=\"ppe-norms-sheet\">");

        // Шапка как в 1С: правый блок утверждения
        sb.Append("<div class=\"ppe-norms-approve\">");
        sb.Append(Editable("approve-title", O(ov, "approve-title", "УТВЕРЖДАЮ"), "ppe-norms-approve-title"));
        sb.Append(Editable("approve-role", O(ov, "approve-role", "Руководитель организации")));
        sb.Append(Editable("approve-date", approveDate));
        sb.Append("</div>");

        sb.Append(Editable("doc-title", O(ov, "doc-title", "Нормы выдачи СИЗ"), "ppe-norms-sheet-title", "h1"));
        // Организация — мелким текстом над таблицей (наши данные), в 1С в печатном листе не дублируется как заголовок
        sb.Append(Editable("doc-org", O(ov, "doc-org", org), "ppe-norms-sheet-org", "div"));

        sb.Append("""
            <table class="ppe-doc-table ppe-norms-print-table">
              <thead>
                <tr>
                  <th class="c-num">N п/п</th>
                  <th>Подразделение</th>
                  <th>Наименование профессии (должности)</th>
                  <th>Тип СИЗ</th>
                  <th>Наименование СИЗ (с указанием конструктивных особенностей, маркировки, защитных свойств, классов защиты / уровней эффективности и количества)</th>
                  <th>Нормы выдачи на год (штуки, пары, комплекты, мл)</th>
                  <th>Основание выдачи СИЗ (пункты ЕТН / Приложения)</th>
                </tr>
              </thead>
              <tbody>
            """);

        if (rowList.Count == 0)
        {
            sb.Append("""<tr><td colspan="7" class="ppe-norms-empty">Нет данных для печати</td></tr>""");
        }
        else
        {
            for (var i = 0; i < rowList.Count; i++)
            {
                var row = rowList[i];
                var prefix = $"r{i}-";
                sb.Append("<tr>");
                sb.Append(Cell(ov, prefix + "num", row.Number.ToString(), "c-num"));
                sb.Append(Cell(ov, prefix + "dept", row.Department));
                sb.Append(Cell(ov, prefix + "prof", row.Profession));
                sb.Append(Cell(ov, prefix + "type", row.PpeType));
                sb.Append(Cell(ov, prefix + "name", row.PpeName));
                sb.Append(Cell(ov, prefix + "norm", row.NormText, "c-norm"));
                sb.Append(Cell(ov, prefix + "basis", row.LegalBasis));
                sb.Append("</tr>");
            }
        }

        sb.Append("</tbody></table>");

        // Подвал как в 1С: слева должность, справа две подписи
        sb.Append("<div class=\"ppe-norms-footer\">");
        sb.Append("<div class=\"ppe-norms-footer-left\">");
        sb.Append(Editable("resp-label", O(ov, "resp-label", "Ответственное лицо"), "ppe-norms-footer-label"));
        sb.Append(Editable("resp-role", O(ov, "resp-role", "Специалист по охране труда")));
        sb.Append("</div>");
        sb.Append("<div class=\"ppe-norms-footer-right\">");
        sb.Append("""
            <div class="ppe-norms-sign">
              <div class="ppe-norms-sign-line" contenteditable="true" data-override-key="sign-1">&nbsp;</div>
              <div class="ppe-norms-sign-hint">(подпись)</div>
            </div>
            <div class="ppe-norms-sign">
              <div class="ppe-norms-sign-line" contenteditable="true" data-override-key="sign-2">&nbsp;</div>
              <div class="ppe-norms-sign-hint">(фамилия, инициалы)</div>
            </div>
            """);
        sb.Append("</div></div></div>");
        return sb.ToString();
    }

    public static string BuildHtml(string organization, IEnumerable<PpeReportBuilder.NormsRow> rows) =>
        BuildBody(organization, rows);

    private static string Cell(
        IReadOnlyDictionary<string, string> overrides,
        string key,
        string value,
        string? cssClass = null)
    {
        var cls = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        return $"""<td{cls} contenteditable="true" data-override-key="{Encode(key)}">{Encode(O(overrides, key, value))}</td>""";
    }

    private static string Editable(string key, string value, string? cssClass = null, string tag = "div")
    {
        var cls = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        return $"<{tag}{cls} contenteditable=\"true\" data-override-key=\"{Encode(key)}\">{Encode(value)}</{tag}>";
    }

    private static string O(IReadOnlyDictionary<string, string> overrides, string key, string fallback) =>
        overrides.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

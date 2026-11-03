using System.Net;
using System.Text;

namespace Zashita.Core.Risks;

/// <summary>
/// Каноническая HTML-матрица 5×5 «тяжесть × вероятность» — как в Excel-шаблоне положения.
/// </summary>
public static class RiskAssessmentMatrixHtml
{
    public static bool LooksLikeMatrixTable(string? plainText)
    {
        var t = (plainText ?? string.Empty).ToLowerInvariant().Replace('ё', 'е');
        if (t.Length < 20)
        {
            return false;
        }

        var hasProb = t.Contains("вероятност", StringComparison.Ordinal);
        var hasSev = t.Contains("тяжест", StringComparison.Ordinal) || t.Contains("ущерб", StringComparison.Ordinal);
        var hasScale = t.Contains("вряд ли", StringComparison.Ordinal)
                       || t.Contains("маловероятн", StringComparison.Ordinal)
                       || t.Contains("микротравм", StringComparison.Ordinal)
                       || t.Contains("весовой", StringComparison.Ordinal);
        return hasProb && hasSev && hasScale;
    }

    public static string Build()
    {
        var sb = new StringBuilder();
        sb.Append("""<p class="risks-method-table-caption"><b>Таблица 1. Матрица оценки рисков</b></p>""");
        sb.Append("""<table class="risks-assessment-matrix" aria-label="Матрица оценки рисков">""");

        // Строка 1: угол + «Весовой коэффициент» + 5 вертикальных подписей вероятности
        sb.Append("<tr>");
        sb.Append(
            """
            <td class="ram-corner" rowspan="2">
              <div class="ram-corner-inner">
                <span class="ram-corner-prob">Вероятность возникновения</span>
                <span class="ram-corner-sev">Тяжесть ущерба</span>
              </div>
            </td>
            """);
        sb.Append("""<td class="ram-head ram-prob-label ram-weight-header"><div class="ram-vert">Весовой коэффициент</div></td>""");
        for (var p = 0; p < 5; p++)
        {
            sb.Append("""<td class="ram-head ram-prob-label"><div class="ram-vert">""");
            sb.Append(E(WorkplaceRiskScale.ProbabilityOptions[p]));
            sb.Append("</div></td>");
        }

        sb.Append("</tr>");

        // Строка 2: пустая ячейка под «Весовой коэффициент» + веса вероятности 1…5
        sb.Append("<tr>");
        sb.Append("""<td class="ram-weight-caption">Весовой коэффициент</td>""");
        for (var p = 1; p <= 5; p++)
        {
            sb.Append($"<td class=\"ram-weight\">{p}</td>");
        }

        sb.Append("</tr>");

        // Строки тяжести: текст | вес | 5 оценок
        for (var s = 1; s <= 5; s++)
        {
            sb.Append("<tr>");
            sb.Append("""<td class="ram-head ram-sev-label">""");
            sb.Append(E(WorkplaceRiskScale.SeverityOptions[s - 1]));
            sb.Append("</td>");
            sb.Append($"<td class=\"ram-sev-weight-cell\">{s}</td>");

            for (var p = 1; p <= 5; p++)
            {
                var score = s * p;
                var zone = WorkplaceRiskScale.ZoneCssClass(score);
                sb.Append($"<td class=\"ram-score {zone}\">{score}</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }

    /// <summary>Таблица 2. Классификация рисков по значимости (как в образце методики).</summary>
    public static string BuildClassification()
    {
        return """
            <p class="risks-method-table-caption"><b>Таблица 2. Классификация рисков по значимости</b></p>
            <table class="risks-method-doc-table risks-risk-class-table" aria-label="Классификация рисков по значимости">
              <thead>
                <tr>
                  <th style="width:22%">Уровень риска</th>
                  <th>Действия, связанные с данным уровнем риска</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td class="risk-zone-low"><b>1–4 (низкий)</b></td>
                  <td class="risks-risk-class-action">Маловероятны аварии, инциденты, травмирование, профзаболевания. Риск приемлем и не требует дополнительных мер управления. Необходимо поддерживать его на существующем уровне.</td>
                </tr>
                <tr>
                  <td class="risk-zone-mid"><b>5–10 (средний)</b></td>
                  <td class="risks-risk-class-action">Не исключена вероятность аварий, инцидентов, травмирования, профзаболеваний. Снизить риск в ходе текущей работы до возможного уровня. Учитывать при разработке целей и планирования мероприятий в перспективе.</td>
                </tr>
                <tr>
                  <td class="risk-zone-high"><b>11–25 (высокий)</b></td>
                  <td class="risks-risk-class-action">Высока вероятность аварий, инцидентов, травмирования, профзаболеваний. Риск неприемлем, требует приостановки деятельности для принятия мер для снижения риска.</td>
                </tr>
              </tbody>
            </table>
            """;
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

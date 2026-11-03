using System.Globalization;
using System.Net;
using System.Text;

namespace Zashita.Core.Risks;

/// <summary>Предпросмотр / печать годового плана по специальной оценке.</summary>
public static class SoutAnnualPlanPrintBuilder
{
    public static string Build(SoutAnnualPlanDocument document)
    {
        var sb = new StringBuilder();
        sb.Append($"""
            <div class="sout-plan-doc">
              <div class="sout-plan-title">ГОДОВОЙ ПЛАН<br/><span class="sout-plan-title-sub">по специальной оценке условий труда на {document.PlanYear} год</span></div>
              <div class="sout-plan-meta">Организация: <strong>{E(document.Organization)}</strong></div>
              <div class="sout-plan-meta">№ {E(string.IsNullOrWhiteSpace(document.Number) ? "—" : document.Number)}
                от {document.Date:dd.MM.yyyy}
                · Вид: {E(document.PlanType)}
                · Сценарий: {E(string.IsNullOrWhiteSpace(document.Scenario) ? "—" : document.Scenario)}
                · Статус: {E(document.IsPosted ? "Утверждён" : document.Status)}
              </div>
              <div class="sout-plan-meta">Период: {document.PeriodStart:dd.MM.yyyy} — {document.PeriodEnd:dd.MM.yyyy}
                {(string.IsNullOrWhiteSpace(document.Department) ? "" : " · Подразделение: " + E(document.Department))}
              </div>

              <h3 class="sout-plan-section">График</h3>
              <table class="sout-plan-table">
                <thead>
                  <tr>
                    <th>№</th>
                    <th>Вид оценки</th>
                    <th>Контрагент</th>
                  </tr>
                </thead>
                <tbody>
            """);

        var generals = document.GeneralRows.Count > 0
            ? document.GeneralRows
            : [new SoutAnnualPlanGeneralRow()];
        for (var i = 0; i < generals.Count; i++)
        {
            var row = generals[i];
            sb.Append($"""
                <tr>
                  <td class="c">{i + 1}</td>
                  <td>{E(row.AssessmentType)}</td>
                  <td>{E(row.Contractor)}</td>
                </tr>
                """);
        }

        sb.Append("""
                </tbody>
              </table>

              <h3 class="sout-plan-section">Расшифровка (по отчёту СОУТ / рабочим местам)</h3>
              <table class="sout-plan-table sout-plan-detail">
                <thead>
                  <tr>
                    <th>№</th>
                    <th>Подразделение</th>
                    <th>№ РМ</th>
                    <th>Сотрудник</th>
                    <th>Должность</th>
                    <th>Дата предыдущая</th>
                    <th>Дата следующая</th>
                    <th>Дата планирования</th>
                    <th>Контрагент</th>
                  </tr>
                </thead>
                <tbody>
            """);

        if (document.DetailRows.Count == 0)
        {
            sb.Append("""<tr><td colspan="9" class="hint">Нет строк расшифровки.</td></tr>""");
        }
        else
        {
            var n = 1;
            foreach (var row in document.DetailRows)
            {
                sb.Append($"""
                    <tr>
                      <td class="c">{n++}</td>
                      <td>{E(row.Department)}</td>
                      <td class="c">{E(row.WorkplaceNumber)}</td>
                      <td>{E(FirstNonEmpty(row.Employee, row.PersonName))}</td>
                      <td>{E(row.Profession)}</td>
                      <td class="c">{Fmt(row.PreviousDate)}</td>
                      <td class="c">{Fmt(row.NextDate)}</td>
                      <td class="c">{Fmt(row.PlannedDate)}</td>
                      <td>{E(row.Contractor)}</td>
                    </tr>
                    """);
            }
        }

        sb.Append("""
                </tbody>
              </table>
              <p class="sout-plan-hint ppe-no-print">Для правки строк откройте карточку плана. Здесь — предпросмотр печати.</p>
            </div>
            """);
        return sb.ToString();
    }

    private static string Fmt(DateTime? value) =>
        value?.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU")) ?? "—";

    private static string FirstNonEmpty(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : (b?.Trim() ?? string.Empty);

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

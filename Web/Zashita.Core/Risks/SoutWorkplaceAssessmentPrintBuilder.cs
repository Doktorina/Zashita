using System.Globalization;
using System.Net;
using System.Text;

namespace Zashita.Core.Risks;

/// <summary>Предпечать списка «Оценка рабочих мест по условиям труда».</summary>
public static class SoutWorkplaceAssessmentPrintBuilder
{
    public static string Build(string organization, IReadOnlyList<WorkplaceRecord> workplaces)
    {
        var leaves = workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .OrderBy(w => w.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.WorkplaceNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.Append($"""
            <div class="sout-assessment-doc">
              <div class="sout-assessment-title">ОЦЕНКА РАБОЧИХ МЕСТ ПО УСЛОВИЯМ ТРУДА</div>
              <div class="sout-assessment-meta">Организация: <strong>{E(organization)}</strong></div>
              <div class="sout-assessment-meta">Дата формирования: {DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"))}</div>
              <p class="sout-assessment-hint">Справочник «Рабочие места» — структура и номера РМ.
              Эта форма — класс условий труда, плановый год СОУТ и код по ОК (данные для отчёта по спецоценке).</p>
              <table class="sout-assessment-table">
                <thead>
                  <tr>
                    <th>№</th>
                    <th>Подразделение</th>
                    <th>Наименование / профессия</th>
                    <th>№ РМ</th>
                    <th>№ основного РМ</th>
                    <th>Класс условий труда</th>
                    <th>Кол-во работников</th>
                    <th>Плановый год СОУТ</th>
                    <th>Код по ОК</th>
                  </tr>
                </thead>
                <tbody>
            """);

        if (leaves.Count == 0)
        {
            sb.Append("""<tr><td colspan="9" class="c">Нет используемых рабочих мест.</td></tr>""");
        }
        else
        {
            var n = 1;
            foreach (var w in leaves)
            {
                var title = string.IsNullOrWhiteSpace(w.Profession) ? w.Name : $"{w.Name} ({w.Profession})";
                sb.Append($"""
                    <tr>
                      <td class="c">{n++}</td>
                      <td>{E(w.Department)}</td>
                      <td>{E(title)}</td>
                      <td class="c">{E(w.WorkplaceNumber)}</td>
                      <td class="c">{E(w.MainWorkplaceNumber)}</td>
                      <td class="c">{E(w.ConditionsClass)}</td>
                      <td class="c">{w.WorkerCount}</td>
                      <td class="c">{E(w.PlanYear)}</td>
                      <td class="c">{E(w.OkCode)}</td>
                    </tr>
                    """);
            }
        }

        sb.Append("""
                </tbody>
              </table>
            </div>
            """);
        return sb.ToString();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

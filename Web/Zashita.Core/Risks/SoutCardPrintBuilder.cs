using System.Globalization;
using System.Net;
using System.Text;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

public static class SoutCardPrintBuilder
{
    public static string Build(
        WorkplaceRecord workplace,
        string organization,
        IEnumerable<OrganizationEmployee>? employees = null)
    {
        var card = workplace.SoutCard ?? new SoutCardDetails();
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        var profession = string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession;
        var department = Dash(workplace.Department);
        var analogous = Dash(card.AnalogousWorkplaces);
        var preparedDate = ResolvePreparedDate(card, workplace) ?? DateTime.Today;
        var prepared = preparedDate.ToString("dd.MM.yyyy", ru);
        var number = string.IsNullOrWhiteSpace(workplace.SoutReportNumber) ? "____" : workplace.SoutReportNumber.Trim();
        var equipment = DisplayCatalog(card.Equipment);
        var materials = DisplayCatalog(card.Materials);
        var klass = string.IsNullOrWhiteSpace(workplace.ConditionsClass)
            ? Dash(card.FinalClass)
            : workplace.ConditionsClass.Trim();

        var selected = new HashSet<string>(card.EmployeeIds ?? [], StringComparer.OrdinalIgnoreCase);
        var snilsList = (employees ?? [])
            .Where(e => selected.Contains(e.Id))
            .Select(e => OneCEmployeeListParser.FormatSnils(e.Snils))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (snilsList.Count == 0 &&
            workplace.SoutCard?.ExtraLines is { } extra &&
            extra.TryGetValue("021", out var rawSnils) &&
            !string.IsNullOrWhiteSpace(rawSnils))
        {
            snilsList = rawSnils
                .Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToList();
        }

        var snilsHtml = snilsList.Count == 0
            ? "<div class=\"sout-card-snils-box\">—</div>"
            : string.Concat(snilsList.Select(s => $"<div class=\"sout-card-snils-box\">{E(s)}</div>"));

        var analogousCount = card.AnalogousWorkerCount > 0 ? card.AnalogousWorkerCount.ToString(ru) : "—";

        return $"""
            <div class="sout-official-preview">
              <div class="sout-official-title">КАРТА № {E(number)} специальной оценки условий труда</div>
              <div class="sout-official-line"><u>{E(profession)}</u></div>
              <div class="sout-official-caption">(наименование профессии (должности) работника)</div>
              <div class="sout-official-field">Наименование структурного подразделения: <u>{E(department)}</u></div>
              <div class="sout-official-field">Количество и номера аналогичных рабочих мест: <u>{E(analogous)}</u></div>

              <div class="sout-official-row"><b>Строка 011.</b> Код профессии по ОК (должности): <u>{E(Dash(workplace.OkCode))}</u></div>

              <div class="sout-official-row"><b>Строка 020.</b> Численность работающих:</div>
              <table class="sout-official-table">
                <tr>
                  <td>на рабочем месте</td>
                  <td class="c">{workplace.WorkerCount}</td>
                  <td>на всех аналогичных рабочих местах</td>
                  <td class="c">{E(analogousCount)}</td>
                </tr>
                <tr>
                  <td colspan="4">из них:</td>
                </tr>
                <tr>
                  <td>женщин</td>
                  <td class="c">{card.WorkersWomen}</td>
                  <td>лиц в возрасте до 18 лет</td>
                  <td class="c">{card.WorkersUnder18}</td>
                </tr>
                <tr>
                  <td colspan="3">инвалидов, допущенных к выполнению работ на данном рабочем месте</td>
                  <td class="c">{card.WorkersDisabled}</td>
                </tr>
              </table>

              <div class="sout-official-row"><b>Строка 021.</b> Закрепление работников:</div>
              {snilsHtml}

              <div class="sout-official-row"><b>Строка 022.</b></div>
              <div class="sout-official-field">Используемое производственное оборудование: <u>{E(equipment)}</u></div>
              <div class="sout-official-field">Используемые материалы и сырье: <u>{E(materials)}</u></div>

              <div class="sout-official-row"><b>Строка 030.</b> Оценка условий труда по вредным (опасным) факторам</div>
              <table class="sout-official-table">
                <tr>
                  <td>Итоговый класс (подкласс) условий труда</td>
                  <td class="c">{E(klass)}</td>
                  <td class="muted">не заполняется</td>
                  <td class="c">—</td>
                </tr>
              </table>

              <div class="sout-official-date">Дата составления: <u>{E(prepared)}</u></div>
              <div class="sout-official-org muted">Организация: {E(RiskPrintChrome.FormatOrganizationForCards(organization))}</div>
            </div>
            """;
    }

    /// <summary>Несколько карт СОУТ подряд — одна выгрузка / печать.</summary>
    public static string BuildMany(
        IEnumerable<WorkplaceRecord> workplaces,
        string organization,
        IEnumerable<OrganizationEmployee>? employees = null)
    {
        var rows = workplaces
            .Where(w => !w.IsGroup && !RiskPrintChrome.IsIndividualEntrepreneurWorkplace(w))
            .OrderBy(w => w.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.SoutReportNumber, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (rows.Count == 0)
        {
            return """<div class="sout-official-preview"><p>Нет карт СОУТ для выгрузки.</p></div>""";
        }

        var staff = (employees ?? []).ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                sb.Append("""<div class="sout-card-page-break"></div>""");
            }

            sb.Append("""<div class="sout-a4-page">""");
            sb.Append(Build(rows[i], organization, staff));
            sb.Append("</div>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Единый источник даты для печати: дата СОУТ / составления, иначе сегодня.
    /// </summary>
    public static DateTime? ResolvePreparedDate(SoutCardDetails card, WorkplaceRecord workplace)
    {
        if (workplace.SoutAssessmentDate is { } assess)
        {
            return assess.Date;
        }

        return card.PreparedDate?.Date;
    }

    /// <summary>
    /// Если даты ещё нет — ставим сегодняшний день (создание карты на печать).
    /// Синхронизирует SoutAssessmentDate и SoutCard.PreparedDate.
    /// </summary>
    public static bool EnsurePreparedDate(WorkplaceRecord workplace, DateTime? asOf = null)
    {
        if (workplace.IsGroup)
        {
            return false;
        }

        workplace.SoutCard ??= new SoutCardDetails();
        if (workplace.SoutAssessmentDate is not null || workplace.SoutCard.PreparedDate is not null)
        {
            return SyncAssessmentDates(workplace);
        }

        var today = (asOf ?? DateTime.Today).Date;
        workplace.SoutAssessmentDate = today;
        workplace.SoutCard.PreparedDate = today;
        return true;
    }

    /// <summary>
    /// Держит SoutAssessmentDate и SoutCard.PreparedDate одинаковыми.
    /// При расхождении побеждает дата из отчёта (SoutAssessmentDate).
    /// </summary>
    public static bool SyncAssessmentDates(WorkplaceRecord workplace)
    {
        if (workplace.IsGroup)
        {
            return false;
        }

        workplace.SoutCard ??= new SoutCardDetails();
        var changed = false;

        if (workplace.SoutAssessmentDate is { } assess)
        {
            if (workplace.SoutCard.PreparedDate?.Date != assess.Date)
            {
                workplace.SoutCard.PreparedDate = assess.Date;
                changed = true;
            }
        }
        else if (workplace.SoutCard.PreparedDate is { } prepared)
        {
            workplace.SoutAssessmentDate = prepared.Date;
            changed = true;
        }

        return changed;
    }

    private static string DisplayCatalog(string? value)
    {
        var text = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ';')
            .Trim();
        return string.IsNullOrWhiteSpace(text) ? "не используется" : text;
    }

    private static string Dash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Отсутствуют" : value.Trim();

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

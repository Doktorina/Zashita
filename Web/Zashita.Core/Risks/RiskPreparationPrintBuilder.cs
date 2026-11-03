using System.Globalization;
using System.Net;
using System.Text;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

public static class RiskPreparationPrintBuilder
{
        private static readonly string[] MonthNames =
        [
            "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
            "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
        ];

        private static readonly string[] MonthShort =
        [
            "Янв", "Фев", "Мар", "Апр", "Май", "Июн",
            "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"
        ];

    public static string Build(
        RiskPreparationDocument document,
        string mode,
        IReadOnlyList<WorkplaceRecord>? workplaces = null,
        IReadOnlyList<OrganizationEmployee>? employees = null,
        string? bankLetterhead = null)
    {
        var normalized = RiskPreparationPrintModes.Normalize(mode);
        return normalized switch
        {
            RiskPreparationPrintModes.ScheduleWeekly => BuildScheduleWeekly(document),
            RiskPreparationPrintModes.Order => BuildOrder(document, employees, bankLetterhead),
            RiskPreparationPrintModes.WorkplacesList => BuildWorkplacesList(document, workplaces ?? [], employees),
            _ => BuildScheduleMonthly(document, workplaces ?? [], employees)
        };
    }

    /// <summary>
    /// График 1:1 с формой 1С: УТВЕРЖДАЮ, организация, «Сроки проведения» × месяцы, председатель.
    /// </summary>
    private static string BuildScheduleMonthly(
        RiskPreparationDocument document,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<OrganizationEmployee>? employees)
    {
        var year = ResolveScheduleYear(document);
        var markMonth = (document.StartDate ?? document.Date).Month;
        ClearBlankMonthOverrides(document);
        var departments = ResolveDepartments(document, workplaces);

        var sb = new StringBuilder();
        sb.Append("""
            <div class="risks-schedule-sheet">
              <div class="risks-print-approve">
                <div class="risks-print-approve-box">
                  <p class="risks-print-approve-label">УТВЕРЖДАЮ:</p>
            """);
        // Пустая строка под подпись — как в форме 1С
        sb.Append(CellBlock(document, "approve-sign", "\u00a0", "risks-print-approve-sign"));
        sb.Append(CellBlock(document, "approve-date", "«___» __________ 20__ г.", "risks-print-approve-date"));
        sb.Append("""
                </div>
              </div>
            """);

        sb.Append(CellBlock(document, "sch-org", document.Organization, "risks-print-org-center"));
        sb.Append(CellBlock(document, "sch-title",
            $"График идентификации опасностей и оценки рисков в подразделениях на {year} г.",
            "risks-print-main-title"));

        // Шапка как в 1С: № | Подразделение | Сроки проведения → месяцы → номера 1…14
        sb.Append("""
            <table class="ppe-doc-table risks-print-table risks-print-months">
              <thead>
                <tr>
                  <th rowspan="2" class="risks-th-num">№ п/п</th>
                  <th rowspan="2" class="risks-th-dept">Наименование подразделения<br/>(заводской цех и т.п.)</th>
                  <th colspan="12" class="risks-th-terms">Сроки проведения</th>
                </tr>
                <tr class="risks-print-month-names">
            """);
        for (var mi = 0; mi < MonthShort.Length; mi++)
        {
            sb.Append($"<th title=\"{Web(MonthNames[mi])}\"><span class=\"risks-month-label\">{Web(MonthShort[mi])}</span></th>");
        }

        sb.Append("""
                </tr>
                <tr class="risks-print-col-nums">
            """);
        for (var col = 1; col <= 14; col++)
        {
            sb.Append($"<th>{col}</th>");
        }

        sb.Append("</tr></thead><tbody>");

        if (departments.Count == 0)
        {
            departments = [string.IsNullOrWhiteSpace(document.Department) ? "" : document.Department.Trim()];
        }

        var n = 1;
        foreach (var dept in departments)
        {
            sb.Append("<tr>");
            sb.Append(Cell(document, $"sch-{n}-num", n.ToString(CultureInfo.InvariantCulture), extraClass: "risks-td-num"));
            sb.Append(Cell(document, $"sch-dept-{n}", dept, extraClass: "risks-print-dept"));
            for (var m = 1; m <= 12; m++)
            {
                // «+» в месяце начала оценки по всем подразделениям (можно править в превью)
                var mark = m == markMonth ? "+" : "";
                sb.Append(Cell(document, $"sch-{n}-m{m}", mark, extraClass: "risks-print-month-cell", monthToggle: true));
            }

            sb.Append("</tr>");
            n++;
        }

        sb.Append("</tbody></table>");

        sb.Append("""
              <div class="risks-print-footer-sign">
                <div class="risks-print-footer-role">Председатель комиссии</div>
                <div class="risks-print-footer-line">__________________<div class="risks-print-footer-hint">(подпись)</div></div>
            """);
        var chairDefault = RiskPrintChrome.ResolveChairmanInitials(document.Organization, employees);
        sb.Append(CellBlock(document, "commission-chair", chairDefault, "risks-print-footer-name"));
        sb.Append("""
              </div>
              <p class="risks-print-edit-hint ppe-no-print">Правка: клик по тексту или ячейке. В месяцах клик ставит / убирает «+». Затем «Записать».</p>
            </div>
            """);
        return sb.ToString();
    }

    public static string BuildWorkplacesListSelected(
        RiskPreparationDocument document,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlySet<string> selectedIds,
        IReadOnlyList<OrganizationEmployee>? employees = null) =>
        BuildWorkplacesList(document, workplaces, employees, selectedIds);

    private static string BuildWorkplacesList(
        RiskPreparationDocument document,
        IReadOnlyList<WorkplaceRecord> workplaces,
        IReadOnlyList<OrganizationEmployee>? employees,
        IReadOnlySet<string>? selectedIds = null)
    {
        // Весь перечень по организации документа (подразделение документа не сужает список).
        var rows = workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => string.IsNullOrWhiteSpace(document.Organization)
                        || string.Equals(w.Organization, document.Organization, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcherSafe(document.Organization, w.Organization))
            .Where(w => selectedIds is null || selectedIds.Count == 0 || selectedIds.Contains(w.Id))
            .OrderBy(w => w.Organization, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.WorkplaceNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Profession, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("""
            <div class="risks-workplaces-sheet">
              <h1 class="risks-wp-title">Перечень рабочих мест, на которых проводится оценка уровней профессиональных рисков</h1>
            """);
        sb.Append(CellBlock(document, "list-org", document.Organization, "risks-wp-org-center"));

        sb.Append("""
            <table class="risks-wp-table risks-wp-table-2col">
              <thead>
                <tr>
                  <th>№ пп</th>
                  <th>Профессия, должность</th>
                </tr>
              </thead>
              <tbody>
            """);

        if (rows.Count == 0)
        {
            sb.Append("""
                <tr>
                  <td colspan="2" class="risks-wp-empty">Нет выбранных рабочих мест. Отметьте профессии галочками.</td>
                </tr>
                """);
        }
        else
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var w = rows[i];
                var title = !string.IsNullOrWhiteSpace(w.Profession) ? w.Profession : w.Name;
                var num = i + 1;
                sb.Append("<tr>");
                sb.Append(Cell(document, $"wp-{num}-n", num.ToString(CultureInfo.InvariantCulture), extraClass: "risks-wp-col-n"));
                sb.Append(Cell(document, $"wp-{num}-name", title));
                sb.Append("</tr>");
            }
        }

        sb.Append("</tbody></table>");
        var chair = RiskPrintChrome.ResolveChairmanInitials(document.Organization, employees);
        if (document.PrintOverrides.TryGetValue("list-chairman", out var chairOv) && !string.IsNullOrWhiteSpace(chairOv))
        {
            chair = RiskPrintChrome.IsUsablePersonChairman(chairOv, document.Organization)
                ? chairOv.Trim()
                : chair;
        }

        RiskPrintChrome.AppendChairmanFooter(sb, chair);
        sb.Append("""
              <p class="risks-print-edit-hint ppe-no-print">Правка: клик по тексту или ячейке таблицы. Затем «Записать».</p>
            </div>
            """);
        return sb.ToString();
    }

    /// <summary>
    /// Если в overrides все месяцы пустые (артефакт «Записать» без правок) — сбрасываем,
    /// чтобы снова показался «+» по дате начала.
    /// </summary>
    private static void ClearBlankMonthOverrides(RiskPreparationDocument document)
    {
        var monthKeys = document.PrintOverrides.Keys
            .Where(k => k.Contains("-m", StringComparison.Ordinal) &&
                        k.StartsWith("sch-", StringComparison.OrdinalIgnoreCase))
            .Where(k =>
            {
                var i = k.LastIndexOf("-m", StringComparison.OrdinalIgnoreCase);
                return i > 0 && k.Length > i + 2 && char.IsDigit(k[i + 2]);
            })
            .ToList();

        if (monthKeys.Count == 0)
        {
            return;
        }

        if (monthKeys.Any(k => string.Equals(document.PrintOverrides[k]?.Trim(), "+", StringComparison.Ordinal)))
        {
            return;
        }

        foreach (var key in monthKeys)
        {
            document.PrintOverrides.Remove(key);
        }
    }

    private static List<string> ResolveDepartments(
        RiskPreparationDocument document,
        IReadOnlyList<WorkplaceRecord> workplaces)
    {
        var fromOverrides = new List<string>();
        for (var n = 1; n <= 500; n++)
        {
            if (!document.PrintOverrides.TryGetValue($"sch-dept-{n}", out var dept)
                || string.IsNullOrWhiteSpace(dept))
            {
                if (n > 1)
                {
                    break;
                }

                continue;
            }

            fromOverrides.Add(dept.Trim());
        }

        if (fromOverrides.Count > 0)
        {
            return fromOverrides;
        }

        var fromWp = workplaces
            .Where(w => !w.IsGroup)
            .Where(w => string.IsNullOrWhiteSpace(document.Organization)
                        || string.Equals(w.Organization, document.Organization, StringComparison.OrdinalIgnoreCase)
                        || OrganizationMatcherSafe(document.Organization, w.Organization))
            .Select(w => w.Department?.Trim() ?? "")
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (fromWp.Count > 0)
        {
            return fromWp;
        }

        if (!string.IsNullOrWhiteSpace(document.Department))
        {
            return [document.Department.Trim()];
        }

        return [];
    }

    private static bool OrganizationMatcherSafe(string left, string right) =>
        OrganizationMatcher.Matches(left, right);

    private static int ResolveScheduleYear(RiskPreparationDocument document)
    {
        if (document.PrintOverrides.TryGetValue("schedule-year", out var yearText)
            && int.TryParse(yearText, out var year)
            && year is >= 2000 and <= 2100)
        {
            return year;
        }

        return (document.StartDate ?? document.Date).Year;
    }

    private static string BuildScheduleWeekly(RiskPreparationDocument document)
    {
        var sb = new StringBuilder();
        AppendHead(sb, document, RiskPreparationPrintModes.ScheduleWeekly);
        sb.Append("""
            <table class="ppe-doc-table risks-print-table">
              <thead>
                <tr>
                  <th>Неделя</th>
                  <th>Период</th>
                  <th>Подразделение</th>
                  <th>Мероприятие</th>
                  <th>Ответственный</th>
                </tr>
              </thead>
              <tbody>
            """);

        var start = document.StartDate ?? document.Date.Date;
        var end = document.EndDate ?? start.AddDays(28);
        var week = 1;
        for (var cursor = start; cursor <= end && week <= 8; cursor = cursor.AddDays(7), week++)
        {
            var weekEnd = cursor.AddDays(6);
            if (weekEnd > end)
            {
                weekEnd = end;
            }

            sb.Append("<tr>");
            sb.Append(Cell(document, $"w{week}-num", week.ToString(CultureInfo.InvariantCulture)));
            sb.Append(Cell(document, $"w{week}-period", $"{cursor:dd.MM.yyyy} — {weekEnd:dd.MM.yyyy}"));
            sb.Append(Cell(document, $"w{week}-dept", document.Department));
            sb.Append(Cell(document, $"w{week}-event", "Идентификация опасностей и оценка рисков"));
            sb.Append(Cell(document, $"w{week}-resp", ""));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string BuildOrder(
        RiskPreparationDocument document,
        IReadOnlyList<OrganizationEmployee>? employees,
        string? bankLetterhead = null)
    {
        var orderDate = document.Date.Date == default || document.Date.Year < 2000
            ? DateTime.Today
            : document.Date.Date;
        var day = orderDate.Day.ToString("00", CultureInfo.InvariantCulture);
        var monthGenitive = RiskPrintChrome.MonthGenitive(orderDate.Month);
        var year = orderDate.Year.ToString(CultureInfo.InvariantCulture);
        var numberShort = FormatOrderNumber(document.Number);
        var org = (document.Organization ?? string.Empty).Trim();
        var staff = employees ?? [];
        var chairman = RiskPrintChrome.ResolveCommissionChairman(org, staff);
        var signFio = RiskPrintChrome.ResolveDirectorSignInitials(org, staff);

        var letterheadOrg = RiskPrintChrome.FormatLetterheadOrganization(org);
        var bankDefault = RiskPrintChrome.ResolveBankLetterhead(
            document.PrintOverrides?.GetValueOrDefault("order-bank"),
            bankLetterhead);

        var sb = new StringBuilder();
        sb.Append("""<div class="risks-order-sheet">""");
        sb.Append("""<div class="risks-order-letterhead">""");
        sb.Append(CellBlock(document, "order-org", letterheadOrg, "risks-order-letterhead-name"));
        sb.Append("""<div class="risks-order-letterhead-rule risks-order-letterhead-rule-double"></div>""");
        sb.Append(CellBlock(document, "order-bank", bankDefault, "risks-order-letterhead-bank"));
        sb.Append("""<div class="risks-order-letterhead-rule risks-order-letterhead-rule-double"></div></div>""");

        sb.Append(CellBlock(document, "order-title",
            string.IsNullOrWhiteSpace(numberShort) || numberShort == "____"
                ? "ПРИКАЗ № ____"
                : $"ПРИКАЗ № {numberShort}",
            "risks-order-title"));
        sb.Append("""<div class="risks-order-meta risks-order-meta-2">""");
        sb.Append(CellBlock(document, "order-date",
            $"«{day}» {monthGenitive} {year} года",
            "risks-order-date risks-order-filled"));
        sb.Append(CellBlock(document, "order-city", "г. Екатеринбург", "risks-order-city risks-order-filled"));
        sb.Append("</div>");

        sb.Append(CellBlock(document, "order-subject",
            "О создании комиссии по проведению оценки профессиональных рисков",
            "risks-order-subject"));
        sb.Append(CellBlock(document, "order-preamble",
            "В целях обеспечения проведения оценки профессиональных рисков",
            "risks-order-preamble"));
        sb.Append("""<p class="risks-order-command">ПРИКАЗЫВАЮ:</p><ol class="risks-order-points">""");
        sb.Append("<li><span>Создать комиссию по проведению оценки профессиональных рисков в следующем составе:</span>");
        sb.Append("<ul class=\"risks-order-sublist\"><li>Председатель комиссии: — ");
        sb.Append(InlineEditable(document, "order-chairman",
            string.IsNullOrWhiteSpace(chairman) ? "______________________________" : chairman));
        sb.Append(";</li></ul></li>");
        var deadline = orderDate.AddMonths(2);
        sb.Append(OrderPoint(document, "order-point-2",
            $"Председателю комиссии провести оценку профессиональных рисков в организации в соответствии с требованиями, установленными в положении о СУОТ, в срок — до {deadline:dd.MM.yyyy}."));
        sb.Append(OrderPoint(document, "order-point-3",
            "Контроль за выполнением приказа оставляю за собой."));
        sb.Append("</ol>");
        sb.Append("""<div class="risks-order-sign-row2">""");
        sb.Append(CellBlock(document, "order-sign-post",
            RiskPrintChrome.IsIndividualEntrepreneur(org) ? "Индивидуальный предприниматель" : "Директор",
            "risks-order-sign-left"));
        sb.Append(CellBlock(document, "order-sign-fio",
            string.IsNullOrWhiteSpace(signFio) ? "__________________" : signFio,
            "risks-order-sign-right"));
        sb.Append("</div>");
        sb.Append("""
              <p class="risks-print-edit-hint ppe-no-print">Правка: клик по тексту. Затем «Записать».</p>
            </div>
            """);
        return sb.ToString();
    }

    private static string MonthGenitive(int month) => month switch
    {
        1 => "января",
        2 => "февраля",
        3 => "марта",
        4 => "апреля",
        5 => "мая",
        6 => "июня",
        7 => "июля",
        8 => "августа",
        9 => "сентября",
        10 => "октября",
        11 => "ноября",
        12 => "декабря",
        _ => ""
    };

    private static string FormatOrderNumber(string number)
    {
        var raw = (number ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return "____";
        }

        // ОТ-00000007 → ОТ-7
        var dash = raw.LastIndexOf('-');
        if (dash > 0 && dash < raw.Length - 1)
        {
            var prefix = raw[..dash];
            var tail = raw[(dash + 1)..];
            if (long.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                return $"{prefix}-{n}";
            }
        }

        return raw;
    }

    private static string InlineEditable(RiskPreparationDocument document, string key, string value)
    {
        var text = ResolveOrderField(document, key, value);
        return $"<span contenteditable=\"true\" data-override-key=\"{Web(key)}\" class=\"risks-order-inline\">{Web(text)}</span>";
    }

    private static string OrderPoint(RiskPreparationDocument document, string key, string value)
    {
        var text = ResolveOrderField(document, key, value);
        return $"<li contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</li>";
    }

    private static void AppendHead(StringBuilder sb, RiskPreparationDocument document, string mode)
    {
        sb.Append("<div class=\"risks-print-head\">");
        sb.Append($"<h2>{Web(RiskPreparationPrintModes.Title(mode))}</h2>");
        sb.Append($"<p>Организация: {Web(document.Organization)}</p>");
        sb.Append($"<p>Документ: {Web(document.Number)} от {document.Date:dd.MM.yyyy HH:mm:ss}</p>");
        sb.Append("</div>");
    }

    private static string Cell(
        RiskPreparationDocument document,
        string key,
        string value,
        string? extraClass = null,
        bool monthToggle = false)
    {
        var text = document.PrintOverrides.TryGetValue(key, out var overrideValue)
            ? overrideValue
            : value;
        var cls = string.IsNullOrWhiteSpace(extraClass) ? "" : $" class=\"{extraClass}\"";
        var toggle = monthToggle ? " data-month-toggle=\"1\"" : "";
        return $"<td contenteditable=\"true\" data-override-key=\"{Web(key)}\"{cls}{toggle}>{Web(text)}</td>";
    }

    private static string CellBlock(
        RiskPreparationDocument document,
        string key,
        string value,
        string? extraClass = null)
    {
        var text = ResolveOrderField(document, key, value);
        var cls = string.IsNullOrWhiteSpace(extraClass) ? "" : $" class=\"{extraClass}\"";
        return $"<div contenteditable=\"true\" data-override-key=\"{Web(key)}\"{cls}>{Web(text).Replace("\n", "<br/>")}</div>";
    }

    private static string ResolveOrderField(RiskPreparationDocument document, string key, string fallback)
    {
        if (document.PrintOverrides is not null
            && document.PrintOverrides.TryGetValue(key, out var ov)
            && !string.IsNullOrWhiteSpace(ov))
        {
            var text = ov.Trim();
            var org = (document.Organization ?? string.Empty).Trim();
            if (key is "order-chairman" or "order-sign-fio")
            {
                if (!RiskPrintChrome.IsUsablePersonChairman(text, org))
                {
                    return fallback;
                }
            }

            if (key == "order-bank" && !RiskPrintChrome.IsFilledBankLetterhead(text))
            {
                return fallback;
            }

            return text;
        }

        return fallback;
    }

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

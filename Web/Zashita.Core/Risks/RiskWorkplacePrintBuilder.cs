using System.Net;
using System.Text;

namespace Zashita.Core.Risks;

public static class RiskWorkplacePrintBuilder
{
    public static string Build(WorkplaceRecord workplace, string organization, string mode)
    {
        var normalized = RiskWorkplacePrintModes.Normalize(mode);
        return normalized switch
        {
            RiskWorkplacePrintModes.IdentificationMeasures => BuildIdentification(workplace, organization, withMeasures: true),
            RiskWorkplacePrintModes.Assessment => BuildAssessment(workplace, organization),
            RiskWorkplacePrintModes.PpeDuty => BuildPpeDuty(workplace, organization),
            RiskWorkplacePrintModes.Chronometry => BuildChronometry(workplace, organization),
            _ => BuildIdentification(workplace, organization, withMeasures: false)
        };
    }

    private static string BuildIdentification(WorkplaceRecord wp, string organization, bool withMeasures)
    {
        var sb = new StringBuilder();
        AppendHead(sb, wp, organization, RiskWorkplacePrintModes.Title(
            withMeasures ? RiskWorkplacePrintModes.IdentificationMeasures : RiskWorkplacePrintModes.Identification));

        sb.Append("""
            <table class="ppe-doc-table risks-print-table">
              <thead>
                <tr>
                  <th>№</th>
                  <th>Опасность / вредный фактор</th>
                  <th>Источник</th>
                  <th>Последствия</th>
            """);

        if (withMeasures)
        {
            sb.Append("<th>Меры по управлению рисками</th><th>Ответственный</th>");
        }

        sb.Append("</tr></thead><tbody>");
        var lines = wp.RiskLines ?? [];
        var rowCount = Math.Max(5, lines.Count);
        for (var i = 0; i < rowCount; i++)
        {
            var line = i < lines.Count ? lines[i] : null;
            var n = i + 1;
            sb.Append("<tr>");
            sb.Append($"<td>{(line is null || string.IsNullOrWhiteSpace(line.HazardNumber) ? n.ToString() : WebUtility.HtmlEncode(line.HazardNumber))}</td>");
            sb.Append(CellOrOverride(wp, $"id{n}-hazard", line?.HazardTitle));
            sb.Append(CellOrOverride(wp, $"id{n}-source", line?.Source));
            sb.Append(CellOrOverride(wp, $"id{n}-effect", line?.Consequences));
            if (withMeasures)
            {
                sb.Append(CellOrOverride(wp, $"id{n}-measure", line?.ControlMeasures));
                sb.Append(CellOrOverride(wp, $"id{n}-resp", line?.Responsible));
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendWorkplaceFooter(sb, wp);
        return sb.ToString();
    }

    private static string BuildAssessment(WorkplaceRecord wp, string organization)
    {
        var sb = new StringBuilder();
        AppendCardHead(sb, wp, organization);
        sb.Append("""
            <table class="ppe-doc-table risks-print-table risks-card-table">
              <colgroup>
                <col style="width:6%" />
                <col style="width:16%" />
                <col style="width:12%" />
                <col style="width:10%" />
                <col style="width:8%" />
                <col style="width:8%" />
                <col style="width:32%" />
                <col style="width:8%" />
              </colgroup>
              <thead>
                <tr>
                  <th>Номер опасности по перечню</th>
                  <th>Наименование опасности (опасного действия, ситуации)</th>
                  <th>Наименование оборудования — потенциального источника причинения травмы</th>
                  <th>Тяжесть ущерба</th>
                  <th>Вероятность возникновения</th>
                  <th>Значение показателя риска опасности</th>
                  <th>Меры управления</th>
                  <th>Остаточный риск</th>
                </tr>
              </thead>
              <tbody>
            """);

        var lines = wp.RiskLines ?? [];
        var rowCount = Math.Max(1, lines.Count);
        for (var i = 0; i < rowCount; i++)
        {
            var line = i < lines.Count ? lines[i] : null;
            var n = i + 1;
            var score = line is null
                ? null
                : WorkplaceRiskScale.ComputeScore(line.Severity, line.Probability)?.ToString()
                  ?? line.InitialRisk;
            sb.Append("<tr>");
            sb.Append($"<td>{(line is null || string.IsNullOrWhiteSpace(line.HazardNumber) ? n.ToString() : WebUtility.HtmlEncode(line.HazardNumber))}</td>");
            sb.Append(CellOrOverride(wp, $"as{n}-hazard", line?.HazardTitle));
            sb.Append(CellOrOverride(wp, $"as{n}-source", line?.Source));
            sb.Append(CellOrOverride(wp, $"as{n}-severity", line?.Severity));
            sb.Append(CellOrOverride(wp, $"as{n}-prob", line?.Probability));
            sb.Append(CellOrOverride(wp, $"as{n}-level", score));
            sb.Append(CellOrOverride(wp, $"as{n}-action", line?.ControlMeasures, multiline: true));
            sb.Append(CellOrOverride(wp, $"as{n}-residual", line?.ResidualRisk));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendCardFooter(sb, wp, organization);
        return sb.ToString();
    }

    private static string BuildPpeDuty(WorkplaceRecord wp, string organization)
    {
        var sb = new StringBuilder();
        AppendHead(sb, wp, organization, RiskWorkplacePrintModes.Title(RiskWorkplacePrintModes.PpeDuty));
        sb.Append("""
            <table class="ppe-doc-table risks-print-table">
              <thead>
                <tr>
                  <th>№</th>
                  <th>Наименование СИЗ</th>
                  <th>Количество</th>
                  <th>Дата выдачи</th>
                  <th>Подпись получившего</th>
                </tr>
              </thead>
              <tbody>
            """);

        for (var i = 1; i <= 4; i++)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{i}</td>");
            sb.Append(EmptyCell(wp, $"ppe{i}-name"));
            sb.Append(EmptyCell(wp, $"ppe{i}-qty"));
            sb.Append(EmptyCell(wp, $"ppe{i}-date"));
            sb.Append(EmptyCell(wp, $"ppe{i}-sign"));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendWorkplaceFooter(sb, wp);
        return sb.ToString();
    }

    private static string BuildChronometry(WorkplaceRecord wp, string organization)
    {
        var sb = new StringBuilder();
        AppendHead(sb, wp, organization, RiskWorkplacePrintModes.Title(RiskWorkplacePrintModes.Chronometry));
        sb.Append("""
            <table class="ppe-doc-table risks-print-table">
              <thead>
                <tr>
                  <th>№</th>
                  <th>Операция / действие</th>
                  <th>Время, мин</th>
                  <th>Примечание</th>
                </tr>
              </thead>
              <tbody>
            """);

        for (var i = 1; i <= 8; i++)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{i}</td>");
            sb.Append(EmptyCell(wp, $"ch{i}-op"));
            sb.Append(EmptyCell(wp, $"ch{i}-min"));
            sb.Append(EmptyCell(wp, $"ch{i}-note"));
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        AppendWorkplaceFooter(sb, wp);
        return sb.ToString();
    }

    private static void AppendCardHead(StringBuilder sb, WorkplaceRecord wp, string organization)
    {
        var org = RiskPrintChrome.FormatOrganizationForCards(
            string.IsNullOrWhiteSpace(organization) ? wp.Organization : organization);
        var profession = !string.IsNullOrWhiteSpace(wp.Profession) ? wp.Profession : wp.Name;
        var cardNo = string.IsNullOrWhiteSpace(wp.RiskCardNumber) ? "____" : wp.RiskCardNumber.Trim();
        var assessDate = wp.RiskAssessmentDate is { } d ? d.ToString("dd.MM.yyyy") : string.Empty;

        sb.Append("""<div class="risks-card-sheet">""");
        sb.Append($"<h1 class=\"risks-card-doc-title\">Карта идентификации опасностей и оценки рисков № {Web(cardNo)}</h1>");
        sb.Append("""<table class="risks-card-meta-table"><tbody>""");
        sb.Append($"<tr><td class=\"risks-card-meta-label\">Организация</td><td contenteditable=\"true\" data-override-key=\"org-name\">{Web(org)}</td>");
        sb.Append($"<td class=\"risks-card-meta-label\">Подразделение</td><td contenteditable=\"true\" data-override-key=\"dept\">{Web(wp.Department)}</td></tr>");
        sb.Append($"<tr><td class=\"risks-card-meta-label\">Наименование должности (профессии)</td><td contenteditable=\"true\" data-override-key=\"profession\">{Web(profession)}</td>");
        sb.Append($"<td class=\"risks-card-meta-label\">Дата проведения идентификации опасностей и оценки рисков</td><td contenteditable=\"true\" data-override-key=\"assess-date\">{Web(string.IsNullOrWhiteSpace(assessDate) ? "\u00a0" : assessDate)}</td></tr>");
        sb.Append($"<tr><td class=\"risks-card-meta-label\">Общий риск</td><td colspan=\"3\" contenteditable=\"true\" data-override-key=\"overall-risk\">{Web(string.IsNullOrWhiteSpace(wp.OverallRisk) ? "\u00a0" : wp.OverallRisk)}</td></tr>");
        sb.Append("</tbody></table>");
    }

    private static void AppendCardFooter(StringBuilder sb, WorkplaceRecord wp, string organization)
    {
        var chair = RiskPrintChrome.ResolveDirectorSignInitials(organization, []);
        if (string.IsNullOrWhiteSpace(chair) || RiskPrintChrome.LooksLikeOrganizationName(chair))
        {
            chair = "\u00a0";
        }

        sb.Append("""<div class="risks-card-compiled">""");
        sb.Append("""<span>Карту составил: руководитель рабочей группы</span>""");
        sb.Append("""<span class="risks-card-compiled-sign"><span class="risks-chair-line"></span><span class="risks-chair-hint">(подпись)</span></span>""");
        sb.Append($"<span class=\"risks-card-compiled-name\" contenteditable=\"true\" data-override-key=\"compiled-by\">{Web(chair)}<span class=\"risks-chair-hint\">(Фамилия И.О.)</span></span>");
        sb.Append("</div>");
        sb.Append($"<div class=\"risks-card-page-foot\"><span>Карта № {Web(string.IsNullOrWhiteSpace(wp.RiskCardNumber) ? "____" : wp.RiskCardNumber)}</span></div>");
        sb.Append("</div>");

        // Лист ознакомления — отдельная страница; в каждой строке текст + подпись + ФИО.
        var acqCardNo = string.IsNullOrWhiteSpace(wp.RiskCardNumber) ? "____" : wp.RiskCardNumber.Trim();
        sb.Append("""<div class="risks-page-break" data-break="acq"></div>""");
        sb.Append("""<div class="risks-card-sheet risks-card-acq-sheet">""");
        // Пустая строка сверху — «спустить» блок росписей на одну строку ниже.
        sb.Append("""<div class="risks-card-acq-spacer" aria-hidden="true">&nbsp;</div>""");
        sb.Append("""<table class="risks-card-acq-table"><tbody>""");
        for (var i = 0; i < 12; i++)
        {
            sb.Append("""
                <tr class="risks-card-acq-row">
                  <td class="risks-card-acq-label">Ознакомлены: работники указанной в карте должности (профессии)</td>
                  <td class="risks-card-acq-field">
                    <div class="risks-card-acq-underscore"></div>
                    <div class="risks-chair-hint">(подпись)</div>
                  </td>
                  <td class="risks-card-acq-field risks-card-acq-field-name">
                    <div class="risks-card-acq-underscore"></div>
                    <div class="risks-chair-hint">(Фамилия И.О.)</div>
                  </td>
                </tr>
                """);
        }

        sb.Append("</tbody></table>");
        // Маленькая подпись, к какой карте относится лист ознакомления (печать + Word).
        sb.Append($"""<div class="risks-card-page-foot risks-card-acq-foot"><span>Карта № {Web(acqCardNo)}</span></div>""");
        sb.Append("</div>");
    }

    private static void AppendHead(StringBuilder sb, WorkplaceRecord wp, string organization, string title)
    {
        var org = RiskPrintChrome.FormatOrganizationForCards(
            string.IsNullOrWhiteSpace(organization) ? wp.Organization : organization);
        sb.Append("""<div class="risks-card-sheet">""");
        sb.Append($"<h2 class=\"risks-print-head-title\">{Web(title)}</h2>");
        sb.Append($"<p contenteditable=\"true\" data-override-key=\"org-name\">Организация: {Web(org)}</p>");
        if (!string.IsNullOrWhiteSpace(wp.RiskCardNumber))
        {
            sb.Append($"<p>Номер карты: {Web(wp.RiskCardNumber)}</p>");
        }

        sb.Append($"<p>Рабочее место: {Web(wp.Name)} (№ {Web(wp.WorkplaceNumber)})</p>");
        sb.Append($"<p>Профессия: {Web(wp.Profession)}</p>");
        sb.Append($"<p>Подразделение: {Web(wp.Department)}</p>");
        if (wp.RiskAssessmentDate is { } d)
        {
            sb.Append($"<p>Дата оценки рисков: {d:dd.MM.yyyy}</p>");
        }
    }

    private static void AppendWorkplaceFooter(StringBuilder sb, WorkplaceRecord wp)
    {
        sb.Append("<div class=\"risks-print-footer\">");
        sb.Append($"<p>Количество работников: {wp.WorkerCount}</p>");
        sb.Append($"<p>Класс условий труда: {Web(wp.ConditionsClass)}</p>");
        sb.Append("</div></div>");
    }

    private static string EmptyCell(WorkplaceRecord wp, string key)
    {
        wp.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = wp.PrintOverrides.TryGetValue(key, out var overrideValue) ? overrideValue : string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"<td contenteditable=\"true\" data-override-key=\"{Web(key)}\">&nbsp;</td>";
        }

        return $"<td contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</td>";
    }

    private static string CellOrOverride(WorkplaceRecord wp, string key, string? fromLine, bool multiline = false)
    {
        wp.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = wp.PrintOverrides.TryGetValue(key, out var overrideValue) && !string.IsNullOrWhiteSpace(overrideValue)
            ? overrideValue
            : fromLine ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"<td contenteditable=\"true\" data-override-key=\"{Web(key)}\">&nbsp;</td>";
        }

        var html = Web(text);
        if (multiline)
        {
            html = html.Replace("\r\n", "<br/>", StringComparison.Ordinal)
                .Replace("\n", "<br/>", StringComparison.Ordinal);
        }

        return $"<td contenteditable=\"true\" data-override-key=\"{Web(key)}\">{html}</td>";
    }

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

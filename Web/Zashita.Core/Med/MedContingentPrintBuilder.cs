using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Zashita.Core.Employees;

namespace Zashita.Core.Med;

/// <summary>Печать «Список контингентов» / «Поименный список ЛПУ» по бланку Excel.</summary>
public static class MedContingentPrintBuilder
{
    public static string BuildContingentsHtml(
        string organization,
        IReadOnlyList<MedContingentListRow> rows,
        MedContingentListRow totals,
        IReadOnlyList<OrganizationEmployee>? employees = null,
        MedContingentPrintSettings? printSettings = null,
        OrganizationRequisites? requisites = null)
    {
        var overrides = printSettings?.Overrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var year = DateTime.Today.Year;
        var active = (employees ?? [])
            .Where(e => e.IsActive && !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
        var workersTotal = active.Count;
        var workersWomen = active.Count(e => IsFemale(e.Name));
        var under18 = active.Count(e => e.BirthDate is { } bd && AgeYears(bd, DateTime.Today) < 18);
        var director = GuessDirector(active);
        var address = (requisites?.Address ?? string.Empty).Trim();
        var inn = (requisites?.Inn ?? string.Empty).Trim();
        var okved = (requisites?.OwnershipOkved ?? string.Empty).Trim();

        var sb = new StringBuilder();
        sb.Append("""<div class="med-contingent-print med-contingent-print-landscape">""");

        AppendApproveBlock(sb, overrides, director);
        sb.Append(Block(overrides, "doc-title",
            BuildContingentTitle(organization, year),
            "med-contingent-doc-title"));

        sb.Append("""<div class="med-contingent-meta">""");

        AppendMetaPair(sb, overrides,
            "org-label", "Организация:",
            "org-value", organization,
            "okved-label", "ОКВЭД:",
            "okved-value", okved);
        AppendMetaPair(sb, overrides,
            "legal-label", "Юридический адрес:",
            "legal-value", address,
            "okpo-label", "ОКПО:",
            "okpo-value", "");
        AppendMetaPair(sb, overrides,
            "fact-label", "Фактический адрес:",
            "fact-value", address,
            "inn-label", "ИНН:",
            "inn-value", inn);

        sb.Append("""<div class="med-contingent-meta-row med-contingent-meta-stats">""");
        sb.Append(Inline(overrides, "workers-label", "Количество работающих всего:", "med-contingent-meta-label"));
        sb.Append(' ');
        sb.Append(Inline(overrides, "workers-total", workersTotal.ToString(CultureInfo.InvariantCulture), "med-contingent-meta-value"));
        sb.Append("""<span class="med-contingent-meta-sep">,</span> """);
        sb.Append(Inline(overrides, "workers-women-label", "в том числе женщин:", "med-contingent-meta-label"));
        sb.Append(' ');
        sb.Append(Inline(overrides, "workers-women", workersWomen.ToString(CultureInfo.InvariantCulture), "med-contingent-meta-value"));
        sb.Append("""<span class="med-contingent-meta-sep">;</span> """);
        sb.Append(Inline(overrides, "under18-label", "из них до 18 лет:", "med-contingent-meta-label"));
        sb.Append(' ');
        sb.Append(Inline(overrides, "under18-value", under18.ToString(CultureInfo.InvariantCulture), "med-contingent-meta-value"));
        sb.Append("""<span class="med-contingent-meta-sep">;</span> """);
        sb.Append(Inline(overrides, "profession-count-label", "профессий:", "med-contingent-meta-label"));
        sb.Append(' ');
        sb.Append(Inline(overrides, "profession-count",
            rows.Select(r => r.Profession).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                .ToString(CultureInfo.InvariantCulture),
            "med-contingent-meta-value"));
        sb.Append("</div>");

        sb.Append("""<div class="med-contingent-meta-row">""");
        sb.Append(Inline(overrides, "disability-label",
            "Из них работников, которым установлена стойкая утрата трудоспособности:",
            "med-contingent-meta-label"));
        sb.Append(' ');
        sb.Append(Inline(overrides, "disability-value", "", "med-contingent-meta-value med-contingent-meta-wide"));
        sb.Append("</div>");

        sb.Append("""<div class="med-contingent-meta-row med-contingent-meta-orders">""");
        sb.Append("""<div class="med-contingent-meta-order-pmo">""");
        sb.Append(Inline(overrides, "order-pmo-label",
            "Приказ о проведении ПМО (дата, №):",
            "med-contingent-meta-label"));
        sb.Append(' ');
        sb.Append(Inline(overrides, "order-pmo-value", "", "med-contingent-meta-value med-contingent-meta-wide"));
        sb.Append("</div>");
        sb.Append("""<div class="med-contingent-meta-order-mz">""");
        sb.Append(Inline(overrides, "order-mz", "Приказы МЗ РФ № 988н и № 29н", "med-contingent-meta-label"));
        sb.Append("</div></div>");
        sb.Append("</div>");

        sb.Append("""
<table class="med-contingent-print-table">
<colgroup>
  <col class="med-col-num" />
  <col class="med-col-dept" />
  <col class="med-col-prof" />
  <col class="med-col-factors" />
  <col class="med-col-code" />
  <col class="med-col-code" />
""");
        for (var i = 0; i < 18; i++)
        {
            sb.Append("""<col class="med-col-n" />""");
        }

        sb.Append("</colgroup>");
        AppendContingentHeader(sb);
        sb.Append("<tbody>");
        foreach (var row in rows)
        {
            AppendContingentDataRows(sb, row);
        }

        // ИТОГО только в конце таблицы (не tfoot — иначе браузер повторяет на каждой странице печати).
        sb.Append("<tr class=\"med-contingent-total\">");
        sb.Append("""<td></td><td colspan="2"><strong>ИТОГО</strong></td><td></td><td></td><td></td>""");
        AppendNums(sb, totals.WorkersTotal, totals.WorkersWomen, totals.ExamTotal, totals.ExamWomen,
            totals.LpuTotal, totals.LpuWomen, totals.PathologyTotal, totals.PathologyWomen,
            totals.AccidentTotal, totals.AccidentWomen, totals.ExceedLimitTotal, totals.ExceedLimitWomen,
            totals.OccupationalDiseaseTotal, totals.OccupationalDiseaseWomen,
            totals.SpecialConditionsTotal, totals.SpecialConditionsWomen,
            totals.Experience5YearsTotal, totals.Experience5YearsWomen);
        sb.Append("</tr></tbody></table></div>");
        return sb.ToString();
    }

    public static string BuildNamedListHtml(
        string organization,
        IReadOnlyList<MedContingentNamedListRow> rows,
        MedContingentPrintSettings? printSettings = null,
        IReadOnlyList<OrganizationEmployee>? employees = null)
    {
        var overrides = printSettings?.Overrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var year = DateTime.Today.Year;
        var director = GuessDirector((employees ?? []).Where(e => e.IsActive).ToList());

        var sb = new StringBuilder();
        sb.Append("""<div class="med-contingent-print med-contingent-print-landscape">""");
        AppendApproveBlock(sb, overrides, director);

        sb.Append(Block(overrides, "named-org", organization, "med-contingent-doc-org"));
        sb.Append(Block(overrides, "named-title", "Поименный список", "med-contingent-doc-title"));
        sb.Append(Block(overrides, "named-subtitle",
            $"лиц, подлежащих периодическим медицинским осмотрам в лечебно-профилактическом учреждении в {year} году",
            "med-contingent-doc-subtitle"));

        sb.Append("""<table class="med-contingent-print-table med-contingent-named-print">""");
        sb.Append("""
<colgroup>
  <col class="med-named-c1" />
  <col class="med-named-c2" />
  <col class="med-named-c3" />
  <col class="med-named-c4" />
  <col class="med-named-c5" />
  <col class="med-named-c6" />
  <col class="med-named-c7" />
  <col class="med-named-c8" />
  <col class="med-named-c9" />
  <col class="med-named-c10" />
  <col class="med-named-c11" />
  <col class="med-named-c12" />
  <col class="med-named-c13" />
  <col class="med-named-c14" />
  <col class="med-named-c15" />
</colgroup>
<thead>
<tr>
  <th rowspan="2">п.н.</th>
  <th rowspan="2">Фамилия, Имя, Отчество</th>
  <th rowspan="2">Дата, месяц, год рождения</th>
  <th rowspan="2">СНИЛС</th>
  <th rowspan="2">Наименование структурного подразделения</th>
  <th>Профессия</th>
  <th colspan="2">Стаж работы</th>
  <th rowspan="2">Вредные и опасные производственные факторы</th>
  <th rowspan="2">Фактический уровень фактора (мг/м³, В/м и пр.)</th>
  <th rowspan="2">Класс условий труда</th>
  <th rowspan="2">Номер пункта по Приказу МЗ РФ 988н</th>
  <th rowspan="2">Номер пункта по Приказу МЗ РФ 29н</th>
  <th colspan="2">Дата последнего медицинского осмотра</th>
</tr>
<tr>
  <th>название</th>
  <th>в контакте с вредными факторами производственной среды и трудового процесса (общий)</th>
  <th>в контакте с вредными факторами производственной среды и трудового процесса (в организации)</th>
  <th>в ЛПУ</th>
  <th>в ЦПП</th>
</tr>
<tr class="med-contingent-col-numbers">
""");
        for (var i = 1; i <= 15; i++)
        {
            sb.Append($"<th>{i}</th>");
        }

        sb.Append("</tr></thead><tbody>");
        if (rows.Count == 0)
        {
            sb.Append("""<tr><td colspan="15">Нет профессий с ненулевой колонкой 11 (в ЛПУ) в перечне контингентов.</td></tr>""");
        }

        foreach (var row in rows)
        {
            AppendNamedDataRows(sb, row);
        }

        sb.Append("</tbody></table></div>");
        return sb.ToString();
    }

    private static void AppendApproveBlock(
        StringBuilder sb,
        IDictionary<string, string> overrides,
        string directorSign)
    {
        sb.Append("""<div class="med-contingent-approve">""");
        sb.Append(Block(overrides, "approve-title", "УТВЕРЖДАЮ", "med-contingent-approve-title"));
        sb.Append(Block(overrides, "approve-role", "Директор", "med-contingent-approve-role"));
        sb.Append(Block(overrides, "approve-name",
            string.IsNullOrWhiteSpace(directorSign) ? "/_______________/" : directorSign,
            "med-contingent-approve-name"));
        sb.Append(Block(overrides, "approve-date",
            $"«___» ______________ {DateTime.Today.Year} г.",
            "med-contingent-approve-date"));
        sb.Append("</div>");
    }

    private static void AppendMetaPair(
        StringBuilder sb,
        IDictionary<string, string> overrides,
        string leftLabelKey, string leftLabel,
        string leftValueKey, string leftValue,
        string rightLabelKey, string rightLabel,
        string rightValueKey, string rightValue)
    {
        sb.Append("""<div class="med-contingent-meta-row med-contingent-meta-split">""");
        sb.Append("""<div class="med-contingent-meta-left">""");
        sb.Append(Inline(overrides, leftLabelKey, leftLabel, "med-contingent-meta-label"));
        sb.Append(' ');
        sb.Append(Inline(overrides, leftValueKey, leftValue, "med-contingent-meta-value"));
        sb.Append("</div>");
        sb.Append("""<div class="med-contingent-meta-right">""");
        sb.Append(Inline(overrides, rightLabelKey, rightLabel, "med-contingent-meta-label"));
        sb.Append(' ');
        sb.Append(Inline(overrides, rightValueKey, rightValue, "med-contingent-meta-value"));
        sb.Append("</div></div>");
    }

    private static void AppendContingentHeader(StringBuilder sb)
    {
        sb.Append("""
<thead>
<tr>
  <th rowspan="4">№</th>
  <th rowspan="4">Цех, участок, отдел, производство</th>
  <th rowspan="4">Профессия</th>
  <th rowspan="4">Факторы производственной среды</th>
  <th rowspan="4">№ прил. и пункта приказа № 988н</th>
  <th rowspan="4">№ прил. и пункта приказа № 29н</th>
  <th colspan="2">Численность работающих</th>
  <th colspan="16">Количество лиц, подлежащих осмотру</th>
</tr>
<tr>
  <th rowspan="3">Всего</th>
  <th rowspan="3">в т.ч. жен</th>
  <th colspan="2">Всего</th>
  <th colspan="2">в ЛПУ</th>
  <th colspan="12">В Центре профессиональной патологии</th>
</tr>
<tr>
  <th rowspan="2">всего</th><th rowspan="2">в т.ч. жен</th>
  <th rowspan="2">всего</th><th rowspan="2">в т.ч. жен</th>
  <th colspan="2">Всего</th>
  <th colspan="2">Участники аварийных ситуаций или инцидентов</th>
  <th colspan="2">Работники, занятые на работах с ФПС и ФТП с разовым или многократным превышением ПДК (ПДУ) по действующему фактору</th>
  <th colspan="2">Работники, имеющие (имевшие) заключение о предварительном диагнозе профессионального заболевания, лица со стойкими последствиями несчастных случаев на производстве, лица из группы повышенного риска развития профессиональных заболеваний</th>
  <th colspan="2">Работники, имеющие решения врачебной комиссией о специальных условиях проведения ПМО (иная периодичность, объём исследований, уровень медицинской организации и пр.)</th>
  <th colspan="2">Работники, на рабочих местах которых отсутствует информация о концентрациях, уровнях, параметрах ФПС и ФТП 1 год и более</th>
</tr>
<tr>
  <th>всего</th><th>в т.ч. жен</th>
  <th>всего</th><th>в т.ч. жен</th>
  <th>всего</th><th>в т.ч. жен</th>
  <th>всего</th><th>в т.ч. жен</th>
  <th>всего</th><th>в т.ч. жен</th>
  <th>всего</th><th>в т.ч. жен</th>
</tr>
<tr class="med-contingent-col-numbers">
""");
        for (var i = 1; i <= 24; i++)
        {
            sb.Append($"<th>{i}</th>");
        }

        sb.Append("</tr></thead>");
    }

    private static void AppendContingentDataRows(StringBuilder sb, MedContingentListRow row)
    {
        var factors = MedContingentFactorLines.Split(row.EnvironmentFactors);
        var codes988 = MedContingentFactorLines.Split(row.Order988n);
        var codes29 = MedContingentFactorLines.Split(row.Order29n);
        var count = Math.Max(1, Math.Max(factors.Count, Math.Max(codes988.Count, codes29.Count)));

        for (var i = 0; i < count; i++)
        {
            sb.Append("<tr>");
            if (i == 0)
            {
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.RowNumber}</td>");
                sb.Append($"<td rowspan=\"{count}\">{Web(row.Department)}</td>");
                sb.Append($"<td rowspan=\"{count}\">{Web(row.Profession)}</td>");
            }

            sb.Append($"<td{StyleAttr(row, $"f{i}")}>{Web(MedContingentFactorLines.At(factors, i))}</td>");
            sb.Append($"<td class=\"med-c\"{StyleAttr(row, $"a{i}")}>{Web(MedContingentFactorLines.At(codes988, i))}</td>");
            sb.Append($"<td class=\"med-c\"{StyleAttr(row, $"b{i}")}>{Web(MedContingentFactorLines.At(codes29, i))}</td>");

            if (i == 0)
            {
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.WorkersTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.WorkersWomen}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.ExamTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.ExamWomen}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.LpuTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.LpuWomen}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.PathologyTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.PathologyWomen}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.AccidentTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.AccidentWomen}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.ExceedLimitTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.ExceedLimitWomen}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.OccupationalDiseaseTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.OccupationalDiseaseWomen}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.SpecialConditionsTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.SpecialConditionsWomen}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.Experience5YearsTotal}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.Experience5YearsWomen}</td>");
            }

            sb.Append("</tr>");
        }
    }

    private static void AppendNamedDataRows(StringBuilder sb, MedContingentNamedListRow row)
    {
        var factors = MedContingentFactorLines.Split(row.HarmfulFactors);
        var codes988 = MedContingentFactorLines.Split(row.Order988n);
        var codes29 = MedContingentFactorLines.Split(row.Order29n);
        var count = Math.Max(1, Math.Max(factors.Count, Math.Max(codes988.Count, codes29.Count)));

        for (var i = 0; i < count; i++)
        {
            // Каждая строка фактора — отдельный <tr>; ФИО и пр. через rowspan.
            // med-named-person-start — толстая граница между людьми.
            sb.Append(i == 0
                ? "<tr class=\"med-named-person-start\">"
                : "<tr class=\"med-named-factor-cont\">");
            if (i == 0)
            {
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{row.RowNumber}</td>");
                sb.Append($"<td rowspan=\"{count}\">{Web(row.FullName)}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{Web(FormatBirth(row.BirthDate))}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{Web(row.Snils)}</td>");
                sb.Append($"<td rowspan=\"{count}\">{Web(row.Department)}</td>");
                sb.Append($"<td rowspan=\"{count}\">{Web(row.Profession)}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{Web(row.ExperienceTotal)}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{Web(row.ExperienceInOrg)}</td>");
            }

            sb.Append($"<td class=\"med-named-factor-cell\">{Web(MedContingentFactorLines.At(factors, i))}</td>");
            sb.Append($"<td class=\"med-c\">{Web(i == 0 ? row.ActualLevel : string.Empty)}</td>");
            sb.Append($"<td class=\"med-c\">{Web(i == 0 ? row.ConditionsClass : string.Empty)}</td>");
            sb.Append($"<td class=\"med-c med-named-code-cell\">{Web(MedContingentFactorLines.At(codes988, i))}</td>");
            sb.Append($"<td class=\"med-c med-named-code-cell\">{Web(MedContingentFactorLines.At(codes29, i))}</td>");
            if (i == 0)
            {
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{Web(row.LastExamLpu)}</td>");
                sb.Append($"<td class=\"med-c\" rowspan=\"{count}\">{Web(row.LastExamCpp)}</td>");
            }

            sb.Append("</tr>");
        }
    }

    private static string BuildContingentTitle(string organization, int year)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Contains("ООО", StringComparison.OrdinalIgnoreCase) ||
            org.Contains("общество", StringComparison.OrdinalIgnoreCase))
        {
            var name = ExtractQuotedName(org);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = org;
            }

            return $"Список контингентов общества с ограниченной ответственностью «{name}», подлежащих предварительным и периодическим медицинским осмотрам в {year} году";
        }

        if (org.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
        {
            return $"Список контингентов {org}, подлежащих предварительным и периодическим медицинским осмотрам в {year} году";
        }

        return $"Список контингентов {org}, подлежащих предварительным и периодическим медицинским осмотрам в {year} году";
    }

    private static string ExtractQuotedName(string org)
    {
        var m = Regex.Match(org, "[«\"]([^»\"]+)[»\"]");
        if (m.Success)
        {
            return m.Groups[1].Value.Trim();
        }

        var cleaned = Regex.Replace(org, @"^(ООО|ОАО|АО|ЗАО)\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
        return cleaned;
    }

    private static string GuessDirector(IReadOnlyList<OrganizationEmployee> employees)
    {
        var director = employees.FirstOrDefault(e =>
            string.Equals((e.Profession ?? string.Empty).Trim(), "Директор", StringComparison.OrdinalIgnoreCase));
        if (director is null)
        {
            return string.Empty;
        }

        return "/" + FormatInitials(director.Name) + "/";
    }

    private static string FormatInitials(string fullName)
    {
        var parts = (fullName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        var last = parts[0];
        var initials = new StringBuilder();
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            initials.Append(char.ToUpperInvariant(parts[i][0])).Append('.');
        }

        return $"{last} {initials}".Trim();
    }

    private static string FormatBirth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
        {
            return dt.ToString("dd.MM.yyyy");
        }

        return value.Trim();
    }

    private static void AppendNums(StringBuilder sb, params int[] values)
    {
        foreach (var v in values)
        {
            sb.Append($"<td class=\"med-c\"><strong>{v}</strong></td>");
        }
    }

    private static string Block(
        IDictionary<string, string> overrides,
        string key,
        string value,
        string? cssClass = null)
    {
        var text = Resolve(overrides, key, value);
        var cls = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        return $"<div{cls} contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</div>";
    }

    private static string Inline(
        IDictionary<string, string> overrides,
        string key,
        string value,
        string? cssClass = null)
    {
        var text = Resolve(overrides, key, value);
        var cls = string.IsNullOrWhiteSpace(cssClass) ? string.Empty : $" class=\"{cssClass}\"";
        return $"<span{cls} contenteditable=\"true\" data-override-key=\"{Web(key)}\">{Web(text)}</span>";
    }

    private static string Resolve(IDictionary<string, string> overrides, string key, string value)
    {
        if (!overrides.TryGetValue(key, out var ov) || ov is null)
        {
            return value ?? string.Empty;
        }

        // Подписи и заголовок, которые мы поправили в шаблоне — не тащим старые «корявые» правки.
        if (key is "order-pmo-label" or "order-mz" or "under18-label" or "disability-label"
            or "workers-women-label" or "profession-count-label")
        {
            return value ?? string.Empty;
        }

        if (key == "doc-title")
        {
            var t = ov.Trim();
            if (t.Length == 0
                || t.Contains("202...", StringComparison.Ordinal)
                || t.Contains("202…", StringComparison.Ordinal)
                || t.Contains("Список лиц", StringComparison.OrdinalIgnoreCase))
            {
                return value ?? string.Empty;
            }
        }

        if (key == "approve-date")
        {
            var t = ov.Trim();
            if (t.Length == 0
                || t.Contains("\"___\"", StringComparison.Ordinal)
                || Regex.IsMatch(t, @"_{6,}\d{4}"))
            {
                return value ?? string.Empty;
            }
        }

        // Пустая правка не перекрывает автоподстановку реквизитов / чисел.
        if (string.IsNullOrWhiteSpace(ov) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return ov;
    }

    private static bool IsFemale(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var last = parts[^1];
        return last.EndsWith('а') || last.EndsWith('я') || last.EndsWith("вна", StringComparison.OrdinalIgnoreCase);
    }

    private static int AgeYears(DateTime birth, DateTime today)
    {
        var age = today.Year - birth.Year;
        if (birth.Date > today.AddYears(-age))
        {
            age--;
        }

        return age;
    }

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string StyleAttr(MedContingentListRow row, string key)
    {
        var css = row.StyleFor(key);
        return string.IsNullOrWhiteSpace(css) ? string.Empty : $" style=\"{Web(css)}\"";
    }
}

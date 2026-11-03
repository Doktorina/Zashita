using System.Globalization;
using System.Net;
using System.Text;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Печатная форма «Приказ о создании комиссии СОУТ» с приложением (график).</summary>
public static class SoutCommissionOrderPrintBuilder
{
    public static string Build(
        SoutCommissionRecord commission,
        string organization,
        IReadOnlyList<OrganizationEmployee>? employees = null,
        string? bankLetterhead = null)
    {
        var org = string.IsNullOrWhiteSpace(organization)
            ? (commission.Organization ?? string.Empty).Trim()
            : organization.Trim();
        var orderDate = commission.OrderDate is { Year: >= 2000 } d
            ? d.Date
            : (commission.CompositionApprovalDate is { Year: >= 2000 } a ? a.Date : DateTime.Today);
        var day = orderDate.Day.ToString("00", CultureInfo.InvariantCulture);
        var monthGenitive = RiskPrintChrome.MonthGenitive(orderDate.Month);
        var year = orderDate.Year.ToString(CultureInfo.InvariantCulture);
        var number = FormatOrderNumber(commission.OrderNumber);
        var staff = employees ?? [];

        SoutCommissionMemberHelpers.SyncComposition(commission);
        var chairMember = SoutCommissionMemberHelpers.FindChairman(commission.Members);
        var chairmanLine = FormatChairmanLine(chairMember, org, staff);
        var chairmanSign = FormatChairmanSign(chairMember, org, staff);

        var members = (commission.Members ?? [])
            .Where(m => !SoutCommissionMemberHelpers.IsChairmanStatus(m.Status))
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select(FormatMemberLine)
            .ToList();

        var responsible = ResolveResponsible(commission.Members, chairmanSign);
        var listDeadline = orderDate.AddDays(7).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var orderDeadline = orderDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        var signFio = RiskPrintChrome.ResolveDirectorSignInitials(org, staff);

        commission.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var letterheadOrg = Ov(commission, "order-org", RiskPrintChrome.FormatLetterheadOrganization(org));
        var bankDefault = RiskPrintChrome.ResolveBankLetterhead(
            commission.PrintOverrides.GetValueOrDefault("order-bank"),
            bankLetterhead);

        var sb = new StringBuilder();
        sb.Append("""<div class="risks-order-sheet sout-commission-order-sheet">""");
        sb.Append("""<div class="risks-order-letterhead">""");
        sb.Append($"""<p class="risks-order-letterhead-name" contenteditable="true" data-override-key="order-org">{Web(letterheadOrg)}</p>""");
        sb.Append("""<div class="risks-order-letterhead-rule risks-order-letterhead-rule-double"></div>""");
        sb.Append($"""<p class="risks-order-letterhead-bank" contenteditable="true" data-override-key="order-bank">{Web(bankDefault)}</p>""");
        sb.Append("""<div class="risks-order-letterhead-rule risks-order-letterhead-rule-double"></div></div>""");

        sb.Append(string.IsNullOrWhiteSpace(number) || number == "____"
            ? """<p class="risks-order-title" contenteditable="true" data-override-key="order-title">ПРИКАЗ № ____</p>"""
            : $"""<p class="risks-order-title" contenteditable="true" data-override-key="order-title">ПРИКАЗ № {Web(number)}</p>""");

        sb.Append("""<div class="risks-order-meta risks-order-meta-2">""");
        sb.Append($"""<p class="risks-order-date risks-order-filled" contenteditable="true" data-override-key="order-date">«{day}» {Web(monthGenitive)} {year} года</p>""");
        sb.Append("""<p class="risks-order-city risks-order-filled" contenteditable="true" data-override-key="order-city">г. Екатеринбург</p>""");
        sb.Append("</div>");

        sb.Append("""<p class="risks-order-subject" contenteditable="true" data-override-key="order-subject">О создании комиссии для организации и проведения специальной оценки условий труда (далее – «СОУТ»)</p>""");
        sb.Append("""<p class="risks-order-preamble" contenteditable="true" data-override-key="order-preamble">В целях реализации требований ст. 212 Трудового кодекса Российской Федерации от 30 декабря 2001 г. № 197-ФЗ, федерального закона от 28.12.2013 г. № 426-ФЗ «О специальной оценке условий труда» (далее «Федеральный закон») и Приказа Минтруда России № 817н от 21.11.2023 г. «Об утверждении Методики проведения специальной оценки условий труда, Классификатора вредных и (или) опасных производственных факторов, формы отчета о проведении специальной оценки условий труда и инструкции по её заполнению»</p>""");
        sb.Append("""<p class="risks-order-command">ПРИКАЗЫВАЮ:</p><ol class="risks-order-points">""");

        sb.Append("<li><span>Создать комиссию для организации и проведения специальной оценки условий труда в следующем составе:</span>");
        sb.Append("<ul class=\"risks-order-sublist\">");
        sb.Append("<li>Председатель комиссии: — ");
        sb.Append($"""<span contenteditable="true" data-override-key="order-chairman">{Web(chairmanLine)}</span>""");
        sb.Append(";</li>");
        if (members.Count > 0)
        {
            sb.Append("<li>Члены комиссии:");
            sb.Append("<ul class=\"risks-order-sublist\">");
            for (var i = 0; i < members.Count; i++)
            {
                sb.Append($"""<li contenteditable="true" data-override-key="order-member-{i}">{Web(members[i])};</li>""");
            }

            sb.Append("</ul></li>");
        }

        sb.Append("</ul></li>");

        sb.Append($"""<li><span contenteditable="true" data-override-key="order-point-2">Подготовить перечень рабочих мест, подлежащих специальной оценке условий труда, в срок до {Web(listDeadline)}. Ответственный — {Web(responsible)}.</span></li>""");
        sb.Append($"""<li><span contenteditable="true" data-override-key="order-point-3">Осуществить выбор организации, проводящей специальную оценку условий труда, в срок до {Web(orderDeadline)}. Ответственный — {Web(responsible)}.</span></li>""");
        sb.Append($"""<li><span contenteditable="true" data-override-key="order-point-4">Подготовить график проведения специальной оценки условий труда (приложение 1 к настоящему приказу) в срок до {Web(orderDeadline)}. Ответственный — {Web(responsible)}.</span></li>""");
        sb.Append("""<li><span contenteditable="true" data-override-key="order-point-5">Обеспечить доступ экспертов организации, проводящей специальную оценку условий труда, к рабочим местам и необходимой документации.</span></li>""");
        sb.Append("""<li><span contenteditable="true" data-override-key="order-point-6">Контроль за исполнением настоящего приказа оставляю за собой.</span></li>""");
        sb.Append("</ol>");

        // Образец: тонкая линия на всю ширину после пунктов, затем подпись слева/справа.
        sb.Append("""<hr class="risks-order-sign-rule" />""");
        var signPost = ResolveOrderSignPost(org, commission, chairMember);
        sb.Append("""<div class="risks-order-sign-row2">""");
        sb.Append($"""<p class="risks-order-sign-left" contenteditable="true" data-override-key="order-sign-post">{Web(signPost)}</p>""");
        sb.Append($"""<p class="risks-order-sign-right" contenteditable="true" data-override-key="order-sign-fio">{Web(string.IsNullOrWhiteSpace(signFio) ? "__________________" : signFio)}</p>""");
        sb.Append("</div>");

        var orderNumberLabel = string.IsNullOrWhiteSpace(number) || number == "____" ? "____" : number;

        sb.Append("""<div class="risks-page-break"></div>""");
        sb.Append("""<div class="sout-commission-appendix">""");
        sb.Append($"""<p class="sout-commission-appendix-ref" contenteditable="true" data-override-key="appendix-ref">Приложение 1<br/>к Приказу №{Web(orderNumberLabel)} от «{day}» {Web(monthGenitive)} {year} г.</p>""");
        sb.Append($"""<h2 class="sout-commission-appendix-title" contenteditable="true" data-override-key="appendix-title">График проведения СОУТ в {Web(org)}</h2>""");
        sb.Append("""<table class="sout-commission-schedule"><thead><tr><th>№ п/п</th><th>Этапы</th><th>Сроки</th></tr></thead><tbody>""");

        AppendScheduleRow(sb, 1, "schedule-1-stage", "schedule-1-deadline",
            "Издание приказа о проведении специальной оценки условий труда и создании комиссии.",
            orderDeadline);
        AppendScheduleRow(sb, 2, "schedule-2-stage", "schedule-2-deadline",
            "Подготовка перечня рабочих мест, подлежащих специальной оценке условий труда.",
            listDeadline);
        AppendScheduleRow(sb, 3, "schedule-3-stage", "schedule-3-deadline",
            "Выбор Аттестующей организации и заключение договора.",
            listDeadline);
        AppendScheduleRow(sb, 4, "schedule-4-stage", "schedule-4-deadline",
            "Предоставление всей необходимой информации для проведения специальной оценки условий труда в Аттестующую организацию.",
            "в сроки, предусмотренные договором");
        AppendScheduleRow(sb, 5, "schedule-5-stage", "schedule-5-deadline",
            "Проведение экспертами выбранной организации-исполнителем идентификации потенциально вредных и (или) опасных производственных факторов, проведение исследований (измерений) идентифицированных вредных и (или) опасных производственных факторов, проведение оценки условий труда, проведение оценки эффективности применения средств индивидуальной защиты (при необходимости). Составление отчета по специальной оценке условий труда экспертом Аттестующей организации.",
            "в сроки, предусмотренные договором");
        AppendScheduleRow(sb, 6, "schedule-6-stage", "schedule-6-deadline",
            "Рассмотрение и утверждение отчета о проведении специальной оценки условий труда.",
            "в течение 10 рабочих дней");
        AppendScheduleRow(sb, 7, "schedule-7-stage", "schedule-7-deadline",
            "Ознакомление работников с результатами специальной оценки условий труда на их рабочих местах",
            "в течение 30 календарных дней со дня утверждения отчёта");
        AppendScheduleRow(sb, 8, "schedule-8-stage", "schedule-8-deadline",
            "Представление скана титульного листа отчета о специальной оценке условий труда Аттестующей организации.",
            "в течение 3 календарных дней со дня утверждения отчёта");
        AppendScheduleRow(sb, 9, "schedule-9-stage", "schedule-9-deadline",
            "Размещение информации о проведенной специальной оценке условий труда на официальном сайте (при наличии) в соответствии с ФЗ-426",
            "в течение 30 календарных дней со дня утверждения отчёта");

        sb.Append("</tbody></table>");

        sb.Append("""<div class="risks-chair-footer sout-commission-chair-footer">""");
        sb.Append("""<div class="risks-chair-label">Председатель комиссии</div>""");
        sb.Append("""<div class="risks-chair-fields"><div class="risks-chair-col"><div class="risks-chair-line"></div><div class="risks-chair-hint">(подпись)</div></div>""");
        sb.Append($"""<div class="risks-chair-col risks-chair-name"><div class="risks-chair-value" contenteditable="true" data-override-key="appendix-chair-sign">{Web(chairmanSign)}</div><div class="risks-chair-hint">(Ф.И.О.)</div></div>""");
        sb.Append("""<div class="risks-chair-col"><div class="risks-chair-line"></div><div class="risks-chair-hint">(дата)</div></div></div></div>""");

        sb.Append("""</div>"""); // sout-commission-appendix
        sb.Append("</div>"); // risks-order-sheet
        return sb.ToString();
    }

    private static void AppendScheduleRow(
        StringBuilder sb,
        int index,
        string stageKey,
        string deadlineKey,
        string stageDefault,
        string deadlineDefault)
    {
        sb.Append("<tr>");
        sb.Append($"<td class=\"sout-schedule-col-n\">{index}</td>");
        sb.Append($"""<td class="sout-schedule-col-stage" contenteditable="true" data-override-key="{Web(stageKey)}">{Web(stageDefault)}</td>""");
        sb.Append($"""<td class="sout-schedule-col-deadline" contenteditable="true" data-override-key="{Web(deadlineKey)}">{Web(deadlineDefault)}</td>""");
        sb.Append("</tr>");
    }

    private static string FormatChairmanLine(
        SoutCommissionMember? chair,
        string org,
        IReadOnlyList<OrganizationEmployee> staff)
    {
        if (chair is not null && !string.IsNullOrWhiteSpace(chair.Name))
        {
            var formatted = RiskCommissionResolver.FormatChairmanForOrder(chair);
            if (RiskPrintChrome.IsUsablePersonChairman(formatted, org))
            {
                return formatted;
            }
        }

        var fallback = RiskPrintChrome.ResolveCommissionChairman(org, staff);
        return string.IsNullOrWhiteSpace(fallback) ? "______________________________" : fallback;
    }

    private static string FormatChairmanSign(
        SoutCommissionMember? chair,
        string org,
        IReadOnlyList<OrganizationEmployee> staff)
    {
        if (chair is not null && !string.IsNullOrWhiteSpace(chair.Name))
        {
            return RiskPrintChrome.FormatInitials(chair.Name.Trim());
        }

        return RiskPrintChrome.ResolveDirectorSignInitials(org, staff);
    }

    private static string FormatMemberLine(SoutCommissionMember member)
    {
        var name = member.Name?.Trim() ?? string.Empty;
        var pos = member.Position?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(pos) ? name : $"{name}, {pos}";
    }

    private static string ResolveResponsible(IEnumerable<SoutCommissionMember>? members, string chairmanSign)
    {
        var list = (members ?? []).Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();
        var clerk = list.FirstOrDefault(m =>
            (m.Position ?? string.Empty).Contains("делопроизвод", StringComparison.OrdinalIgnoreCase)
            || (m.Status ?? string.Empty).Contains("секретар", StringComparison.OrdinalIgnoreCase));
        if (clerk is not null)
        {
            return RiskPrintChrome.FormatInitials(clerk.Name.Trim());
        }

        var member = list.FirstOrDefault(m => !SoutCommissionMemberHelpers.IsChairmanStatus(m.Status));
        if (member is not null)
        {
            return RiskPrintChrome.FormatInitials(member.Name.Trim());
        }

        return string.IsNullOrWhiteSpace(chairmanSign) ? "________________" : chairmanSign;
    }

    private static string FormatOrderNumber(string? number)
    {
        var raw = (number ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "____";
        }

        return raw.StartsWith("№", StringComparison.OrdinalIgnoreCase)
            ? raw[1..].Trim()
            : raw;
    }

    /// <summary>
    /// Подпись приказа: для ИП — «Индивидуальный предприниматель», иначе «Директор»
    /// (учитываем шапку, должность председателя и сохранённый override).
    /// </summary>
    private static string ResolveOrderSignPost(
        string organization,
        SoutCommissionRecord commission,
        SoutCommissionMember? chair)
    {
        var saved = Ov(commission, "order-sign-post", string.Empty);
        // Старый неверный override «Директор» у ИП не оставляем.
        if (saved.Length > 0
            && !(RiskPrintChrome.IsIndividualEntrepreneur(organization)
                 && string.Equals(saved, "Директор", StringComparison.OrdinalIgnoreCase)))
        {
            return saved;
        }

        if (RiskPrintChrome.IsIndividualEntrepreneur(organization))
        {
            return "Индивидуальный предприниматель";
        }

        var letterhead = RiskPrintChrome.FormatLetterheadOrganization(organization);
        if (letterhead.StartsWith("Индивидуальный предприниматель", StringComparison.OrdinalIgnoreCase)
            || letterhead.StartsWith("ИП ", StringComparison.OrdinalIgnoreCase))
        {
            return "Индивидуальный предприниматель";
        }

        if (RiskPrintChrome.IsIndividualEntrepreneurProfessionLabel(chair?.Position))
        {
            return "Индивидуальный предприниматель";
        }

        return "Директор";
    }

    private static string Ov(SoutCommissionRecord commission, string key, string fallback)
    {
        if (commission.PrintOverrides is not null
            && commission.PrintOverrides.TryGetValue(key, out var ov)
            && !string.IsNullOrWhiteSpace(ov))
        {
            return ov.Trim();
        }

        return fallback ?? string.Empty;
    }

    private static string Web(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}

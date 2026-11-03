using System.Globalization;
using System.Net;
using System.Text;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Печатная форма приказа на оценку рисков (создание комиссии).</summary>
public static class RiskUnscheduledOrderPrintBuilder
{
    public static string Build(
        RiskUnscheduledOrderDocument document,
        string? mode = null,
        IReadOnlyList<OrganizationEmployee>? employees = null,
        string? bankLetterhead = null,
        SoutCommissionRecord? commission = null)
    {
        _ = mode;
        var orderDate = document.Date.Date == default ? DateTime.Today : document.Date.Date;
        if (orderDate.Year < 2000)
        {
            orderDate = DateTime.Today;
        }

        var day = orderDate.Day.ToString("00", CultureInfo.InvariantCulture);
        var monthGenitive = RiskPrintChrome.MonthGenitive(orderDate.Month);
        var year = orderDate.Year.ToString(CultureInfo.InvariantCulture);
        // Номер приказа — из карточки комиссии («№ приказа о создании комиссии»), иначе из документа.
        var numberShort = FormatOrderNumber(ResolveOrderNumber(document, commission));
        var org = (document.Organization ?? string.Empty).Trim();
        var staff = employees ?? [];
        var chairman = ResolveChairman(org, staff, commission);
        var members = ResolveMembers(commission);
        var signFio = RiskPrintChrome.ResolveDirectorSignInitials(org, staff);

        // Дата приказа — из комиссии, если задана (и пользователь не переопределил order-date).
        if (commission?.OrderDate is { Year: >= 2000 } commissionDate
            && (document.PrintOverrides is null
                || !document.PrintOverrides.TryGetValue("order-date", out var dateOv)
                || string.IsNullOrWhiteSpace(dateOv)))
        {
            orderDate = commissionDate.Date;
            day = orderDate.Day.ToString("00", CultureInfo.InvariantCulture);
            monthGenitive = RiskPrintChrome.MonthGenitive(orderDate.Month);
            year = orderDate.Year.ToString(CultureInfo.InvariantCulture);
        }

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
        sb.Append("""<div class="risks-order-letterhead-rule risks-order-letterhead-rule-double"></div>""");
        sb.Append("</div>");

        var titleText = string.IsNullOrWhiteSpace(numberShort) || numberShort == "____"
            ? "ПРИКАЗ № ____"
            : $"ПРИКАЗ № {numberShort}";
        // Если у комиссии задан номер приказа — не даём старому PrintOverrides перекрыть его.
        if (!string.IsNullOrWhiteSpace(NormalizeOrderNumber(commission?.OrderNumber)))
        {
            sb.Append($"<p contenteditable=\"true\" data-override-key=\"order-title\" class=\"risks-order-title risks-order-filled\">{Web(titleText)}</p>");
        }
        else
        {
            sb.Append(CellBlock(document, "order-title", titleText, "risks-order-title"));
        }

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
        sb.Append("""<p class="risks-order-command">ПРИКАЗЫВАЮ:</p>""");
        sb.Append("""<ol class="risks-order-points">""");

        sb.Append("<li>");
        sb.Append("""<span>Создать комиссию по проведению оценки профессиональных рисков в следующем составе:</span>""");
        sb.Append("<ul class=\"risks-order-sublist\">");
        sb.Append("<li>Председатель комиссии: — ");
        sb.Append(InlineEditable(document, "order-chairman",
            string.IsNullOrWhiteSpace(chairman) ? "______________________________" : chairman));
        sb.Append(";</li>");

        if (members.Count > 0)
        {
            sb.Append("<li>Члены комиссии:");
            sb.Append("<ul class=\"risks-order-sublist\">");
            for (var i = 0; i < members.Count; i++)
            {
                sb.Append("<li>");
                sb.Append(InlineEditable(document, $"order-member-{i}", members[i]));
                sb.Append(";</li>");
            }

            sb.Append("</ul></li>");
        }

        sb.Append("</ul></li>");

        var deadline = orderDate.AddMonths(2);
        sb.Append(OrderPoint(document, "order-point-2",
            $"Председателю комиссии провести оценку профессиональных рисков в соответствии с требованиями, установленными в положении о СУОТ, в срок — до {deadline:dd.MM.yyyy}."));
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

    private static string ResolveChairman(
        string org,
        IReadOnlyList<OrganizationEmployee> staff,
        SoutCommissionRecord? commission)
    {
        if (commission is not null)
        {
            RiskCommissionResolver.EnsureMembers(commission);
            var chair = SoutCommissionMemberHelpers.FindChairman(commission.Members);
            if (chair is not null && !string.IsNullOrWhiteSpace(chair.Name))
            {
                var formatted = RiskCommissionResolver.FormatChairmanForOrder(chair);
                if (RiskPrintChrome.IsUsablePersonChairman(formatted, org))
                {
                    return formatted;
                }
            }
        }

        return RiskPrintChrome.ResolveCommissionChairman(org, staff);
    }

    private static List<string> ResolveMembers(SoutCommissionRecord? commission)
    {
        if (commission is null)
        {
            return [];
        }

        RiskCommissionResolver.EnsureMembers(commission);
        return RiskCommissionResolver.FormatMembersForOrder(commission.Members).ToList();
    }

    private static string OrderPoint(RiskUnscheduledOrderDocument document, string key, string text) =>
        $"<li>{CellBlock(document, key, text, "risks-order-preamble")}</li>";

    private static string InlineEditable(RiskUnscheduledOrderDocument document, string key, string value)
    {
        var text = Resolve(document, key, value);
        var filled = !string.IsNullOrWhiteSpace(text) && !text.Contains('_', StringComparison.Ordinal);
        var cls = filled ? "risks-order-inline risks-order-filled" : "risks-order-inline";
        return $"<span contenteditable=\"true\" data-override-key=\"{Web(key)}\" class=\"{cls}\">{Web(text)}</span>";
    }

    private static string CellBlock(RiskUnscheduledOrderDocument document, string key, string value, string cssClass)
    {
        var text = Resolve(document, key, value);
        var filled = !string.IsNullOrWhiteSpace(text) && !text.Contains('_', StringComparison.Ordinal);
        var cls = filled ? $"{cssClass} risks-order-filled" : cssClass;
        // <p>, не <div> — Word лучше держит выравнивание и отступы
        return $"<p contenteditable=\"true\" data-override-key=\"{Web(key)}\" class=\"{cls}\">{Web(text).Replace("\n", "<br/>")}</p>";
    }

    private static string Resolve(RiskUnscheduledOrderDocument document, string key, string fallback)
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

    private static string ResolveOrderNumber(
        RiskUnscheduledOrderDocument document,
        SoutCommissionRecord? commission)
    {
        // Ручной заголовок «ПРИКАЗ № …» — только если пользователь явно сохранил override.
        // Иначе всегда берём номер из комиссии.
        var fromCommission = NormalizeOrderNumber(commission?.OrderNumber);
        if (!string.IsNullOrWhiteSpace(fromCommission))
        {
            return fromCommission;
        }

        return NormalizeOrderNumber(document.Number);
    }

    private static string NormalizeOrderNumber(string? raw)
    {
        var n = (raw ?? string.Empty).Trim();
        if (n.StartsWith("№", StringComparison.OrdinalIgnoreCase))
        {
            n = n[1..].Trim();
        }

        return n;
    }

    private static string FormatOrderNumber(string? number)
    {
        var raw = NormalizeOrderNumber(number);
        if (raw.Length == 0)
        {
            return "____";
        }

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

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

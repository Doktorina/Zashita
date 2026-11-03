using System.Globalization;
using System.Net;
using System.Text;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>Общие фрагменты печати документов ОПР (шапки, подписи).</summary>
public static class RiskPrintChrome
{
    public static string FormatInitials(string? fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;
        if (parts.Length == 1) return parts[0];
        var last = parts[0];
        var initials = new StringBuilder();
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            initials.Append(char.ToUpperInvariant(parts[i][0])).Append('.');
        }

        return $"{last} {initials}".Trim();
    }

    public static string GuessDirectorName(IReadOnlyList<OrganizationEmployee> employees) =>
        FormatInitials(GuessDirectorFullName(employees));

    public static string GuessDirectorFullName(IReadOnlyList<OrganizationEmployee> employees) =>
        FindDirectorEmployee(employees)?.Name?.Trim() ?? string.Empty;

    /// <summary>Например: «Директор Алешкевич Александр Дмитриевич».</summary>
    public static string GuessDirectorTitleAndName(IReadOnlyList<OrganizationEmployee> employees)
    {
        var director = FindDirectorEmployee(employees);
        if (director is null || string.IsNullOrWhiteSpace(director.Name))
        {
            return string.Empty;
        }

        var title = NormalizeHeadTitle(director.Profession);
        return $"{title} {director.Name.Trim()}";
    }

    /// <summary>ФИО руководителя для строки подписи (инициалы).</summary>
    public static string ResolveDirectorSignInitials(
        string? organization,
        IReadOnlyList<OrganizationEmployee>? employees)
    {
        var fromStaff = GuessDirectorName(employees ?? []);
        if (!string.IsNullOrWhiteSpace(fromStaff) && !LooksLikeOrganizationName(fromStaff))
        {
            return fromStaff;
        }

        if (IsIndividualEntrepreneur(organization))
        {
            var person = ResolveIpPersonName(organization);
            if (!string.IsNullOrWhiteSpace(person) && !LooksLikeOrganizationName(person))
            {
                return FormatInitials(person);
            }
        }

        return string.Empty;
    }

    /// <summary>Инициалы председателя комиссии для подвала документа.</summary>
    public static string ResolveChairmanInitials(
        string? organization,
        IReadOnlyList<OrganizationEmployee>? employees)
    {
        var line = ResolveCommissionChairman(organization, employees);
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        foreach (var prefix in HeadTitlePrefixes)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var person = line[prefix.Length..].Trim();
                return LooksLikeOrganizationName(person) ? string.Empty : FormatInitials(person);
            }
        }

        return LooksLikeOrganizationName(line) ? string.Empty : FormatInitials(line);
    }

    /// <summary>
    /// Председатель комиссии: директор организации или ИП (физлицо), никогда наименование юрлица.
    /// </summary>
    public static string ResolveCommissionChairman(
        string? organization,
        IReadOnlyList<OrganizationEmployee>? employees)
    {
        var org = (organization ?? string.Empty).Trim();
        var chairman = GuessDirectorTitleAndName(employees ?? []);
        if (IsUsablePersonChairman(chairman, org))
        {
            return chairman;
        }

        var full = GuessDirectorFullName(employees ?? []);
        if (!string.IsNullOrWhiteSpace(full) && !LooksLikeOrganizationName(full))
        {
            return $"Директор {full.Trim()}";
        }

        if (IsIndividualEntrepreneur(org))
        {
            var person = ResolveIpPersonName(org);
            if (!string.IsNullOrWhiteSpace(person) && !LooksLikeOrganizationName(person))
            {
                return $"Директор {person}";
            }
        }

        return string.Empty;
    }

    public static string ResolveIpPersonName(string? organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return string.Empty;
        }

        return OrganizationMatcher.InferIndividualEntrepreneurEmployee(org)
            ?? StripIpPrefix(org);
    }

    /// <summary>Отклоняет юрлицо / пустые / шаблонные значения в поле председателя.</summary>
    public static bool IsUsablePersonChairman(string? value, string? organization)
    {
        var text = (value ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (text.Length == 0 || text.Contains('_', StringComparison.Ordinal))
        {
            return false;
        }

        if (LooksLikeOrganizationName(text))
        {
            return false;
        }

        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return true;
        }

        if (string.Equals(text, org, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, StripIpPrefix(org), StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, FormatLetterheadOrganization(org), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var withoutTitle = text;
        foreach (var prefix in HeadTitlePrefixes)
        {
            if (withoutTitle.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                withoutTitle = withoutTitle[prefix.Length..].Trim();
                break;
            }
        }

        return !LooksLikeOrganizationName(withoutTitle)
               && !string.Equals(withoutTitle, org, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(withoutTitle, StripIpPrefix(org), StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatLetterheadOrganization(string? organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return string.Empty;
        }

        if (!IsIndividualEntrepreneur(org))
        {
            return org;
        }

        var person = ResolveIpPersonName(org);
        return string.IsNullOrWhiteSpace(person)
            ? org
            : $"Индивидуальный предприниматель {person}";
    }

    public static string BlankBankLetterhead() =>
        "БИК _______________  Банк ________________________________  г. _______________\nк/с _________________________  р/с _________________________";

    public static bool IsFilledBankLetterhead(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.Contains('_', StringComparison.Ordinal))
        {
            var filled = text.Replace("_", string.Empty, StringComparison.Ordinal);
            return filled.Contains("БИК", StringComparison.OrdinalIgnoreCase)
                   && filled.Any(char.IsDigit);
        }

        return text.Contains("БИК", StringComparison.OrdinalIgnoreCase)
               && text.Any(char.IsDigit);
    }

    public static string ResolveBankLetterhead(string? overrideValue, string? knownForOrganization)
    {
        if (IsFilledBankLetterhead(overrideValue))
        {
            return overrideValue!.Trim();
        }

        if (IsFilledBankLetterhead(knownForOrganization))
        {
            return knownForOrganization!.Trim();
        }

        return BlankBankLetterhead();
    }

    public static bool LooksLikeOrganizationName(string? value)
    {
        var t = (value ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (t.Length == 0)
        {
            return false;
        }

        if (OrganizationMatcher.LooksLikeOrganization(t))
        {
            return true;
        }

        if (t.Contains("ООО", StringComparison.OrdinalIgnoreCase)
            && (t.Contains('«') || t.Contains('"') || t.Contains('\"') || t.Contains('"')))
        {
            return true;
        }

        return t.StartsWith("АО ", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("ПАО ", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("ЗАО ", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("ОАО ", StringComparison.OrdinalIgnoreCase);
    }

    public static string StripIpPrefix(string org)
    {
        var t = (org ?? string.Empty).Trim();
        if (t.StartsWith("ИП ", StringComparison.OrdinalIgnoreCase))
        {
            return t[3..].Trim();
        }

        if (t.StartsWith("ИП", StringComparison.OrdinalIgnoreCase) && t.Length > 2 && !char.IsLetter(t[2]))
        {
            return t[2..].Trim();
        }

        const string longPrefix = "Индивидуальный предприниматель";
        if (t.StartsWith(longPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return t[longPrefix.Length..].Trim(' ', '«', '»', '"', '\'');
        }

        return t;
    }

    /// <summary>
    /// Организация в картах ОПР/СОУТ: без «ИП» / «Индивидуальный предприниматель»
    /// (полное написание — только в обучении и шапках приказов).
    /// </summary>
    public static string FormatOrganizationForCards(string? organization)
    {
        var org = (organization ?? string.Empty).Trim();
        if (org.Length == 0)
        {
            return string.Empty;
        }

        if (!IsIndividualEntrepreneur(org))
        {
            return org;
        }

        var person = ResolveIpPersonName(org);
        return string.IsNullOrWhiteSpace(person) ? StripIpPrefix(org) : person.Trim();
    }

    /// <summary>Карта/РМ самого ИП — не показывать в перечнях рисков и СОУТ.</summary>
    public static bool IsIndividualEntrepreneurWorkplace(WorkplaceRecord workplace)
    {
        if (workplace.IsGroup)
        {
            return false;
        }

        var prof = string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession;
        if (IsIndividualEntrepreneurProfessionLabel(prof) || IsIndividualEntrepreneurProfessionLabel(workplace.Name))
        {
            return true;
        }

        return OrganizationMatcher.IsIndividualEntrepreneurPerson(
            workplace.Organization,
            workplace.Name,
            prof);
    }

    public static bool IsIndividualEntrepreneurProfessionLabel(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return false;
        }

        if (t.Equals("ИП", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return t.Contains("индивидуальн", StringComparison.OrdinalIgnoreCase)
               && t.Contains("предпринимател", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIndividualEntrepreneur(string? organization)
    {
        var t = (organization ?? string.Empty).Trim();
        return t.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
               || t.Contains("индивидуальн", StringComparison.OrdinalIgnoreCase);
    }

    public static string OrgRoleLabel(string? organization) =>
        IsIndividualEntrepreneur(organization) ? "Индивидуальный предприниматель" : "Руководитель организации";

    /// <summary>
    /// Должность в блоке «Утверждаю» на титуле: из штатки (Директор / Генеральный директор / ИП),
    /// иначе запасной ярлык по типу организации.
    /// </summary>
    public static string ResolveApproveRole(
        string? organization,
        IReadOnlyList<OrganizationEmployee>? employees)
    {
        var director = FindDirectorEmployee(employees ?? []);
        if (director is not null && !string.IsNullOrWhiteSpace(director.Profession))
        {
            return NormalizeHeadTitle(director.Profession);
        }

        if (IsIndividualEntrepreneur(organization))
        {
            return "Индивидуальный предприниматель";
        }

        if (director is not null)
        {
            return "Директор";
        }

        return OrgRoleLabel(organization);
    }

    public static string MonthGenitive(int month) => month switch
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
        _ => "__________"
    };

    public static void AppendChairmanFooter(StringBuilder sb, string? chairmanName)
    {
        var name = string.IsNullOrWhiteSpace(chairmanName) ? "\u00a0" : chairmanName.Trim().Trim('/');
        sb.Append("""
            <div class="risks-chair-footer">
              <div class="risks-chair-label">Председатель комиссии:</div>
              <div class="risks-chair-fields">
                <div class="risks-chair-col">
                  <div class="risks-chair-line"></div>
                  <div class="risks-chair-hint">(подпись)</div>
                </div>
                <div class="risks-chair-col risks-chair-name">
            """);
        sb.Append($"<div class=\"risks-chair-value\">{Web(name)}</div>");
        sb.Append("""
                  <div class="risks-chair-hint">(Ф.И.О.)</div>
                </div>
                <div class="risks-chair-col">
                  <div class="risks-chair-line"></div>
                  <div class="risks-chair-hint">(дата)</div>
                </div>
              </div>
            </div>
            """);
    }

    private static readonly string[] HeadTitlePrefixes =
    [
        "Генеральный директор ",
        "Индивидуальный предприниматель ",
        "Директор ",
        "Руководитель "
    ];

    private static OrganizationEmployee? FindDirectorEmployee(IReadOnlyList<OrganizationEmployee> employees)
    {
        var active = ReportableEmployees(employees);
        return active
            .Select(e => (Employee: e, Score: HeadProfessionScore(e.Profession)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Employee.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Employee)
            .FirstOrDefault();
    }

    private static List<OrganizationEmployee> ReportableEmployees(IReadOnlyList<OrganizationEmployee> employees) =>
        employees
            .Where(EmployeeHrStatus.IsEmployed)
            .Where(e => !LooksLikeOrganizationName(e.Name))
            .Where(e => !LooksLikeOrganizationName(e.Profession))
            .ToList();

    private static int HeadProfessionScore(string? profession)
    {
        var p = (profession ?? string.Empty).Trim().ToLowerInvariant();
        if (p.Length == 0)
        {
            return 0;
        }

        if (p is "директор" or "генеральный директор")
        {
            return 100;
        }

        if (p.StartsWith("генеральный директор", StringComparison.Ordinal))
        {
            return 95;
        }

        if (p.Contains("генеральн", StringComparison.Ordinal) && p.Contains("директор", StringComparison.Ordinal))
        {
            return 90;
        }

        if (p is "руководитель" or "руководитель организации")
        {
            return 85;
        }

        if (p.Contains("управляющ", StringComparison.Ordinal))
        {
            return 80;
        }

        if (p.Contains("индивидуальн", StringComparison.Ordinal)
            && p.Contains("предпринимател", StringComparison.Ordinal))
        {
            return 75;
        }

        if (p.Contains("директор", StringComparison.Ordinal)
            && !p.Contains("заместител", StringComparison.Ordinal)
            && !p.Contains("зам.", StringComparison.Ordinal))
        {
            return 70;
        }

        if (p.Contains("руководитель", StringComparison.Ordinal))
        {
            return 50;
        }

        return 0;
    }

    private static string NormalizeHeadTitle(string? profession)
    {
        var p = (profession ?? string.Empty).Trim().TrimEnd('*', ' ').Trim();
        if (string.IsNullOrWhiteSpace(p) || LooksLikeOrganizationName(p))
        {
            return "Директор";
        }

        var lower = p.ToLowerInvariant();
        if (lower is "руководитель" or "руководитель организации")
        {
            return "Руководитель";
        }

        if (lower.Contains("генеральн", StringComparison.Ordinal) && lower.Contains("директор", StringComparison.Ordinal))
        {
            return "Генеральный директор";
        }

        if (lower.Contains("индивидуальн", StringComparison.Ordinal)
            && lower.Contains("предпринимател", StringComparison.Ordinal))
        {
            return "Индивидуальный предприниматель";
        }

        if (lower is "директор" || lower.StartsWith("директор ", StringComparison.Ordinal))
        {
            return "Директор";
        }

        if (lower.Contains("директор", StringComparison.Ordinal))
        {
            return "Директор";
        }

        return p.Length > 48 ? "Директор" : p;
    }

    private static string Web(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

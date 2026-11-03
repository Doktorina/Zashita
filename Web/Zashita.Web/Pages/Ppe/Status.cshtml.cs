using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class StatusModel : PageModel
{
    private readonly IssueStandardsStore _standards;
    private readonly PpeNomenclatureStore _nomenclature;
    private readonly PpeProvisionStore _provision;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    private const string DefaultLegalBasis = "п.610 Приказ 767н";

    public StatusModel(
        IssueStandardsStore standards,
        PpeNomenclatureStore nomenclature,
        PpeProvisionStore provision,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _standards = standards;
        _nomenclature = nomenclature;
        _provision = provision;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> OrganizationOptions { get; private set; } = [];
    public IReadOnlyList<string> DepartmentOptions { get; private set; } = [];
    public IReadOnlyList<string> ProfessionOptions { get; private set; } = [];
    public IReadOnlyList<string> PpeNameOptions { get; private set; } = [];
    public IReadOnlyList<string> StatusOptions { get; } = ["Выдано", "Не выдано"];
    public IReadOnlyList<PpeProvisionViewRow> Rows { get; private set; } = [];
    public IReadOnlyList<PpeProvisionTreeLine> TreeLines { get; private set; } = [];
    public string? Message { get; private set; }

    [BindProperty]
    public DateTime ReportDate { get; set; } = DateTime.Today;

    [BindProperty(SupportsGet = true)]
    public string? SelectedOrganization { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Department { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Profession { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? PpeName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    public void OnGet() => LoadReport();

    public IActionResult OnPostSave(
        string[]? organization,
        string[]? employee,
        string[]? profession,
        string[]? ppeName,
        string[]? issueDate,
        string[]? issuedQty)
    {
        var count = new[]
        {
            organization?.Length ?? 0,
            employee?.Length ?? 0,
            profession?.Length ?? 0,
            ppeName?.Length ?? 0,
            issueDate?.Length ?? 0,
            issuedQty?.Length ?? 0
        }.DefaultIfEmpty(0).Max();

        for (var i = 0; i < count; i++)
        {
            var org = ValueAt(organization, i);
            var person = ValueAt(employee, i);
            var prof = ValueAt(profession, i);
            var item = ValueAt(ppeName, i);
            var date = ParseDate(ValueAt(issueDate, i));
            var qty = ValueAt(issuedQty, i);
            _provision.Upsert(org, person, prof, item, date, qty);
        }

        TempData["Message"] = "Даты выдачи сохранены.";
        return RedirectToPage(FilterRoute());
    }

    private void LoadReport()
    {
        ReportDate = DateTime.Today;
        string? selectedOrganization = null;
        _orgs.ApplyReportOrganizationFilter(HttpContext, ref selectedOrganization, allowAll: true);
        SelectedOrganization = selectedOrganization;
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = PpeCardTypes.ProvisionMenuTitle;
        ViewData["Subtitle"] = string.IsNullOrWhiteSpace(SelectedOrganization) ? "Все организации" : SelectedOrganization;
        Message = TempData["Message"]?.ToString();

        OrganizationOptions = _orgs.GetVisibleOrganizations(HttpContext);
        var orgsToLoad = string.IsNullOrWhiteSpace(SelectedOrganization)
            ? _employees.ResolveOrganizationsForAllReports(
                OrganizationOptions,
                _orgs.IsOrganizationHidden).ToList()
            : [SelectedOrganization.Trim()];

        var allRows = orgsToLoad.SelectMany(BuildRows).ToList();
        DepartmentOptions = DistinctSorted(allRows.Select(r => r.Department));
        ProfessionOptions = DistinctSorted(allRows.Select(r => r.Profession));
        PpeNameOptions = DistinctSorted(allRows.Select(r => r.PpeName));

        Rows = allRows
            .Where(r => string.IsNullOrWhiteSpace(Department) || string.Equals(r.Department, Department, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(Profession) || string.Equals(r.Profession, Profession, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(PpeName) || string.Equals(r.PpeName, PpeName, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(StatusFilter) || string.Equals(r.Status, StatusFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        TreeLines = BuildTree(Rows);
    }

    private List<PpeProvisionViewRow> BuildRows(string organization)
    {
        var standards = _standards.LoadAll()
            .Where(s => !string.IsNullOrWhiteSpace(s.Organization))
            .Where(s => IssueStandard.IncludesSiz(s.Category))
            .Where(s => OrganizationMatcher.Matches(organization, s.Organization))
            .ToList();
        // Только штат 1С (как МО/обучение/риски/СОУТ).
        var employees = _employees.LoadForMedReport(organization)
            .Where(EmployeeHrStatus.IsEmployed)
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
        var nomenclatureLookup = _nomenclature.LoadForEditor(standards)
            .Where(n => !string.IsNullOrWhiteSpace(n.Name))
            .GroupBy(n => n.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var provisionLookup = _provision.LoadAll()
            .Where(r => OrganizationMatcher.Matches(organization, r.Organization))
            .GroupBy(r => Key(r.Organization, r.Employee, r.Profession, r.PpeName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IssueDate ?? DateTime.MinValue).First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<PpeProvisionViewRow>();
        foreach (var employee in employees
                     .OrderBy(e => e.Department, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(e => e.Profession, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var profession = employee.Profession?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(profession))
            {
                continue;
            }

            var matched = standards
                .Where(s => ProfessionMatcher.Matches(profession, s.Profession) ||
                            string.Equals(profession, s.Profession, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matched.Count == 0)
            {
                continue;
            }

            var items = matched
                .SelectMany(s => s.Items.Select(item => (Standard: s, Item: item?.Trim() ?? string.Empty)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Item))
                .GroupBy(x => x.Item, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Item, StringComparer.CurrentCultureIgnoreCase);

            foreach (var item in items)
            {
                nomenclatureLookup.TryGetValue(item.Item, out var nom);
                var provisionKey = Key(organization, employee.Name, profession, item.Item);
                provisionLookup.TryGetValue(provisionKey, out var fact);
                var issueDate = fact?.IssueDate;
                var normText = ResolveNormText(nom);
                result.Add(new PpeProvisionViewRow(
                    organization,
                    string.IsNullOrWhiteSpace(employee.Department) ? "—" : employee.Department.Trim(),
                    profession,
                    employee.Name.Trim(),
                    item.Item,
                    normText,
                    string.IsNullOrWhiteSpace(fact?.IssuedQty) ? ResolveDefaultIssuedQty(normText) : fact!.IssuedQty.Trim(),
                    issueDate?.ToString("dd.MM.yyyy"),
                    ResolveNextReplacement(issueDate, normText),
                    ResolveLegalBasis(nom),
                    issueDate.HasValue ? "Выдано" : "Не выдано"));
            }
        }

        return result;
    }

    private static List<PpeProvisionTreeLine> BuildTree(IReadOnlyList<PpeProvisionViewRow> rows)
    {
        var lines = new List<PpeProvisionTreeLine>();
        foreach (var orgGroup in rows.GroupBy(r => r.Organization).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            lines.Add(PpeProvisionTreeLine.Group(0, orgGroup.Key));
            foreach (var depGroup in orgGroup.GroupBy(r => r.Department).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                lines.Add(PpeProvisionTreeLine.Group(1, depGroup.Key));
                foreach (var profGroup in depGroup.GroupBy(r => r.Profession).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                {
                    lines.Add(PpeProvisionTreeLine.Group(2, profGroup.Key));
                    foreach (var personGroup in profGroup.GroupBy(r => r.Employee).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
                    {
                        lines.Add(PpeProvisionTreeLine.Group(3, personGroup.Key));
                        lines.AddRange(personGroup
                            .OrderBy(r => r.PpeName, StringComparer.CurrentCultureIgnoreCase)
                            .Select(r => PpeProvisionTreeLine.Data(4, r)));
                    }
                }
            }
        }

        return lines;
    }

    private object FilterRoute() => new
    {
        reportDate = ReportDate.ToString("yyyy-MM-dd"),
        selectedOrganization = SelectedOrganization,
        department = Department,
        profession = Profession,
        ppeName = PpeName,
        statusFilter = StatusFilter
    };

    private static string ResolveNormText(PpeNomenclatureItem? item)
    {
        if (item is not null && !string.IsNullOrWhiteSpace(item.Norm) &&
            !string.Equals(item.Norm, "По норме", StringComparison.OrdinalIgnoreCase))
        {
            return item.Norm.Trim();
        }

        return "1 шт. на 12 мес";
    }

    private static string ResolveDefaultIssuedQty(string normText)
    {
        var text = normText.Trim();
        if (text.Contains("пар", StringComparison.OrdinalIgnoreCase))
        {
            var number = Regex.Match(text, @"\d+").Value;
            return string.IsNullOrWhiteSpace(number) ? "1 пара" : $"{number} пар";
        }

        return "1 шт.";
    }

    private static string ResolveLegalBasis(PpeNomenclatureItem? item)
    {
        if (item is not null && !string.IsNullOrWhiteSpace(item.NormParagraph))
        {
            return item.NormParagraph.Trim();
        }

        return DefaultLegalBasis;
    }

    private static string? ResolveNextReplacement(DateTime? issueDate, string normText)
    {
        if (!issueDate.HasValue)
        {
            return null;
        }

        var due = issueDate.Value.Date;
        var text = normText.ToLowerInvariant();
        var match = Regex.Match(text, @"(\d+)");
        var number = match.Success && int.TryParse(match.Value, out var parsed) ? parsed : 1;

        if (text.Contains("мес"))
        {
            due = due.AddMonths(number);
        }
        else if (text.Contains("2 год"))
        {
            due = due.AddYears(2);
        }
        else if (text.Contains("3 год"))
        {
            due = due.AddYears(3);
        }
        else
        {
            due = due.AddYears(1);
        }

        return due.ToString("dd.MM.yyyy");
    }

    private static string ValueAt(string[]? values, int index) =>
        values is not null && index < values.Length ? values[index] ?? string.Empty : string.Empty;

    private static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return iso.Date;
        }

        return DateTime.TryParse(text.Trim(), CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var any)
            ? any.Date
            : null;
    }

    private static List<string> DistinctSorted(IEnumerable<string> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v) && v != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string Key(string organization, string employee, string profession, string ppeName) =>
        $"{organization.Trim()}|{employee.Trim()}|{profession.Trim()}|{ppeName.Trim()}".ToLowerInvariant();

    public sealed record PpeProvisionViewRow(
        string Organization,
        string Department,
        string Profession,
        string Employee,
        string PpeName,
        string NormText,
        string IssuedQty,
        string? IssueDate,
        string? NextReplacementDate,
        string LegalBasis,
        string Status);

    public sealed record PpeProvisionTreeLine(int Level, bool IsGroup, string Label, PpeProvisionViewRow? Row)
    {
        public static PpeProvisionTreeLine Group(int level, string label) => new(level, true, label, null);
        public static PpeProvisionTreeLine Data(int level, PpeProvisionViewRow row) => new(level, false, row.PpeName, row);
    }
}

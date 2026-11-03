using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class ContingentListsModel : PageModel
{
    public const string TabContingents = "contingents";
    public const string TabNamed = "named";

    private readonly MedFactorStore _factors;
    private readonly MedStandardStore _standards;
    private readonly MedContingentOverridesStore _overrides;
    private readonly MedContextService _med;

    public ContingentListsModel(
        MedFactorStore factors,
        MedStandardStore standards,
        MedContingentOverridesStore overrides,
        MedContextService med)
    {
        _factors = factors;
        _standards = standards;
        _overrides = overrides;
        _med = med;
    }

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    [BindProperty]
    public List<ContingentEditRow> ContingentEdits { get; set; } = [];

    [BindProperty]
    public List<NamedEditRow> NamedEdits { get; set; } = [];

    [BindProperty]
    public List<string> NamedRemovedKeys { get; set; } = [];

    [BindProperty]
    public string? HintText { get; set; }

    [BindProperty]
    public string? HintStyle { get; set; }

    [BindProperty]
    public string? HintTab { get; set; }

    public string Organization { get; private set; } = string.Empty;
    public string ActiveTab { get; private set; } = TabContingents;
    public IReadOnlyList<MedContingentListRow> ContingentTableRows { get; private set; } = [];
    public MedContingentListRow ContingentTotals { get; private set; } = new();
    public IReadOnlyList<MedContingentNamedListRow> NamedListRows { get; private set; } = [];
    /// <summary>Листья справочника «Перечень факторов» — для выпадающего списка в кол. 988н.</summary>
    public IReadOnlyList<FactorPickOption> FactorOptions { get; private set; } = [];
    public int EmployeeCount { get; private set; }
    public string HintDisplayText { get; private set; } = string.Empty;
    public string HintDisplayStyle { get; private set; } = string.Empty;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet()
    {
        LoadPage();
    }

    public IActionResult OnPostSaveContingent()
    {
        Organization = _med.GetOrganization(HttpContext);
        var saved = (ContingentEdits ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Profession))
            .Select(e => new MedContingentPathologyOverride
            {
                Department = e.Department?.Trim() ?? string.Empty,
                Profession = e.Profession,
                EnvironmentFactors = e.EnvironmentFactors,
                Order988n = e.Order988n,
                Order29n = e.Order29n,
                ExamTotal = Clamp(e.ExamTotal),
                ExamWomen = Clamp(e.ExamWomen),
                LpuTotal = Clamp(e.LpuTotal),
                LpuWomen = Clamp(e.LpuWomen),
                PathologyTotal = Clamp(e.PathologyTotal),
                PathologyWomen = Clamp(e.PathologyWomen),
                AccidentTotal = Clamp(e.AccidentTotal),
                AccidentWomen = Clamp(e.AccidentWomen),
                ExceedLimitTotal = Clamp(e.ExceedLimitTotal),
                ExceedLimitWomen = Clamp(e.ExceedLimitWomen),
                OccupationalDiseaseTotal = Clamp(e.OccupationalDiseaseTotal),
                OccupationalDiseaseWomen = Clamp(e.OccupationalDiseaseWomen),
                SpecialConditionsTotal = Clamp(e.SpecialConditionsTotal),
                SpecialConditionsWomen = Clamp(e.SpecialConditionsWomen),
                Experience5YearsTotal = Clamp(e.Experience5YearsTotal),
                Experience5YearsWomen = Clamp(e.Experience5YearsWomen),
                Age40Total = Clamp(e.Age40Total),
                Age40Women = Clamp(e.Age40Women),
                MissingDataTotal = Clamp(e.MissingDataTotal),
                MissingDataWomen = Clamp(e.MissingDataWomen),
                CellStyles = ParseCellStyles(e.CellStylesJson)
            })
            .ToList();

        _overrides.SaveOrganization(Organization, saved);
        SaveHintForTab(TabContingents);
        TempData["Message"] = "Перечень контингентов сохранён.";
        return RedirectToPage(new { tab = TabContingents });
    }

    public IActionResult OnPostSaveHint()
    {
        Organization = _med.GetOrganization(HttpContext);
        var tab = NormalizeTab(HintTab);
        SaveHintForTab(tab);
        TempData["Message"] = "Текст подсказки сохранён.";
        return RedirectToPage(new { tab });
    }

    public IActionResult OnPostSaveNamed()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);

        var previous = MedContingentOverridesStore.IndexNamedByKey(_overrides.LoadNamed(Organization));
        var saved = new Dictionary<string, MedContingentNamedOverride>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in NamedRemovedKeys ?? [])
        {
            var k = (key ?? string.Empty).Trim();
            if (k.Length == 0)
            {
                continue;
            }

            saved[k] = new MedContingentNamedOverride { PersonKey = k, Removed = true };
        }

        // Ранее скрытые строки сохраняем, даже если не пришли в форме.
        foreach (var (key, ov) in previous)
        {
            if (ov.Removed && !saved.ContainsKey(key))
            {
                saved[key] = new MedContingentNamedOverride { PersonKey = key, Removed = true };
            }
        }

        // База без ручных правок — сохраняем только отличия (и удаления).
        var employees = _med.GetEmployees(Organization);
        var contingent = MedContingentListBuilder.Build(
            employees,
            _med.GetExams(Organization),
            _factors.LoadAll(),
            _overrides.Load(Organization),
            _standards.LoadByOrganization(Organization));
        var baseline = MedContingentNamedListBuilder.Build(
            contingent.Rows,
            employees,
            _med.GetExams(Organization));
        var baselineByKey = baseline
            .GroupBy(r => r.PersonKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var e in NamedEdits ?? [])
        {
            var key = (e.PersonKey ?? string.Empty).Trim();
            if (key.Length == 0 || saved.TryGetValue(key, out var existing) && existing.Removed)
            {
                continue;
            }

            var factors = string.IsNullOrWhiteSpace(e.HarmfulFactors)
                ? MedContingentListRow.DefaultFactors
                : e.HarmfulFactors.Trim();
            var code988 = string.IsNullOrWhiteSpace(e.Order988n)
                ? MedContingentListRow.DefaultOrder
                : e.Order988n.Trim();
            var code29 = string.IsNullOrWhiteSpace(e.Order29n)
                ? MedContingentListRow.DefaultOrder
                : e.Order29n.Trim();

            if (baselineByKey.TryGetValue(key, out var auto)
                && NamedRowEquals(auto, e, factors, code988, code29))
            {
                continue;
            }

            saved[key] = new MedContingentNamedOverride
            {
                PersonKey = key,
                Removed = false,
                FullName = e.FullName,
                BirthDate = e.BirthDate,
                Snils = e.Snils,
                Department = e.Department,
                Profession = e.Profession,
                ExperienceTotal = e.ExperienceTotal,
                ExperienceInOrg = null, // всегда от даты трудоустройства
                HarmfulFactors = factors,
                ActualLevel = e.ActualLevel,
                ConditionsClass = e.ConditionsClass,
                Order988n = code988,
                Order29n = code29,
                LastExamLpu = e.LastExamLpu,
                LastExamCpp = e.LastExamCpp
            };
        }

        _overrides.SaveNamed(Organization, saved.Values);
        SaveHintForTab(TabNamed);
        TempData["Message"] = "Поименный список сохранён.";
        return RedirectToPage(new { tab = TabNamed });
    }

    private static bool NamedRowEquals(
        MedContingentNamedListRow auto,
        NamedEditRow edit,
        string factors,
        string code988,
        string code29) =>
        Eq(auto.FullName, edit.FullName)
        && Eq(auto.BirthDate, edit.BirthDate)
        && Eq(auto.Snils, edit.Snils)
        && Eq(auto.Department, edit.Department)
        && Eq(auto.Profession, edit.Profession)
        && Eq(auto.ExperienceTotal, edit.ExperienceTotal)
        && Eq(auto.HarmfulFactors, factors)
        && Eq(auto.ActualLevel, edit.ActualLevel)
        && Eq(auto.ConditionsClass, edit.ConditionsClass)
        && Eq(auto.Order988n, code988)
        && Eq(auto.Order29n, code29)
        && Eq(auto.LastExamLpu, edit.LastExamLpu)
        && Eq(auto.LastExamCpp, edit.LastExamCpp);

    private static bool Eq(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.Ordinal);

    private void SaveHintForTab(string tab)
    {
        var hints = _overrides.LoadPageHints(Organization);
        var html = SanitizeHintHtml(HintText);
        if (tab == TabNamed)
        {
            hints.NamedHintHtml = html;
            hints.NamedHintStyle = string.Empty;
        }
        else
        {
            hints.ContingentsHintHtml = html;
            hints.ContingentsHintStyle = string.Empty;
        }

        _overrides.SavePageHints(Organization, hints);
    }

    private void LoadPage()
    {
        Organization = _med.GetOrganization(HttpContext);
        _med.EnsureInitialized(Organization);
        ActiveTab = NormalizeTab(Tab);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Списки контингентов";
        ViewData["Subtitle"] = Organization;

        var employees = _med.GetEmployees(Organization);
        EmployeeCount = employees.Count;

        if (TempData["Message"] is string tempMessage)
        {
            Message = tempMessage;
            IsError = TempData["IsError"] is true;
        }

        var allFactors = _factors.LoadAll();
        FactorOptions = BuildFactorOptions(allFactors);
        var built = MedContingentListBuilder.Build(
            employees,
            _med.GetExams(Organization),
            allFactors,
            _overrides.Load(Organization),
            _standards.LoadByOrganization(Organization));
        ContingentTableRows = built.Rows;
        ContingentTotals = built.Totals;

        if (ActiveTab == TabNamed)
        {
            NamedListRows = MedContingentNamedListBuilder.Build(
                ContingentTableRows,
                employees,
                _med.GetExams(Organization),
                _overrides.LoadNamed(Organization));
        }

        LoadHintDisplay();
    }

    private void LoadHintDisplay()
    {
        var hints = _overrides.LoadPageHints(Organization);
        if (ActiveTab == TabNamed)
        {
            HintDisplayStyle = string.Empty;
            HintDisplayText = SanitizeHintHtml(NormalizeStoredHint(hints.NamedHintHtml));
            return;
        }

        HintDisplayStyle = string.Empty;
        HintDisplayText = SanitizeHintHtml(NormalizeStoredHint(hints.ContingentsHintHtml));
    }

    /// <summary>Старую служебную подсказку не показываем — поле пустое, текст задаёт пользователь.</summary>
    private static string NormalizeStoredHint(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return string.Empty;
        }

        var text = stored.Trim();
        var plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ");
        plain = System.Net.WebUtility.HtmlDecode(plain).Trim();
        if (plain.StartsWith("Численность — из 1С", StringComparison.Ordinal)
            || plain.StartsWith("Список сотрудников по профессиям", StringComparison.Ordinal)
            || plain.Contains("в печать не попадает", StringComparison.OrdinalIgnoreCase)
            || plain.StartsWith("Можно вписать свой текст", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return text;
    }

    /// <summary>Разрешаем только безопасную разметку для цвета/жирного по выделению.</summary>
    private static string SanitizeHintHtml(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var html = raw.Trim();
        // Убрать скрипты и опасные теги целиком.
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"</?(script|iframe|object|embed|link|meta|style|form|input|button)[^>]*>",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"\son\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"javascript\s*:",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Оставляем только b/strong/span/br/font (execCommand foreColor часто даёт font color=).
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"</?(?!/?\s*(?:b|strong|span|br|font)\b)[a-z0-9:-]+(?:\s[^>]*)?>",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // В style оставляем только color и font-weight.
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"style\s*=\s*(""|')(?<style>.*?)\1",
            m =>
            {
                var style = m.Groups["style"].Value;
                var keep = new List<string>();
                foreach (var chunk in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var i = chunk.IndexOf(':');
                    if (i <= 0) continue;
                    var prop = chunk[..i].Trim().ToLowerInvariant();
                    var val = chunk[(i + 1)..].Trim();
                    if (prop is "color" && IsSafeCssColor(val))
                    {
                        keep.Add($"color: {val}");
                    }
                    else if (prop is "font-weight" && (val is "700" or "bold" or "600" or "800"))
                    {
                        keep.Add("font-weight: 700");
                    }
                }

                return keep.Count == 0 ? "" : $"style=\"{string.Join("; ", keep)}\"";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // font color="#..." → span style
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<font\s+[^>]*color\s*=\s*(""|')?(?<c>#[0-9a-fA-F]{3,8}|[a-zA-Z]+)(""|')?[^>]*>",
            m =>
            {
                var c = m.Groups["c"].Value;
                return IsSafeCssColor(c) ? $"<span style=\"color: {c}\">" : "<span>";
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"</font>",
            "</span>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return html.Trim();
    }

    private static string SanitizeStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var chunk in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = chunk.IndexOf(':');
            if (i <= 0)
            {
                continue;
            }

            var prop = chunk[..i].Trim().ToLowerInvariant();
            var val = chunk[(i + 1)..].Trim();
            if (prop is "font-weight" && (val is "700" or "bold"))
            {
                parts.Add("font-weight: 700");
            }
            else if (prop is "color" && IsSafeCssColor(val))
            {
                parts.Add($"color: {val}");
            }
        }

        return string.Join("; ", parts);
    }

    private static bool IsSafeCssColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 48)
        {
            return false;
        }

        value = value.Trim();
        if (value.StartsWith('#') && value.Length is 4 or 7 or 9)
        {
            return value.Skip(1).All(Uri.IsHexDigit);
        }

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
        {
            return value.All(c => char.IsLetterOrDigit(c) || "(),.% ".Contains(c));
        }

        return value.Equals("red", StringComparison.OrdinalIgnoreCase)
               || value.Equals("blue", StringComparison.OrdinalIgnoreCase)
               || value.Equals("green", StringComparison.OrdinalIgnoreCase)
               || value.Equals("brown", StringComparison.OrdinalIgnoreCase)
               || value.Equals("inherit", StringComparison.OrdinalIgnoreCase)
               || value.Equals("black", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeTab(string? tab) =>
        string.Equals(tab, TabNamed, StringComparison.OrdinalIgnoreCase)
            ? TabNamed
            : TabContingents;

    private static IReadOnlyList<FactorPickOption> BuildFactorOptions(IEnumerable<MedFactorNode> nodes) =>
        nodes
            .Where(n => !n.IsGroup)
            .Where(n => !string.IsNullOrWhiteSpace(n.ItemNumber) || !string.IsNullOrWhiteSpace(n.Name))
            .OrderBy(n => n.ItemNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(n =>
            {
                var num = (n.ItemNumber ?? string.Empty).Trim();
                var name = (n.Name ?? string.Empty).Trim();
                var displayNum = string.IsNullOrWhiteSpace(num) ? string.Empty : MedItemNumber.Display(num);
                // Полный текст — для title/подсказки; в <option> только короткий номер,
                // иначе системный выпадающий список Windows обрезает длинные строки.
                var label = string.IsNullOrWhiteSpace(displayNum)
                    ? name
                    : string.IsNullOrWhiteSpace(name) ? displayNum : $"{displayNum} — {name}";
                var shortLabel = !string.IsNullOrWhiteSpace(displayNum)
                    ? displayNum
                    : TruncateOption(name, 42);
                return new FactorPickOption(n.Id, num, name, label, shortLabel);
            })
            .ToList();

    private static string TruncateOption(string value, int max)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length <= max)
        {
            return t;
        }

        return t[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Подобрать значение select по сохранённым 988н / 29н / тексту фактора.</summary>
    public string? ResolveSelectedFactorId(string? code988, string? code29, string? factorName)
    {
        var a = (code988 ?? string.Empty).Trim();
        var b = (code29 ?? string.Empty).Trim();
        var name = (factorName ?? string.Empty).Trim();
        if (a is "-" or "—" or "–") a = string.Empty;
        if (b is "-" or "—" or "–") b = string.Empty;
        if (string.Equals(name, MedContingentListRow.DefaultFactors, StringComparison.OrdinalIgnoreCase))
        {
            name = string.Empty;
        }

        FactorPickOption? Match(Func<FactorPickOption, bool> pred) =>
            FactorOptions.FirstOrDefault(pred);

        // Сначала точный номер пункта (6.1 / п.6.1).
        var byCode = Match(o => !string.IsNullOrWhiteSpace(a)
                                && (string.Equals(o.Id, a, StringComparison.OrdinalIgnoreCase)
                                    || MedItemNumber.EqualsNormalized(o.ItemNumber, a)))
                     ?? Match(o => !string.IsNullOrWhiteSpace(b)
                                   && MedItemNumber.EqualsNormalized(o.ItemNumber, b));
        if (byCode is not null)
        {
            return byCode.Id;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Точное имя, иначе самое длинное вхождение (длинный текст из стандарта ↔ короткое имя в справочнике).
        var byName = FactorOptions
            .Where(o => !string.IsNullOrWhiteSpace(o.Name))
            .Select(o =>
            {
                var n = o.Name.Trim();
                var score = string.Equals(n, name, StringComparison.OrdinalIgnoreCase) ? 1_000_000
                    : name.Contains(n, StringComparison.OrdinalIgnoreCase) ? n.Length * 10
                    : n.Contains(name, StringComparison.OrdinalIgnoreCase) ? name.Length
                    : 0;
                return (Opt: o, Score: score);
            })
            .Where(x => x.Score > 0 && (x.Score >= 1_000_000 || x.Opt.Name.Trim().Length >= 6))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Opt)
            .FirstOrDefault();

        return byName?.Id;
    }

    private static int Clamp(int value) => value < 0 ? 0 : value;

    public sealed record FactorPickOption(
        string Id,
        string ItemNumber,
        string Name,
        string Label,
        string ShortLabel);

    private static Dictionary<string, string>? ParseCellStyles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null || map.Count == 0)
            {
                return null;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in map)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var k = key.Trim().ToLowerInvariant();
                if (k.Length < 2 || k[0] is not ('f' or 'a' or 'b') || !k.Skip(1).All(char.IsDigit))
                {
                    continue;
                }

                var css = SanitizeStyle(value);
                if (!string.IsNullOrWhiteSpace(css))
                {
                    result[k] = css;
                }
            }

            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    public sealed class NamedEditRow
    {
        public string PersonKey { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
        public string Snils { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Profession { get; set; } = string.Empty;
        public string ExperienceTotal { get; set; } = string.Empty;
        public string ExperienceInOrg { get; set; } = string.Empty;
        public string HarmfulFactors { get; set; } = string.Empty;
        public string ActualLevel { get; set; } = string.Empty;
        public string ConditionsClass { get; set; } = string.Empty;
        public string Order988n { get; set; } = string.Empty;
        public string Order29n { get; set; } = string.Empty;
        public string LastExamLpu { get; set; } = string.Empty;
        public string LastExamCpp { get; set; } = string.Empty;
    }

    public sealed class ContingentEditRow
    {
        public string Department { get; set; } = string.Empty;
        public string Profession { get; set; } = string.Empty;
        public string EnvironmentFactors { get; set; } = MedContingentListRow.DefaultFactors;
        public string Order988n { get; set; } = MedContingentListRow.DefaultOrder;
        public string Order29n { get; set; } = MedContingentListRow.DefaultOrder;
        public string? CellStylesJson { get; set; }
        public int WorkersTotal { get; set; }
        public int WorkersWomen { get; set; }
        public int ExamTotal { get; set; }
        public int ExamWomen { get; set; }
        public int LpuTotal { get; set; }
        public int LpuWomen { get; set; }
        public int PathologyTotal { get; set; }
        public int PathologyWomen { get; set; }
        public int AccidentTotal { get; set; }
        public int AccidentWomen { get; set; }
        public int ExceedLimitTotal { get; set; }
        public int ExceedLimitWomen { get; set; }
        public int OccupationalDiseaseTotal { get; set; }
        public int OccupationalDiseaseWomen { get; set; }
        public int SpecialConditionsTotal { get; set; }
        public int SpecialConditionsWomen { get; set; }
        public int Experience5YearsTotal { get; set; }
        public int Experience5YearsWomen { get; set; }
        public int Age40Total { get; set; }
        public int Age40Women { get; set; }
        public int MissingDataTotal { get; set; }
        public int MissingDataWomen { get; set; }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Zashita.Core.Employees;

namespace Zashita.Core.Training;

/// <summary>
/// Проставляет в отчёт по обучению (колонка «заметки») текст «Заявка N от dd.MM.yyyy»
/// по строке программы, на которую оформлена заявка.
/// </summary>
public static class TrainingApplicationCommentSync
{
    private static readonly Regex ManagedCommentRegex = new(
        @"^\s*заявк[аи]\s+.+\s+от\s+\d{1,2}\.\d{1,2}\.\d{2,4}\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string FormatComment(string? number, DateTime documentDate)
    {
        var n = FormatNumber(number);
        return $"Заявка {n} от {documentDate:dd.MM.yyyy}";
    }

    public static string FormatNumber(string? number)
    {
        var raw = (number ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return "—";
        }

        if (raw.StartsWith("№", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[1..].Trim();
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length > 0
            && long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n >= 0)
        {
            return n < 100
                ? n.ToString("00", CultureInfo.InvariantCulture)
                : n.ToString(CultureInfo.InvariantCulture);
        }

        return raw;
    }

    /// <summary>Наш автотекст или уже похожая заметка о заявке — можно обновлять/очищать.</summary>
    public static bool IsManagedOrApplicationComment(string? comment)
    {
        var c = (comment ?? string.Empty).Trim();
        if (c.Length == 0)
        {
            return true;
        }

        if (ManagedCommentRegex.IsMatch(c))
        {
            return true;
        }

        return TrainingConductValues.LooksLikeApplicationComment(c);
    }

    public static bool MatchesApplicationNumber(string? comment, string? number)
    {
        var c = (comment ?? string.Empty).Trim();
        var n = FormatNumber(number);
        if (c.Length == 0 || n is "—" or "")
        {
            return false;
        }

        return c.Contains(n, StringComparison.OrdinalIgnoreCase)
               && c.Contains("заявк", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Программы, на которые нужно повесить заметку: вид обучения, ProgramName участника,
    /// а также mapped program из справочника соответствий.
    /// </summary>
    public static IReadOnlyList<string> ResolveProgramsForRecord(
        TrainingPlanningRecord record,
        Func<string?, string>? resolveProgramForKind = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            var v = (value ?? string.Empty).Trim();
            if (v.Length > 0)
            {
                names.Add(v);
            }
        }

        Add(record.TrainingKind);
        if (resolveProgramForKind is not null)
        {
            Add(resolveProgramForKind(record.TrainingKind));
        }

        foreach (var participant in record.Participants ?? [])
        {
            Add(participant.ProgramName);
            if (resolveProgramForKind is not null)
            {
                Add(resolveProgramForKind(participant.ProgramName));
            }
        }

        // «Планирование: Программа» в содержании заявки.
        var content = (record.Content ?? string.Empty).Trim();
        const string prefix = "Планирование:";
        if (content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            Add(content[prefix.Length..].Trim().TrimEnd(':'));
        }

        return names.ToList();
    }

    /// <summary>
    /// После сохранения заявки: для каждого участника × программы проставить Comment.
    /// Не затирает произвольные пользовательские заметки.
    /// </summary>
    public static int ApplyOnSave(
        TrainingPlanningRecord record,
        List<TrainingConductRecord> conduct,
        Func<string?, string>? resolveProgramForKind = null)
    {
        var programs = ResolveProgramsForRecord(record, resolveProgramForKind);
        if (programs.Count == 0)
        {
            return 0;
        }

        var comment = FormatComment(record.Number, record.DocumentDate);
        var org = (record.Organization ?? string.Empty).Trim();
        var changed = 0;

        foreach (var participant in record.Participants ?? [])
        {
            var person = (participant.FullName ?? string.Empty).Trim();
            if (person.Length == 0)
            {
                continue;
            }

            var profession = (participant.Position ?? string.Empty).Trim();
            foreach (var program in programs)
            {
                if (UpsertComment(
                        conduct,
                        org,
                        record.Department,
                        profession,
                        person,
                        program,
                        comment))
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>При удалении заявки — убрать автозаметку с этим номером.</summary>
    public static int ApplyOnDelete(
        TrainingPlanningRecord record,
        List<TrainingConductRecord> conduct,
        Func<string?, string>? resolveProgramForKind = null)
    {
        var programs = ResolveProgramsForRecord(record, resolveProgramForKind);
        var org = (record.Organization ?? string.Empty).Trim();
        var changed = 0;

        foreach (var participant in record.Participants ?? [])
        {
            var person = (participant.FullName ?? string.Empty).Trim();
            if (person.Length == 0)
            {
                continue;
            }

            foreach (var program in programs)
            {
                foreach (var row in FindRows(conduct, org, person, program))
                {
                    if (!MatchesApplicationNumber(row.Comment, record.Number))
                    {
                        continue;
                    }

                    row.Comment = string.Empty;
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Подтянуть заметки из уже сохранённых заявок в список проведений
    /// (для старых заявок без синка и при открытии отчёта).
    /// </summary>
    public static int EnsureFromApplications(
        IEnumerable<TrainingPlanningRecord> applications,
        List<TrainingConductRecord> conduct,
        Func<string?, string>? resolveProgramForKind = null)
    {
        var changed = 0;
        foreach (var app in applications
                     .Where(a => !string.IsNullOrWhiteSpace(a.Number)
                                 || (a.Participants?.Any(p => !string.IsNullOrWhiteSpace(p.FullName)) ?? false))
                     .OrderBy(a => a.DocumentDate)
                     .ThenBy(a => a.Number, StringComparer.OrdinalIgnoreCase))
        {
            changed += ApplyOnSave(app, conduct, resolveProgramForKind);
        }

        return changed;
    }

    private static bool UpsertComment(
        List<TrainingConductRecord> conduct,
        string organization,
        string? department,
        string profession,
        string person,
        string program,
        string comment)
    {
        var existing = FindRows(conduct, organization, person, program)
            .OrderByDescending(r => r.TrainingDate ?? DateTime.MinValue)
            .FirstOrDefault();

        if (existing is not null)
        {
            if (!IsManagedOrApplicationComment(existing.Comment))
            {
                return false;
            }

            if (string.Equals(existing.Comment?.Trim(), comment, StringComparison.Ordinal))
            {
                return false;
            }

            existing.Comment = comment;
            if (string.IsNullOrWhiteSpace(existing.Result)
                || existing.Result.Contains("не проводилось", StringComparison.OrdinalIgnoreCase))
            {
                existing.Result = "Не проводилось";
            }

            return true;
        }

        conduct.Add(new TrainingConductRecord
        {
            Organization = organization,
            Department = (department ?? string.Empty).Trim(),
            Profession = profession,
            Person = person,
            Program = program,
            TrainingType = "Обучение",
            Result = "Не проводилось",
            TrainingDate = null,
            NextTrainingDate = null,
            Comment = comment
        });
        return true;
    }

    private static IEnumerable<TrainingConductRecord> FindRows(
        List<TrainingConductRecord> conduct,
        string organization,
        string person,
        string program) =>
        conduct.Where(r =>
            !r.IsArchivedSnapshot
            && string.Equals((r.Person ?? string.Empty).Trim(), person, StringComparison.OrdinalIgnoreCase)
            && string.Equals((r.Program ?? string.Empty).Trim(), program, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(organization)
                || string.IsNullOrWhiteSpace(r.Organization)
                || string.Equals(r.Organization.Trim(), organization, StringComparison.OrdinalIgnoreCase)
                || OrganizationMatcher.Matches(organization, r.Organization)));
}

namespace Zashita.Core.Training;

public static class TrainingConductLookup
{
    public static string ResolveCertificate(
        IEnumerable<TrainingConductRecord> records,
        string person,
        string program,
        DateTime? asOf = null)
    {
        var date = (asOf ?? DateTime.Today).Date;
        var best = records
            .Where(r => IsPassed(r.Result) || HasProtocolOrDate(r))
            .Where(r => PersonMatches(r.Person, person))
            .Where(r => ProgramMatches(r.Program, program))
            .Where(r => r.TrainingDate is null || r.TrainingDate.Value.Date <= date)
            .OrderByDescending(r => r.TrainingDate ?? DateTime.MinValue)
            .FirstOrDefault();

        return best is null ? string.Empty : FormatCertificate(FirstNonEmpty(best.ProtocolNumber, best.CertificateNumber));
    }

    public static string ResolveInstructorQualification(
        IEnumerable<TrainingConductRecord> records,
        string instructorName,
        DateTime briefingDate)
    {
        var firePrograms = new[]
        {
            "вводный противопожарный",
            "противопожарный",
            "пожарн",
            "пб",
            "проведение инструктаж",
            "инструктаж"
        };

        var best = records
            .Where(r => IsPassed(r.Result) || HasProtocolOrDate(r))
            .Where(r => PersonMatches(r.Person, instructorName))
            .Where(r => firePrograms.Any(p => r.Program.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Where(r => r.TrainingDate is null || r.TrainingDate.Value.Date <= briefingDate.Date)
            .OrderByDescending(r => r.TrainingDate ?? DateTime.MinValue)
            .FirstOrDefault();

        return best is null ? string.Empty : FormatCertificate(FirstNonEmpty(best.ProtocolNumber, best.CertificateNumber));
    }

    private static bool HasProtocolOrDate(TrainingConductRecord r) =>
        r.TrainingDate.HasValue
        || !string.IsNullOrWhiteSpace(r.ProtocolNumber)
        || !string.IsNullOrWhiteSpace(r.CertificateNumber);

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v.Trim();
            }
        }

        return string.Empty;
    }

    private static bool PersonMatches(string left, string right)
    {
        var a = NormalizePerson(left);
        var b = NormalizePerson(right);
        return a.Length > 0 && (a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.Contains(b, StringComparison.OrdinalIgnoreCase)
            || b.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePerson(string value) =>
        (value ?? string.Empty).Replace("(подрядчик)", "", StringComparison.OrdinalIgnoreCase).Trim();

    private static bool ProgramMatches(string left, string right)
    {
        var a = (left ?? string.Empty).Trim();
        var b = (right ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.Contains(b, StringComparison.OrdinalIgnoreCase)
            || b.Contains(a, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        static string Norm(string s) =>
            System.Text.RegularExpressions.Regex.Replace(
                    s.ToLowerInvariant().Replace('ё', 'е'),
                    @"\s+",
                    " ")
                .Trim();

        var na = Norm(a);
        var nb = Norm(b);

        // «Инструктажи по ПБ» ↔ «Проведение инструктажей по ПБ»
        static bool IsFireBriefingsFamily(string n) =>
            (n.Contains("инструктаж", StringComparison.Ordinal) || n.Contains("инструктажи", StringComparison.Ordinal))
            && (n.Contains("пб", StringComparison.Ordinal)
                || n.Contains("пожар", StringComparison.Ordinal)
                || n.Contains("противопожар", StringComparison.Ordinal))
            && !n.Contains("ответственн", StringComparison.Ordinal)
            && !n.Contains("вводн", StringComparison.Ordinal)
            && !n.Contains("первичн", StringComparison.Ordinal)
            && !n.Contains("повторн", StringComparison.Ordinal)
            && !n.Contains("специалист", StringComparison.Ordinal);

        if (IsFireBriefingsFamily(na) && IsFireBriefingsFamily(nb))
        {
            return true;
        }

        static bool IsFireResponsibleFamily(string n) =>
            n.Contains("ответственн", StringComparison.Ordinal)
            && (n.Contains("пб", StringComparison.Ordinal)
                || n.Contains("пожар", StringComparison.Ordinal)
                || n.Contains("обеспечен", StringComparison.Ordinal));

        if (IsFireResponsibleFamily(na) && IsFireResponsibleFamily(nb))
        {
            return true;
        }

        static bool IsFireSpecialistFamily(string n) =>
            n.Contains("специалист", StringComparison.Ordinal)
            && (n.Contains("профилактик", StringComparison.Ordinal)
                || n.Contains("специалист по пожар", StringComparison.Ordinal));

        return IsFireSpecialistFamily(na) && IsFireSpecialistFamily(nb);
    }

    private static bool IsPassed(string? result)
    {
        var r = (result ?? string.Empty).Trim();
        if (r.Length == 0)
        {
            return false;
        }

        return r.Contains("сдал", StringComparison.OrdinalIgnoreCase)
            || r.Contains("пройден", StringComparison.OrdinalIgnoreCase)
            || r.Equals("пройдено", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCertificate(string number)
    {
        var n = (number ?? string.Empty).Trim();
        if (n.Length == 0)
        {
            return string.Empty;
        }

        if (n.Contains("удостовер", StringComparison.OrdinalIgnoreCase))
        {
            return n;
        }

        return n.StartsWith('№') ? $"удостоверение {n}" : $"удостоверение № {n}";
    }
}

namespace Zashita.Core.Employees;

/// <summary>
/// Разделение юрлиц, которые в выгрузке 1С приходят под одним именем.
/// </summary>
public static class OrganizationLegalEntitySplit
{
    public const string KskOrganization = "ООО «КСК»";
    public const string StroyKarkasOrganization = "ООО «Компания Строй Каркас»";

    /// <summary>
    /// Сотрудники именно ООО КСК (по данным 1С «КСК ООО»): Елеонская, Винокуров, Шихов.
    /// Остальные из ключа ООО «КСК» относятся к ООО «Компания Строй Каркас».
    /// </summary>
    public static readonly HashSet<string> KskOnlySnils = new(StringComparer.Ordinal)
    {
        "01851194037",
        "07751483186",
        "12659662491"
    };

    public static IReadOnlyList<string> PreservedOrganizations { get; } =
        OrganizationCanonicalRegistry.CanonicalNames;

    private static readonly (string Left, string Right)[] KnownDistinctPairs = [];

    /// <summary>
    /// Явно разные юрлица — никогда не склеивать.
    /// Источник истины: <see cref="OrganizationCanonicalRegistry"/> (организации.xlsx)
    /// + жёсткие пары (КСК ↔ Строй Каркас, однофамильцы ИП).
    /// </summary>
    public static bool AreKnownDistinct(string left, string right) =>
        OrganizationCanonicalRegistry.AreDistinctLegalEntities(left, right);

    /// <summary>Только жёсткие пары без реестра (чтобы не зациклить Resolve).</summary>
    internal static bool AreKnownDistinctLegacy(string left, string right)
    {
        var a = (left ?? string.Empty).Trim();
        var b = (right ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        foreach (var (x, y) in KnownDistinctPairs)
        {
            if ((BelongsToPairSide(a, x) && BelongsToPairSide(b, y))
                || (BelongsToPairSide(a, y) && BelongsToPairSide(b, x)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BelongsToPairSide(string value, string canonical)
    {
        var nv = OrganizationMatcher.Normalize(value);
        var nc = OrganizationMatcher.Normalize(canonical);
        if (string.Equals(nv, nc, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (canonical.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
            && value.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
        {
            var valueCore = OrganizationMatcher.GetCoreName(value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var canonicalCore = OrganizationMatcher.GetCoreName(canonical)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (valueCore.Length < 2 || canonicalCore.Length < 2)
            {
                return false;
            }

            if (!valueCore[0].Equals(canonicalCore[0], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Имя: полное совпадение или первая буква (Александр ↔ А).
            var vf = valueCore[1];
            var cf = canonicalCore[1];
            if (vf.Equals(cf, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return vf.Length > 0 && cf.Length > 0
                   && char.ToUpperInvariant(vf[0]) == char.ToUpperInvariant(cf[0])
                   && (vf.Length == 1 || cf.Length == 1 || vf.StartsWith(cf, StringComparison.OrdinalIgnoreCase)
                       || cf.StartsWith(vf, StringComparison.OrdinalIgnoreCase));
        }

        var valueCoreName = OrganizationMatcher.GetCoreName(value);
        var canonicalCoreName = OrganizationMatcher.GetCoreName(canonical);
        if (string.Equals(canonicalCoreName, "кск", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(valueCoreName, "кск", StringComparison.OrdinalIgnoreCase);
        }

        if (canonicalCoreName.Contains("строй каркас", StringComparison.OrdinalIgnoreCase))
        {
            return valueCoreName.Contains("строй каркас", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(valueCoreName, canonicalCoreName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsKskOnlyEmployee(OrganizationEmployee employee)
    {
        var snils = OneCEmployeeListParser.NormalizeSnils(employee.Snils);
        if (!string.IsNullOrWhiteSpace(snils) && KskOnlySnils.Contains(snils))
        {
            return true;
        }

        var name = (employee.Name ?? string.Empty).Trim();
        return name.StartsWith("Елеонская", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Винокуров Сергей", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Шихов Александр", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsKskOnlyPerson(string? name, string? snils)
    {
        var snilsNorm = OneCEmployeeListParser.NormalizeSnils(snils);
        if (!string.IsNullOrWhiteSpace(snilsNorm) && KskOnlySnils.Contains(snilsNorm))
        {
            return true;
        }

        var n = (name ?? string.Empty).Trim();
        return n.StartsWith("Елеонская", StringComparison.OrdinalIgnoreCase)
               || n.StartsWith("Винокуров Сергей", StringComparison.OrdinalIgnoreCase)
               || n.StartsWith("Шихов Александр", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>«КСК ООО» в выгрузке 1С — реальное ООО «КСК».</summary>
    public static bool IsCsvRealKskLabel(string? value)
    {
        var text = (value ?? string.Empty).Trim().Trim('"').Trim();
        if (text.Length == 0)
        {
            return false;
        }

        // «КСК ООО» — форма ООО на конце, ядро КСК.
        if (text.EndsWith(" ООО", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("ООО", StringComparison.OrdinalIgnoreCase))
        {
            var body = text[..^4].Trim().Trim('"').Trim('«', '»', '"').Trim();
            return body.Equals("КСК", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>«ООО "КСК"» / «ООО КСК» в выгрузке — люди Компании Строй Каркас.</summary>
    public static bool IsCsvOooKskAsKarkasLabel(string? value)
    {
        var text = (value ?? string.Empty).Trim().Trim('"').Trim();
        if (text.Length == 0 || !text.StartsWith("ООО", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Канон «ООО «КСК»» (ёлочки) — уже разобранное ООО КСК, не ярлык Каркаса.
        if (text.Contains('«', StringComparison.Ordinal) || text.Contains('»', StringComparison.Ordinal))
        {
            return false;
        }

        var body = text[3..].Trim().Trim('"').Trim();
        return body.Equals("КСК", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Во всех ключах «КСК» оставляем только тройку (Елеонская / Винокуров / Шихов);
    /// остальных переносим в ООО «Компания Строй Каркас».
    /// </summary>
    public static bool SplitKskEmployees(
        IReadOnlyDictionary<string, List<OrganizationEmployee>> data,
        out Dictionary<string, List<OrganizationEmployee>> next)
    {
        var map = new Dictionary<string, List<OrganizationEmployee>>(data, StringComparer.OrdinalIgnoreCase);
        EnsureKey(map, KskOrganization);
        EnsureKey(map, StroyKarkasOrganization);

        var kskLikeKeys = map.Keys
            .Where(IsKskLikeOrganizationKey)
            .ToList();
        if (kskLikeKeys.Count == 0)
        {
            next = map;
            return false;
        }

        var source = kskLikeKeys.SelectMany(k => map[k]).ToList();
        var keep = source.Where(IsKskOnlyEmployee).ToList();
        var move = source.Where(e => !IsKskOnlyEmployee(e)).ToList();
        var onlyCanonical =
            kskLikeKeys.Count == 1
            && string.Equals(kskLikeKeys[0], KskOrganization, StringComparison.OrdinalIgnoreCase);
        if (move.Count == 0 && onlyCanonical)
        {
            next = map;
            return false;
        }

        foreach (var key in kskLikeKeys)
        {
            map.Remove(key);
        }

        map.TryGetValue(StroyKarkasOrganization, out var existingKarkas);
        map[KskOrganization] = DedupEmployees(keep);
        map[StroyKarkasOrganization] = DedupEmployees(
            existingKarkas is { Count: > 0 } ? existingKarkas.Concat(move) : move);
        next = map;
        return true;
    }

    private static bool IsKskLikeOrganizationKey(string organization)
    {
        if (OrganizationMatcher.Matches(organization, StroyKarkasOrganization)
            && !OrganizationMatcher.Matches(organization, KskOrganization))
        {
            return false;
        }

        return OrganizationMatcher.Matches(organization, KskOrganization)
               || string.Equals(
                   OrganizationMatcher.GetCoreName(organization),
                   "кск",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static List<OrganizationEmployee> DedupEmployees(IEnumerable<OrganizationEmployee> employees) =>
        employees
            .GroupBy(e => OneCStaffEligibility.PersonStableKey(e), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => EmployeeHrStatus.IsEmployed(e) ? 1 : 0).First())
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>
    /// Юрлицо из строки 1С → канон.
    /// «КСК ООО» → ООО «КСК»; «ООО "КСК"» → Строй Каркас.
    /// ИП: полное ФИО из CSV не сжимаем в инициалы («С.А.»).
    /// </summary>
    public static string ResolveCsvOrganization(string organization, string? name, string? snils)
    {
        var org = (organization ?? string.Empty).Trim();
        if (IsCsvRealKskLabel(org))
        {
            return KskOrganization;
        }

        if (IsCsvOooKskAsKarkasLabel(org))
        {
            return StroyKarkasOrganization;
        }

        if (OrganizationMatcher.Matches(org, StroyKarkasOrganization)
            && !OrganizationMatcher.Matches(org, KskOrganization))
        {
            return StroyKarkasOrganization;
        }

        // Уже канон «ООО «КСК»» (или схлопнутый ярлык): тройка остаётся в КСК, остальные → Каркас.
        if (OrganizationMatcher.Matches(org, KskOrganization)
            || string.Equals(
                OrganizationMatcher.GetCoreName(org),
                "кск",
                StringComparison.OrdinalIgnoreCase))
        {
            return IsKskOnlyPerson(name, snils) ? KskOrganization : StroyKarkasOrganization;
        }

        var canonical = OrganizationCanonicalRegistry.ResolveCanonicalName(org) ?? org;
        if (OrganizationMatcher.Matches(canonical, StroyKarkasOrganization)
            && !OrganizationMatcher.Matches(canonical, KskOrganization))
        {
            return StroyKarkasOrganization;
        }

        if (OrganizationMatcher.Matches(canonical, KskOrganization)
            || string.Equals(
                OrganizationMatcher.GetCoreName(canonical),
                "кск",
                StringComparison.OrdinalIgnoreCase))
        {
            return IsKskOnlyPerson(name, snils) ? KskOrganization : StroyKarkasOrganization;
        }

        // ИП из 1С приходит полным ФИО; в xlsx — инициалы. Ключ штата = как в CSV.
        if (org.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
            && OrganizationMatcher.IsIpInitialsForm(canonical)
            && !OrganizationMatcher.IsIpInitialsForm(org))
        {
            return org;
        }

        return canonical;
    }

    public static IEnumerable<string> EnsurePreservedOrganizations(IEnumerable<string> organizations)
    {
        var set = new HashSet<string>(
            organizations.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var org in PreservedOrganizations)
        {
            set.Add(org);
        }

        // Не оставлять рядом «ИП … О.С.» и «ИП … Оксана Сергеевна» — иначе пустая карточка.
        return OrganizationMatcher.DeduplicateOrganizations(set, PreservedOrganizations);
    }

    private static bool TryGetKey(
        Dictionary<string, List<OrganizationEmployee>> data,
        string preferred,
        out string? key)
    {
        key = data.Keys.FirstOrDefault(k =>
            string.Equals(k, preferred, StringComparison.OrdinalIgnoreCase)
            || OrganizationMatcher.MatchesForCatalog(k, preferred));
        return key is not null;
    }

    private static void EnsureKey(Dictionary<string, List<OrganizationEmployee>> data, string organization)
    {
        if (!data.Keys.Any(k => string.Equals(k, organization, StringComparison.OrdinalIgnoreCase)))
        {
            data[organization] = [];
        }
    }
}

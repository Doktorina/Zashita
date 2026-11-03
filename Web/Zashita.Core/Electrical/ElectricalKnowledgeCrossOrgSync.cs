using Zashita.Core.Employees;

namespace Zashita.Core.Electrical;

/// <summary>
/// Аттестация ЭБ общая для одного человека во всех его компаниях.
/// Ключ человека — СНИЛС (не ФИО): иначе однофамильцы попадали в чужие ИП/ООО.
/// В других компаниях, где человек есть в штате по СНИЛС, строка создаётся/обновляется.
/// </summary>
public static class ElectricalKnowledgeCrossOrgSync
{
    /// <summary>Место работы с СНИЛС (только реальная занятость из штата).</summary>
    public readonly record struct EmploymentSite(
        string Organization,
        string Position,
        string Snils,
        string EmployeeName);

    /// <summary>
    /// После сохранения: распространить аттестацию по СНИЛС на уже существующие строки
    /// того же человека в других организациях.
    /// </summary>
    public static int PropagateFromSaved(
        ElectricalKnowledgeCheckStore store,
        IEnumerable<ElectricalKnowledgeCheckRecord> savedRows,
        IReadOnlyDictionary<string, ElectricalKnowledgeCheckRecord>? previousById = null,
        IReadOnlyDictionary<string, IReadOnlyList<EmploymentSite>>? employmentByPersonKey = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var sources = new Dictionary<string, ElectricalKnowledgeCheckRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in (savedRows ?? [])
                     .Where(r => !string.IsNullOrWhiteSpace(PersonKey(r)))
                     .GroupBy(PersonKey, StringComparer.OrdinalIgnoreCase))
        {
            // Без СНИЛС кросс-орг. синк по ФИО запрещён — путает однофамильцев.
            if (!group.Key.StartsWith("snils:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eligible = group
                .Where(row => ShouldPropagateRow(row, previousById))
                .ToList();
            if (eligible.Count == 0)
            {
                continue;
            }

            var edited = eligible
                .Where(row =>
                    previousById is not null
                    && !string.IsNullOrWhiteSpace(row.Id)
                    && previousById.TryGetValue(row.Id, out var prev)
                    && CertificationDiffer(prev, row))
                .ToList();
            var candidates = edited.Count > 0 ? edited : eligible;

            var canonical = PickCanonical(candidates);
            if (canonical is not null)
            {
                sources[group.Key] = canonical;
            }
        }

        if (sources.Count == 0)
        {
            return 0;
        }

        var touched = 0;
        store.MutateAll(all =>
        {
            foreach (var (personKey, canonical) in sources)
            {
                IReadOnlyList<EmploymentSite>? sites = null;
                if (employmentByPersonKey is not null
                    && employmentByPersonKey.TryGetValue(personKey, out var found))
                {
                    sites = found;
                }

                touched += ApplyCanonical(all, personKey, canonical, sites);
            }

            return touched > 0;
        });

        return touched;
    }

    private static bool ShouldPropagateRow(
        ElectricalKnowledgeCheckRecord row,
        IReadOnlyDictionary<string, ElectricalKnowledgeCheckRecord>? previousById)
    {
        if (HasCertificationData(row))
        {
            return true;
        }

        if (previousById is null
            || string.IsNullOrWhiteSpace(row.Id)
            || !previousById.TryGetValue(row.Id, out var prev))
        {
            return false;
        }

        return HasCertificationData(prev) && CertificationDiffer(prev, row);
    }

    public static bool CertificationDiffer(
        ElectricalKnowledgeCheckRecord a,
        ElectricalKnowledgeCheckRecord b) =>
        !string.Equals(
            ElectricalKnowledgeCalculator.NormalizeGroup(a.CurrentGroup),
            ElectricalKnowledgeCalculator.NormalizeGroup(b.CurrentGroup),
            StringComparison.OrdinalIgnoreCase)
        || a.CurrentGroupAssignedDate?.Date != b.CurrentGroupAssignedDate?.Date
        || !string.Equals(
            (a.LastCheckResult ?? string.Empty).Trim(),
            (b.LastCheckResult ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase)
        || !string.Equals(
            ElectricalKnowledgeCalculator.NormalizeEducation(a.Education),
            ElectricalKnowledgeCalculator.NormalizeEducation(b.Education),
            StringComparison.Ordinal)
        || a.PeriodicityMonths != b.PeriodicityMonths;

    /// <summary>
    /// Выровнять строки одного СНИЛС и при необходимости создать строки
    /// в других компаниях из индекса занятости (только по СНИЛС).
    /// </summary>
    public static int ReconcileExisting(
        ElectricalKnowledgeCheckStore store,
        IReadOnlyDictionary<string, IReadOnlyList<EmploymentSite>>? employmentByPersonKey = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var touched = 0;
        store.MutateAll(all =>
        {
            foreach (var group in all
                         .Where(r => PersonKey(r).StartsWith("snils:", StringComparison.OrdinalIgnoreCase))
                         .GroupBy(PersonKey, StringComparer.OrdinalIgnoreCase))
            {
                var canonical = PickCanonical(group.Where(HasCertificationData));
                if (canonical is null)
                {
                    canonical = PickCanonical(group);
                }

                if (canonical is null || !HasCertificationData(canonical))
                {
                    continue;
                }

                IReadOnlyList<EmploymentSite>? sites = null;
                if (employmentByPersonKey is not null
                    && employmentByPersonKey.TryGetValue(group.Key, out var found))
                {
                    sites = found;
                }

                // Без мест работы — только обновляем уже существующие строки (как раньше).
                if (sites is null && group.Count() < 2)
                {
                    continue;
                }

                touched += ApplyCanonical(all, group.Key, canonical, sites);
            }

            return touched > 0;
        });

        return touched;
    }

    /// <summary>
    /// В одной организации — один человек один раз (по СНИЛС, иначе по ФИО).
    /// Оставляет самую заполненную/свежую строку; остальные удаляет.
    /// </summary>
    public static int DeduplicateWithinOrganizations(ElectricalKnowledgeCheckStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var removed = 0;
        store.MutateAll(all =>
        {
            if (all.Count < 2)
            {
                return false;
            }

            var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dropIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Сначала точное совпадение названия орг., затем склейка алиасов юрлица.
            foreach (var orgGroup in all
                         .GroupBy(r => (r.Organization ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            {
                removed += MarkDuplicatesInOrgGroup(orgGroup.ToList(), keepIds, dropIds);
            }

            // Алиасы одной компании («ВИКТОРИЯ СТРОЙ» / с кавычками и т.п.) — тоже один человек.
            var remaining = all.Where(r => !dropIds.Contains(r.Id)).ToList();
            removed += MarkDuplicatesAcrossOrgAliases(remaining, keepIds, dropIds);

            if (dropIds.Count == 0)
            {
                return false;
            }

            all.RemoveAll(r => dropIds.Contains(r.Id));
            return true;
        });

        return removed;
    }

    private static int MarkDuplicatesInOrgGroup(
        List<ElectricalKnowledgeCheckRecord> rows,
        HashSet<string> keepIds,
        HashSet<string> dropIds)
    {
        var removed = 0;
        foreach (var personGroup in rows.GroupBy(PersonDedupeKey, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(personGroup.Key) || personGroup.Count() < 2)
            {
                continue;
            }

            var ordered = personGroup
                .OrderByDescending(r => HasCertificationData(r))
                .ThenByDescending(TrainingFillScore)
                .ThenByDescending(r => r.ModifiedAt)
                .ThenByDescending(r => r.CurrentGroupAssignedDate ?? DateTime.MinValue)
                .ToList();
            var winner = ordered[0];
            keepIds.Add(winner.Id);
            for (var i = 1; i < ordered.Count; i++)
            {
                if (dropIds.Add(ordered[i].Id))
                {
                    removed++;
                }
            }
        }

        return removed;
    }

    private static int MarkDuplicatesAcrossOrgAliases(
        List<ElectricalKnowledgeCheckRecord> rows,
        HashSet<string> keepIds,
        HashSet<string> dropIds)
    {
        var removed = 0;
        // Группы по СНИЛС / ФИО без учёта орг.; внутри — только если орг. — алиасы одной компании.
        foreach (var personGroup in rows
                     .Where(r => !string.IsNullOrWhiteSpace(PersonIdentityKey(r)))
                     .GroupBy(PersonIdentityKey, StringComparer.OrdinalIgnoreCase))
        {
            var list = personGroup.ToList();
            if (list.Count < 2)
            {
                continue;
            }

            var clusters = new List<List<ElectricalKnowledgeCheckRecord>>();
            foreach (var row in list)
            {
                var placed = false;
                foreach (var cluster in clusters)
                {
                    if (cluster.Any(x => SameOrganizationStrict(x.Organization ?? string.Empty, row.Organization)))
                    {
                        cluster.Add(row);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    clusters.Add([row]);
                }
            }

            foreach (var cluster in clusters.Where(c => c.Count > 1))
            {
                removed += MarkDuplicatesInOrgGroup(cluster, keepIds, dropIds);
            }
        }

        return removed;
    }

    /// <summary>Ключ дедупа внутри одной строки названия орг.</summary>
    private static string PersonDedupeKey(ElectricalKnowledgeCheckRecord row)
    {
        var snils = NormalizeSnils(row.Snils);
        if (snils.Length == 11)
        {
            return "snils:" + snils;
        }

        var name = (row.EmployeeName ?? string.Empty).Trim().ToLowerInvariant();
        return name.Length == 0 ? string.Empty : "name:" + name;
    }

    /// <summary>Идентичность человека без организации (для склейки алиасов юрлица).</summary>
    private static string PersonIdentityKey(ElectricalKnowledgeCheckRecord row)
    {
        var snils = NormalizeSnils(row.Snils);
        if (snils.Length == 11)
        {
            return "snils:" + snils;
        }

        var name = (row.EmployeeName ?? string.Empty).Trim().ToLowerInvariant();
        return name.Length == 0 ? string.Empty : "name:" + name;
    }

    /// <summary>
    /// Убрать артефакты старого синка по ФИО: строка в орг., где человека нет в штате,
    /// но то же ФИО есть в штате другой компании.
    /// </summary>
    public static int PurgeNameCollisionArtifacts(
        ElectricalKnowledgeCheckStore store,
        IReadOnlyDictionary<string, HashSet<string>> employedNamesByOrg)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (employedNamesByOrg.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        store.MutateAll(all =>
        {
            var keep = new List<ElectricalKnowledgeCheckRecord>(all.Count);
            foreach (var row in all)
            {
                var org = (row.Organization ?? string.Empty).Trim();
                var name = (row.EmployeeName ?? string.Empty).Trim();
                if (org.Length == 0 || name.Length == 0)
                {
                    keep.Add(row);
                    continue;
                }

                var inThisOrg = OrgHasName(employedNamesByOrg, org, name);
                if (inThisOrg)
                {
                    keep.Add(row);
                    continue;
                }

                // Заполненную аттестацию / ручные строки никогда не удаляем —
                // иначе ввод в реестре ЭБ пропадал при открытии страницы.
                if (row.AddedManually || HasCertificationData(row)
                    || !string.IsNullOrWhiteSpace(row.ResponsibleNote)
                    || row.CurrentGroupAssignedDate.HasValue)
                {
                    keep.Add(row);
                    continue;
                }

                // Есть СНИЛС и человек не в штате этой орг. — оставляем (архив / ручная запись).
                if (!string.IsNullOrWhiteSpace(NormalizeSnils(row.Snils)))
                {
                    keep.Add(row);
                    continue;
                }

                // Пустая строка: ФИО есть в другой компании, а в этой нет — артефакт старого синка по имени.
                var inOtherOrg = employedNamesByOrg.Any(kv =>
                    !OrganizationMatcher.Matches(kv.Key, org)
                    && kv.Value.Contains(name));
                if (inOtherOrg)
                {
                    removed++;
                    continue;
                }

                keep.Add(row);
            }

            if (removed == 0)
            {
                return false;
            }

            all.Clear();
            all.AddRange(keep);
            return true;
        });

        return removed;
    }

    public static int ReapplySavedCertification(
        ElectricalKnowledgeCheckStore store,
        IEnumerable<ElectricalKnowledgeCheckRecord> savedRows)
    {
        ArgumentNullException.ThrowIfNull(store);
        var byId = (savedRows ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (byId.Count == 0)
        {
            return 0;
        }

        var touched = 0;
        store.MutateAll(all =>
        {
            foreach (var row in all)
            {
                if (!byId.TryGetValue(row.Id, out var saved))
                {
                    continue;
                }

                if (CopyCertification(saved, row) || CopyIdentity(saved, row))
                {
                    row.ModifiedAt = DateTime.UtcNow;
                    touched++;
                }
            }

            return touched > 0;
        });

        return touched;
    }

    /// <summary>Подтянуть аттестацию с других орг. того же СНИЛС (не по ФИО).</summary>
    public static bool TryFillFromPeers(
        ElectricalKnowledgeCheckStore store,
        ElectricalKnowledgeCheckRecord target)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(target);
        var key = PersonKey(target);
        if (!key.StartsWith("snils:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var peer = PickCanonical(
            store.LoadAll()
                .Where(r =>
                    !string.Equals(r.Id, target.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(PersonKey(r), key, StringComparison.OrdinalIgnoreCase)
                    && HasCertificationData(r)));
        if (peer is null)
        {
            return false;
        }

        return CopyCertification(peer, target);
    }

    public static string PersonKey(ElectricalKnowledgeCheckRecord row)
    {
        var snils = NormalizeSnils(row.Snils);
        if (snils.Length == 11)
        {
            return "snils:" + snils;
        }

        var name = (row.EmployeeName ?? string.Empty).Trim().ToLowerInvariant();
        var org = (row.Organization ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(name) ? string.Empty : $"name:{org}|{name}";
    }

    public static string NormalizeSnils(string? snils) =>
        OneCEmployeeListParser.NormalizeSnils(snils ?? string.Empty);

    /// <summary>
    /// Обновить строки с тем же СНИЛС; при наличии мест работы из штата —
    /// создать недостающие строки в других компаниях (только по СНИЛС, не по ФИО).
    /// </summary>
    private static int ApplyCanonical(
        List<ElectricalKnowledgeCheckRecord> all,
        string personKey,
        ElectricalKnowledgeCheckRecord canonical,
        IReadOnlyList<EmploymentSite>? employmentSites)
    {
        var touched = 0;
        var snils = NormalizeSnils(canonical.Snils);
        if (snils.Length != 11 || !personKey.StartsWith("snils:", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        foreach (var row in all.Where(r =>
                     string.Equals(PersonKey(r), personKey, StringComparison.OrdinalIgnoreCase)))
        {
            var certChanged = CopyCertification(canonical, row);
            var idChanged = CopyIdentity(canonical, row);
            if (string.IsNullOrWhiteSpace(NormalizeSnils(row.Snils)))
            {
                row.Snils = snils;
                idChanged = true;
            }

            if (certChanged || idChanged)
            {
                row.ModifiedAt = DateTime.UtcNow;
                touched++;
            }
        }

        if (employmentSites is null || employmentSites.Count == 0)
        {
            return touched;
        }

        foreach (var site in employmentSites)
        {
            var org = (site.Organization ?? string.Empty).Trim();
            if (org.Length == 0)
            {
                continue;
            }

            // Только точное/каноническое совпадение юрлица — без Matches(..., "") = true.
            var already = all.Any(r =>
                string.Equals(PersonKey(r), personKey, StringComparison.OrdinalIgnoreCase)
                && SameOrganizationStrict(org, r.Organization));
            if (already)
            {
                continue;
            }

            var name = (site.EmployeeName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                name = (canonical.EmployeeName ?? string.Empty).Trim();
            }

            if (name.Length == 0)
            {
                continue;
            }

            var created = new ElectricalKnowledgeCheckRecord
            {
                Organization = org,
                EmployeeName = name,
                Snils = snils,
                Position = (site.Position ?? string.Empty).Trim(),
                AddedManually = true,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };
            CopyCertification(canonical, created);
            all.Add(created);
            touched++;
        }

        return touched;
    }

    /// <summary>
    /// Убрать из реестра ЭБ строки людей, которых нет в штате этой организации
    /// (по СНИЛС, иначе по ФИО). Аттестация остаётся в компаниях, где человек реально работает.
    /// </summary>
    public static int PurgeNotEmployedInOrganization(
        ElectricalKnowledgeCheckStore store,
        IReadOnlyDictionary<string, HashSet<string>> employedNamesByOrg,
        IReadOnlyDictionary<string, HashSet<string>> employedSnilsByOrg)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (employedNamesByOrg.Count == 0 && employedSnilsByOrg.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        store.MutateAll(all =>
        {
            var keep = new List<ElectricalKnowledgeCheckRecord>(all.Count);
            foreach (var row in all)
            {
                var org = (row.Organization ?? string.Empty).Trim();
                var name = (row.EmployeeName ?? string.Empty).Trim();
                if (org.Length == 0 || name.Length == 0)
                {
                    keep.Add(row);
                    continue;
                }

                var snils = NormalizeSnils(row.Snils);
                if (snils.Length == 11)
                {
                    if (OrgHasSnils(employedSnilsByOrg, org, snils))
                    {
                        keep.Add(row);
                        continue;
                    }

                    // СНИЛС есть, но в штате этой орг. такого СНИЛС нет — чужая строка.
                    removed++;
                    continue;
                }

                if (OrgHasName(employedNamesByOrg, org, name))
                {
                    keep.Add(row);
                    continue;
                }

                removed++;
            }

            if (removed == 0)
            {
                return false;
            }

            all.Clear();
            all.AddRange(keep);
            return true;
        });

        return removed;
    }

    private static bool SameOrganizationStrict(string left, string? right)
    {
        var a = (left ?? string.Empty).Trim();
        var b = (right ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
               || OrganizationMatcher.MatchesForCatalog(a, b);
    }

    private static bool OrgHasSnils(
        IReadOnlyDictionary<string, HashSet<string>> employedSnilsByOrg,
        string org,
        string snils)
    {
        foreach (var (key, set) in employedSnilsByOrg)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (SameOrganizationStrict(org, key) && set.Contains(snils))
            {
                return true;
            }
        }

        return false;
    }

    private static bool OrgHasName(
        IReadOnlyDictionary<string, HashSet<string>> employedNamesByOrg,
        string org,
        string name)
    {
        foreach (var (key, names) in employedNamesByOrg)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (SameOrganizationStrict(org, key) && names.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStrongLocalNote(ElectricalKnowledgeCheckRecord row) =>
        !string.IsNullOrWhiteSpace(row.ResponsibleNote)
        && row.ResponsibleNote.Trim().Length > 2;

    private static ElectricalKnowledgeCheckRecord? PickCanonical(
        IEnumerable<ElectricalKnowledgeCheckRecord> rows) =>
        rows
            .OrderByDescending(r => r.ModifiedAt)
            .ThenByDescending(TrainingFillScore)
            .ThenByDescending(r => r.CurrentGroupAssignedDate ?? DateTime.MinValue)
            .ThenByDescending(GroupRank)
            .FirstOrDefault();

    private static int GroupRank(ElectricalKnowledgeCheckRecord row) =>
        ElectricalKnowledgeCalculator.NormalizeGroup(row.CurrentGroup) switch
        {
            ElectricalGroups.V => 5,
            ElectricalGroups.IV => 4,
            ElectricalGroups.III => 3,
            ElectricalGroups.II => 2,
            ElectricalGroups.I => 1,
            _ => 0
        };

    private static bool CopyIdentity(
        ElectricalKnowledgeCheckRecord source,
        ElectricalKnowledgeCheckRecord target)
    {
        var snils = NormalizeSnils(source.Snils);
        if (snils.Length != 11)
        {
            return false;
        }

        if (string.Equals(NormalizeSnils(target.Snils), snils, StringComparison.Ordinal))
        {
            return false;
        }

        target.Snils = snils;
        return true;
    }

    private static bool CopyCertification(
        ElectricalKnowledgeCheckRecord source,
        ElectricalKnowledgeCheckRecord target)
    {
        var group = ElectricalKnowledgeCalculator.NormalizeGroup(source.CurrentGroup);
        var education = ElectricalKnowledgeCalculator.NormalizeEducation(source.Education);
        var result = (source.LastCheckResult ?? string.Empty).Trim();
        var assigned = source.CurrentGroupAssignedDate?.Date;
        int? period = source.PeriodicityMonths is 12 or 36 ? source.PeriodicityMonths : null;

        var changed = false;
        if (!string.Equals(target.Education ?? string.Empty, education, StringComparison.Ordinal))
        {
            target.Education = education;
            changed = true;
        }

        if (!string.Equals(
                ElectricalKnowledgeCalculator.NormalizeGroup(target.CurrentGroup),
                group,
                StringComparison.OrdinalIgnoreCase))
        {
            target.CurrentGroup = group;
            changed = true;
        }

        if (target.CurrentGroupAssignedDate?.Date != assigned)
        {
            target.CurrentGroupAssignedDate = assigned;
            changed = true;
        }

        if (!string.Equals(target.LastCheckResult ?? string.Empty, result, StringComparison.OrdinalIgnoreCase))
        {
            target.LastCheckResult = result;
            changed = true;
        }

        if (target.PeriodicityMonths != period)
        {
            target.PeriodicityMonths = period;
            changed = true;
        }

        return changed;
    }

    private static bool HasCertificationData(ElectricalKnowledgeCheckRecord row) =>
        !string.IsNullOrWhiteSpace(row.CurrentGroup)
        || row.CurrentGroupAssignedDate.HasValue
        || !string.IsNullOrWhiteSpace(row.LastCheckResult)
        || !string.IsNullOrWhiteSpace(row.Education);

    private static int TrainingFillScore(ElectricalKnowledgeCheckRecord row)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(row.CurrentGroup))
        {
            score += 4;
        }

        if (row.CurrentGroupAssignedDate.HasValue)
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(row.LastCheckResult))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(row.Education))
        {
            score += 1;
        }

        if (row.PeriodicityMonths is 12 or 36)
        {
            score += 1;
        }

        return score;
    }
}

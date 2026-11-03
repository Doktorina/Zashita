using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Core.Risks;
using Zashita.Core.Training;

namespace Zashita.Web.Services;

/// <summary>Открытые приёмы/переводы с незакрытыми пробелами по модулям.</summary>
public sealed class StaffingOnboardingQuery
{
    private readonly StaffingChangeStore _staffingChanges;
    private readonly OrganizationEmployeesService _employees;
    private readonly TrainingConductStore _trainingConduct;
    private readonly MedExamStore _medExams;
    private readonly WorkplaceStore _workplaces;
    private readonly RiskWorkplaceStore _riskWorkplaces;
    private readonly object _cacheSync = new();
    private string _cacheKey = string.Empty;
    private DateTime _cacheUtc = DateTime.MinValue;
    private IReadOnlyList<StaffingOnboardingItem> _cacheItems = [];

    public StaffingOnboardingQuery(
        StaffingChangeStore staffingChanges,
        OrganizationEmployeesService employees,
        TrainingConductStore trainingConduct,
        MedExamStore medExams,
        WorkplaceStore workplaces,
        RiskWorkplaceStore riskWorkplaces)
    {
        _staffingChanges = staffingChanges;
        _employees = employees;
        _trainingConduct = trainingConduct;
        _medExams = medExams;
        _workplaces = workplaces;
        _riskWorkplaces = riskWorkplaces;
    }

    public void InvalidateCache()
    {
        lock (_cacheSync)
        {
            _cacheKey = string.Empty;
            _cacheItems = [];
            _cacheUtc = DateTime.MinValue;
        }
    }

    public IReadOnlyList<StaffingOnboardingItem> LoadOpen(
        string? organization,
        string? moduleKey = null,
        int take = 20)
    {
        var org = (organization ?? string.Empty).Trim();
        var module = (moduleKey ?? string.Empty).Trim();
        var cacheKey = $"{org}|{module}|{take}";

        lock (_cacheSync)
        {
            if (string.Equals(_cacheKey, cacheKey, StringComparison.Ordinal)
                && DateTime.UtcNow - _cacheUtc < TimeSpan.FromSeconds(20))
            {
                return _cacheItems;
            }
        }

        var items = BuildOpen(org, module, take);
        lock (_cacheSync)
        {
            _cacheKey = cacheKey;
            _cacheUtc = DateTime.UtcNow;
            _cacheItems = items;
        }

        return items;
    }

    private IReadOnlyList<StaffingOnboardingItem> BuildOpen(string org, string moduleKey, int take)
    {
        var key = moduleKey.Trim().ToLowerInvariant();
        var needTraining = key.Length == 0 || key is "обучение" or "training";
        var needMed = key.Length == 0 || key is "медосмотры" or "med";
        var needSout = key.Length == 0 || key is "специальная-оценка" or "спецоценка" or "sout";
        var needRisks = key.Length == 0 || key is "риски" or "risks";

        IReadOnlyList<TrainingConductRecord> conductsAll = needTraining
            ? _trainingConduct.LoadAll()
            : [];
        IReadOnlyList<MedExamRecord> examsAll = needMed
            ? (string.IsNullOrWhiteSpace(org) ? _medExams.LoadAll() : _medExams.LoadByOrganization(org))
            : [];
        // СОУТ и риски — разные перечни РМ, нельзя подменять друг другом.
        IReadOnlyList<WorkplaceRecord> soutWorkplaces = needSout
            ? (string.IsNullOrWhiteSpace(org) ? _workplaces.LoadAll() : _workplaces.LoadByOrganization(org))
            : [];
        IReadOnlyList<WorkplaceRecord> riskWorkplaces = needRisks
            ? (string.IsNullOrWhiteSpace(org) ? _riskWorkplaces.LoadAll() : _riskWorkplaces.LoadByOrganization(org))
            : [];

        var rosterCache = new Dictionary<string, IReadOnlyList<OrganizationEmployee>>(StringComparer.OrdinalIgnoreCase);

        var raw = _staffingChanges.LoadRecent(120, onlyOpen: false)
            .Where(e => e.Kind is StaffingChangeKind.Hired or StaffingChangeKind.Transferred)
            .Where(e => string.IsNullOrWhiteSpace(org)
                        || OrganizationMatcher.Matches(org, e.Organization)
                        || string.Equals(e.Organization, org, StringComparison.OrdinalIgnoreCase))
            .Where(e => IsCurrentlyEmployed(org, e, rosterCache))
            .OrderByDescending(e => e.OccurredAt)
            .ToList();

        var seenPeople = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<StaffingOnboardingItem>();
        foreach (var alert in raw)
        {
            var personKey = $"{(alert.Organization ?? "").Trim()}\u001e{(alert.Person ?? "").Trim()}";
            if (!seenPeople.Add(personKey))
            {
                continue;
            }

            var rosterOrg = string.IsNullOrWhiteSpace(org)
                ? (alert.Organization ?? string.Empty).Trim()
                : org;
            rosterCache.TryGetValue(rosterOrg, out var roster);
            var profession = ResolveProfession(alert, roster);

            // На вкладке модуля не считаем чужие пробелы «пустыми» из‑за незагруженных данных:
            // передаём пустые списки только для ненужных модулей — Evaluate тогда оставит gap=false? 
            // Нет: пустой список → hasX=false → gap=true. Поэтому для чужих модулей
            // подставляем «уже заполнено», передавая фиктивные заполненные данные нельзя.
            // Правильно: Evaluate всегда со всеми данными на главной; на модуле — только фильтр NeedsModule,
            // а для незагруженных модулей gap по ним не должен отсекать. NeedsModule смотрит только свой флаг.
            // Если needTraining=false, conducts=[], hasTraining=false, Training gap=true — NeedsModule("риски")
            // ignores Training. OK!
            // If needSout=false, sout=[], hasSout=false, Sout=true — NeedsModule("обучение") ignores Sout. OK!

            var gap = StaffingOnboarding.Evaluate(
                alert.Organization,
                alert.Person,
                profession,
                conductsAll,
                examsAll,
                soutWorkplaces,
                riskWorkplaces,
                alert.Snils);

            // На странице модуля «обнуляем» пробелы чужих разделов, чтобы IsComplete/фильтр были честными
            // только по текущему модулю (остальные считаем закрытыми для этой карточки).
            if (key.Length > 0)
            {
                gap = NarrowGapToModule(gap, key);
            }

            if (gap.IsComplete)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(moduleKey) && !gap.NeedsModule(moduleKey))
            {
                continue;
            }

            items.Add(new StaffingOnboardingItem(
                alert,
                gap,
                StaffingOnboarding.ResolveDisplayDate(alert, roster)));
            if (items.Count >= take)
            {
                break;
            }
        }

        return items;
    }

    /// <summary>
    /// На карточке раздела показываем только пробел этого раздела;
    /// остальные модули не влияют на исчезновение строки здесь.
    /// </summary>
    private static StaffingOnboarding.Gap NarrowGapToModule(StaffingOnboarding.Gap gap, string moduleKey) =>
        moduleKey switch
        {
            "обучение" or "training" => new StaffingOnboarding.Gap(gap.Training, false, false, false),
            "медосмотры" or "med" => new StaffingOnboarding.Gap(false, gap.Med, false, false),
            "специальная-оценка" or "спецоценка" or "sout" =>
                new StaffingOnboarding.Gap(false, false, gap.Sout, false),
            "риски" or "risks" => new StaffingOnboarding.Gap(false, false, false, gap.Risks),
            _ => gap
        };

    private static string ResolveProfession(
        StaffingChangeEvent alert,
        IReadOnlyList<OrganizationEmployee>? roster)
    {
        if (!string.IsNullOrWhiteSpace(alert.NewProfession))
        {
            return alert.NewProfession.Trim();
        }

        if (roster is null)
        {
            return string.Empty;
        }

        var snils = OneCEmployeeListParser.NormalizeSnils(alert.Snils);
        foreach (var e in roster)
        {
            if (snils.Length == 11
                && string.Equals(OneCEmployeeListParser.NormalizeSnils(e.Snils), snils, StringComparison.Ordinal))
            {
                return e.Profession?.Trim() ?? string.Empty;
            }

            if (EmployeeNameMatcher.Matches(e.Name, alert.Person))
            {
                return e.Profession?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private bool IsCurrentlyEmployed(
        string selectedOrganization,
        StaffingChangeEvent alert,
        Dictionary<string, IReadOnlyList<OrganizationEmployee>> rosterCache)
    {
        var eventOrg = (alert.Organization ?? string.Empty).Trim();
        if (eventOrg.Length == 0)
        {
            return false;
        }

        var rosterOrg = string.IsNullOrWhiteSpace(selectedOrganization) ? eventOrg : selectedOrganization;
        if (!rosterCache.TryGetValue(rosterOrg, out var roster))
        {
            roster = _employees.LoadForReport(rosterOrg);
            rosterCache[rosterOrg] = roster;
        }

        return StaffingOnboarding.IsInActiveRoster(roster, alert.Person, alert.Snils);
    }
}

public sealed record StaffingOnboardingItem(
    StaffingChangeEvent Event,
    StaffingOnboarding.Gap Gap,
    DateTime? DisplayDate);

using System.Text.Json;
using System.Text.RegularExpressions;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// Импорт карт СОУТ из Word → рабочие места модуля «Специальная оценка».
/// </summary>
public sealed class SoutCardDocxImportService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly TimeSpan DraftTtl = TimeSpan.FromHours(8);

    private readonly RiskDataPaths _paths;
    private readonly WorkplaceStore _workplaces;
    private readonly SoutCatalogStore _catalog;
    private readonly SoutConditionsClassStore _classes;

    public SoutCardDocxImportService(
        RiskDataPaths paths,
        WorkplaceStore workplaces,
        SoutCatalogStore catalog,
        SoutConditionsClassStore classes)
    {
        _paths = paths;
        _workplaces = workplaces;
        _catalog = catalog;
        _classes = classes;
    }

    public SoutCardImportDraft CreateDraftFromFiles(
        string organization,
        IReadOnlyList<(Stream Stream, string FileName)> files,
        bool allowOverwriteMatch = false)
    {
        var allCards = new List<ParsedSoutCard>();
        var names = new List<string>();
        var unrecognizedHints = new List<string>();
        foreach (var (stream, fileName) in files)
        {
            var parsed = SoutCardDocxImportParser.Parse(stream);
            allCards.AddRange(parsed);
            names.Add(Path.GetFileName(fileName));
            if (parsed.Count == 0)
            {
                try
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    unrecognizedHints.Add(
                        $"{Path.GetFileName(fileName)}: {SoutCardDocxImportParser.DescribeUnrecognized(stream)}");
                }
                catch
                {
                    // подсказка необязательна
                }
            }
        }

        var draft = CreateDraftFromCards(
            organization,
            allCards,
            names.Count == 0 ? "файлы.docx" : string.Join(", ", names.Take(5)) + (names.Count > 5 ? "…" : ""),
            allowOverwriteMatch);
        draft.UnrecognizedHints = unrecognizedHints;
        return draft;
    }

    public SoutCardImportApplyResult ImportFilesAuto(
        string organization,
        IReadOnlyList<(Stream Stream, string FileName)> files,
        bool overwriteExisting,
        IReadOnlyList<OrganizationEmployee>? employees = null)
    {
        var draft = CreateDraftFromFiles(organization, files, allowOverwriteMatch: overwriteExisting);
        if (draft.Items.Count == 0)
        {
            var messages = new List<string>
            {
                overwriteExisting
                    ? "Режим перезаписи: в файлах не найдено карт СОУТ («КАРТА № … специальной оценки»)."
                    : "В файлах не найдено карт СОУТ («КАРТА № … специальной оценки»)."
            };
            if (draft.UnrecognizedHints is { Count: > 0 })
            {
                messages.Add("Фрагменты из файла: " + string.Join(" || ", draft.UnrecognizedHints.Take(3)));
            }

            return new SoutCardImportApplyResult { Messages = messages };
        }

        var selected = new HashSet<string>(
            draft.Items.Select(i => i.ItemKey),
            StringComparer.OrdinalIgnoreCase);
        // Галочка «перезаписать уже заполненные» — все позиции; иначе только отмеченные в превью.
        var overwrite = overwriteExisting
            ? new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                draft.Items.Where(i => i.OverwriteExisting).Select(i => i.ItemKey),
                StringComparer.OrdinalIgnoreCase);

        var result = Apply(
            draft,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            selected,
            overwrite,
            employees);

        result.Messages.Insert(0, $"Файлов: {files.Count}, карт в Word: {draft.Items.Count}.");
        DeleteDraft(draft.Id);
        return result;
    }

    public (bool Ok, string Message) ApplyMatchingCardToWorkplaceFromFiles(
        WorkplaceRecord workplace,
        IReadOnlyList<(Stream Stream, string FileName)> files,
        bool overwriteExisting,
        IReadOnlyList<OrganizationEmployee>? employees = null)
    {
        var allCards = new List<ParsedSoutCard>();
        foreach (var (stream, _) in files)
        {
            allCards.AddRange(SoutCardDocxImportParser.Parse(stream));
        }

        if (allCards.Count == 0)
        {
            return (false, "В файлах не найдено карт СОУТ.");
        }

        var match = FindBestCardForWorkplace(workplace, allCards);
        if (match.Card is null)
        {
            var sample = string.Join(", ", allCards.Take(8).Select(c =>
                string.IsNullOrWhiteSpace(c.Profession) ? $"№{c.CardNumber}" : c.Profession));
            return (false, $"Нет карты для «{DisplayName(workplace)}». В файлах: {sample}.");
        }

        if (HasSoutData(workplace) && !overwriteExisting)
        {
            // Дополняем пустые поля, уже внесённое не трогаем.
            var catalogBatchMerge = new SoutCatalogImportBatch(_catalog, workplace.Organization);
            ApplyCardToWorkplace(
                workplace,
                match.Card,
                employees,
                catalogBatchMerge,
                mergePreserveExisting: true);
            catalogBatchMerge.Save();
            _workplaces.Upsert(workplace);
            var noteMerge = catalogBatchMerge.SummaryMessage();
            return (true,
                $"Дополнено из Word (без перезаписи): карта № {match.Card.CardNumber}, «{match.Card.Profession}»."
                + (string.IsNullOrWhiteSpace(noteMerge) ? string.Empty : " " + noteMerge));
        }

        var catalogBatch = new SoutCatalogImportBatch(_catalog, workplace.Organization);
        ApplyCardToWorkplace(
            workplace,
            match.Card,
            employees,
            catalogBatch,
            mergePreserveExisting: !overwriteExisting);
        catalogBatch.Save();
        _workplaces.Upsert(workplace);
        var catalogNote = catalogBatch.SummaryMessage();
        return (true,
            $"Из Word: карта № {match.Card.CardNumber}, «{match.Card.Profession}», класс {match.Card.ConditionsClass}."
            + (string.IsNullOrWhiteSpace(catalogNote) ? string.Empty : " " + catalogNote));
    }

    public SoutCardImportDraft? LoadDraft(string draftId)
    {
        if (string.IsNullOrWhiteSpace(draftId))
        {
            return null;
        }

        var path = DraftPath(draftId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SoutCardImportDraft>(File.ReadAllText(path), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public void SaveDraft(SoutCardImportDraft draft)
    {
        Directory.CreateDirectory(_paths.SoutCardImportDraftsFolder);
        File.WriteAllText(DraftPath(draft.Id), JsonSerializer.Serialize(draft, JsonOpts));
    }

    public void DeleteDraft(string draftId)
    {
        var path = DraftPath(draftId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public SoutCardImportApplyResult Apply(
        SoutCardImportDraft draft,
        IReadOnlyDictionary<string, string> workplaceByItemKey,
        IReadOnlySet<string> selectedKeys,
        IReadOnlySet<string> overwriteKeys,
        IReadOnlyList<OrganizationEmployee>? employees = null)
    {
        var result = new SoutCardImportApplyResult();
        var catalogBatch = new SoutCatalogImportBatch(_catalog, draft.Organization);
        var claimedWorkplaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in draft.Items)
        {
            if (!selectedKeys.Contains(item.ItemKey))
            {
                result.Skipped++;
                continue;
            }

            var explicitWorkplace = workplaceByItemKey.TryGetValue(item.ItemKey, out var mappedWp)
                                    && !string.IsNullOrWhiteSpace(mappedWp);
            var workplaceId = explicitWorkplace ? mappedWp : item.MatchedWorkplaceId;

            WorkplaceRecord? workplace = null;
            if (!string.IsNullOrWhiteSpace(workplaceId))
            {
                workplace = _workplaces.GetById(workplaceId!);
            }

            // Одно РМ — одна карта за этот импорт. Вторая/третья карта той же профессии → новое РМ.
            if (workplace is not null && claimedWorkplaceIds.Contains(workplace.Id))
            {
                workplace = null;
            }

            var canOverwrite = overwriteKeys.Contains(item.ItemKey) || item.OverwriteExisting;
            var sameCard = workplace is not null
                           && SameReportNumber(item.Card.CardNumber, workplace.SoutReportNumber);

            // Заполненная карта с другим №: без галочки — новая строка; с галочкой — перезапись.
            if (workplace is not null
                && !workplace.IsGroup
                && HasSoutData(workplace)
                && !sameCard
                && !canOverwrite)
            {
                workplace = null;
            }

            // Без галочки — только пустое РМ / тот же №. С галочкой — можно взять уже заполненное.
            if ((workplace is null || workplace.IsGroup) && !explicitWorkplace)
            {
                var rematch = FindBestWorkplace(
                    item.Card,
                    _workplaces.LoadAll().Where(w => !w.IsGroup).ToList(),
                    claimedWorkplaceIds,
                    draft.Organization,
                    preferFilledForOverwrite: canOverwrite);
                if (rematch.Workplace is not null
                    && !claimedWorkplaceIds.Contains(rematch.Workplace.Id)
                    && (canOverwrite
                        || !HasSoutData(rematch.Workplace)
                        || SameReportNumber(item.Card.CardNumber, rematch.Workplace.SoutReportNumber)))
                {
                    workplace = rematch.Workplace;
                    sameCard = SameReportNumber(item.Card.CardNumber, workplace.SoutReportNumber);
                }
            }

            // Нет подходящего РМ → новая строка по профессии (2-я, 3-я…).
            if (workplace is null || workplace.IsGroup)
            {
                var deptHint = ResolveDepartmentHint(item.Card, draft.Organization, claimedWorkplaceIds);
                workplace = TryCreateWorkplaceFromCard(item.Card, draft.Organization, deptHint);
                if (workplace is not null)
                {
                    result.Created++;
                    item.MatchedWorkplaceId = workplace.Id;
                    item.MatchedWorkplaceName = DisplayName(workplace);
                    sameCard = false;
                }
            }

            if (workplace is null || workplace.IsGroup)
            {
                result.Skipped++;
                result.Messages.Add($"Карта № {item.Card.CardNumber} ({item.Card.Profession}): нет рабочего места.");
                continue;
            }

            sameCard = SameReportNumber(item.Card.CardNumber, workplace.SoutReportNumber);
            // Тот же № карты — обновляем поля из Word; галочка — чужой № на уже заполненном РМ.
            var replaceExisting = canOverwrite || sameCard;

            // Без галочки не затираем чужой № на уже заполненном РМ.
            if (HasSoutData(workplace) && !sameCard && !canOverwrite)
            {
                result.Skipped++;
                result.Messages.Add(
                    $"Карта № {item.Card.CardNumber} ({item.Card.Profession}): «{DisplayName(workplace)}» уже заполнена — не перезаписана.");
                continue;
            }

            // Merge без галочки (и без того же №); полная перезапись — галочка или тот же № карты.
            ApplyCardToWorkplace(
                workplace,
                item.Card,
                employees,
                catalogBatch,
                mergePreserveExisting: !replaceExisting);
            if (string.IsNullOrWhiteSpace(workplace.LinkedSoutCardWorkplaceId))
            {
                workplace.LinkedSoutCardWorkplaceId = workplace.Id;
            }

            _workplaces.Upsert(workplace);
            claimedWorkplaceIds.Add(workplace.Id);
            result.Applied++;
            result.Messages.Add(
                $"Карта № {item.Card.CardNumber} → «{DisplayName(workplace)}»: ОК {item.Card.OkCode}, класс {item.Card.ConditionsClass}.");
        }

        catalogBatch.Save();
        var catalogNote = catalogBatch.SummaryMessage();
        if (!string.IsNullOrWhiteSpace(catalogNote))
        {
            result.Messages.Insert(0, catalogNote);
        }

        return result;
    }

    public void ApplyCardToWorkplace(
        WorkplaceRecord workplace,
        ParsedSoutCard card,
        IReadOnlyList<OrganizationEmployee>? employees = null,
        SoutCatalogImportBatch? catalogBatch = null,
        bool mergePreserveExisting = true)
    {
        if (!string.IsNullOrWhiteSpace(card.CardNumber)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.SoutReportNumber)))
        {
            workplace.SoutReportNumber = card.CardNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(card.Profession))
        {
            var prof = card.Profession.Trim();
            if (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.Profession))
            {
                workplace.Profession = prof;
            }

            if (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.Name))
            {
                workplace.Name = string.IsNullOrWhiteSpace(workplace.Profession) ? prof : workplace.Profession;
            }
        }

        if (!string.IsNullOrWhiteSpace(card.Department)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.Department)))
        {
            workplace.Department = card.Department.Trim();
        }

        if (!string.IsNullOrWhiteSpace(card.OkCode)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.OkCode)))
        {
            workplace.OkCode = card.OkCode.Trim();
        }

        // Чел. из штата важнее: из Word пишем только если в РМ ещё 0.
        if (card.WorkerCount > 0 && workplace.WorkerCount <= 0)
        {
            workplace.WorkerCount = card.WorkerCount;
        }
        else if (!mergePreserveExisting && card.WorkerCount > 0)
        {
            workplace.WorkerCount = card.WorkerCount;
        }

        if (!string.IsNullOrWhiteSpace(card.ConditionsClass)
            && (!mergePreserveExisting
                || string.IsNullOrWhiteSpace(workplace.ConditionsClass)
                || IsDefaultConditionsClass(workplace.ConditionsClass)))
        {
            workplace.ConditionsClass = _classes.MapToCatalog(card.ConditionsClass);
        }

        if (card.PreparedDate is not null
            && (!mergePreserveExisting || workplace.SoutAssessmentDate is null))
        {
            workplace.SoutAssessmentDate = card.PreparedDate;
        }

        // «Оценка проведена» только если в Word есть дата, № карты или реальный класс.
        var hasAssessmentEvidence = card.PreparedDate is not null
                                    || !string.IsNullOrWhiteSpace(card.CardNumber)
                                    || (!string.IsNullOrWhiteSpace(card.ConditionsClass)
                                        && !IsDefaultConditionsClass(card.ConditionsClass));
        if (hasAssessmentEvidence)
        {
            workplace.SoutAssessmentNotConducted = false;
            // Импортированная карта — плановая оценка; флаг внеплановой с кадровых смен снимаем.
            workplace.RequiresUnscheduledSout = false;
        }

        workplace.SoutCard ??= new SoutCardDetails();
        var details = workplace.SoutCard;

        if (card.PreparedDate is not null
            && (!mergePreserveExisting || details.PreparedDate is null))
        {
            details.PreparedDate = card.PreparedDate;
        }

        if (card.PreparedDate is not null || workplace.SoutAssessmentDate is not null || details.PreparedDate is not null)
        {
            SoutCardPrintBuilder.SyncAssessmentDates(workplace);
        }

        if (!string.IsNullOrWhiteSpace(card.AnalogousWorkplaces)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(details.AnalogousWorkplaces)))
        {
            details.AnalogousWorkplaces = card.AnalogousWorkplaces;
        }

        if (card.AnalogousWorkerCount > 0
            && (!mergePreserveExisting || details.AnalogousWorkerCount <= 0))
        {
            details.AnalogousWorkerCount = card.AnalogousWorkerCount;
        }

        if (card.WorkersWomen > 0 && (!mergePreserveExisting || details.WorkersWomen <= 0))
        {
            details.WorkersWomen = card.WorkersWomen;
        }

        if (card.WorkersUnder18 > 0 && (!mergePreserveExisting || details.WorkersUnder18 <= 0))
        {
            details.WorkersUnder18 = card.WorkersUnder18;
        }

        if (card.WorkersDisabled > 0 && (!mergePreserveExisting || details.WorkersDisabled <= 0))
        {
            details.WorkersDisabled = card.WorkersDisabled;
        }

        // Оборудование / материалы → справочники + привязка к РМ (и текст в карте).
        var ownsBatch = catalogBatch is null;
        catalogBatch ??= new SoutCatalogImportBatch(_catalog, workplace.Organization);
        catalogBatch.ApplyToWorkplace(workplace, card.Equipment, card.Materials, mergePreserveExisting);
        if (ownsBatch)
        {
            catalogBatch.Save();
        }

        if (!string.IsNullOrWhiteSpace(workplace.ConditionsClass)
            && (!mergePreserveExisting
                || string.IsNullOrWhiteSpace(details.FinalClass)
                || IsDefaultConditionsClass(details.FinalClass)))
        {
            details.FinalClass = workplace.ConditionsClass;
        }

        if (!string.IsNullOrWhiteSpace(card.Recommendations)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(details.Recommendations)))
        {
            details.Recommendations = card.Recommendations;
        }

        if (card.SnilsList.Count > 0)
        {
            details.ExtraLines ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!mergePreserveExisting
                || !details.ExtraLines.TryGetValue("021", out var existing021)
                || string.IsNullOrWhiteSpace(existing021))
            {
                details.ExtraLines["021"] = string.Join("; ",
                    card.SnilsList.Select(OneCEmployeeListParser.FormatSnils));
            }

            if (employees is { Count: > 0 })
            {
                var ids = MatchEmployeesBySnils(employees, card.SnilsList);
                if (ids.Count > 0
                    && (!mergePreserveExisting || details.EmployeeIds is null || details.EmployeeIds.Count == 0))
                {
                    details.EmployeeIds = ids;
                }
            }
        }

        workplace.PlanYear = SoutStatusBuilder.ResolvePlanYearDisplay(workplace);
        workplace.ModifiedAt = DateTime.UtcNow;
    }

    private SoutCardImportDraft CreateDraftFromCards(
        string organization,
        IReadOnlyList<ParsedSoutCard> cards,
        string sourceFileName,
        bool allowOverwriteMatch = false)
    {
        var workplaces = _workplaces.LoadAll().Where(w => !w.IsGroup).ToList();
        var draft = new SoutCardImportDraft
        {
            Organization = organization,
            SourceFileName = sourceFileName
        };
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in cards)
        {
            var match = FindBestWorkplace(
                card,
                workplaces,
                used,
                organization,
                preferFilledForOverwrite: allowOverwriteMatch);

            // Без «перезаписать»: заполненное РМ с другим № не занимаем — Apply создаст новую строку.
            // С галочкой: оставляем сопоставление, чтобы Apply перезаписал данные.
            if (match.Workplace is not null
                && HasSoutData(match.Workplace)
                && !SameReportNumber(card.CardNumber, match.Workplace.SoutReportNumber)
                && !allowOverwriteMatch)
            {
                match = new MatchResult(
                    null,
                    $"Доп. карта «{ProfessionMatcher.Normalize(card.Profession)}» № {card.CardNumber} — будет создано РМ.");
            }
            else if (match.Workplace is null)
            {
                match = new MatchResult(
                    null,
                    $"Нет свободного РМ «{ProfessionMatcher.Normalize(card.Profession)}» — будет создано.");
            }

            var item = new SoutCardImportDraftItem
            {
                Card = card,
                Selected = true,
                MatchedWorkplaceId = match.Workplace?.Id,
                MatchedWorkplaceName = match.Workplace is null ? string.Empty : DisplayName(match.Workplace),
                MatchNote = match.Note,
                HasExistingData = match.Workplace is not null && HasSoutData(match.Workplace),
                // Галочка на форме — перезапись всех позиций с уже заполненными данными.
                OverwriteExisting = allowOverwriteMatch
                                    && match.Workplace is not null
                                    && HasSoutData(match.Workplace)
            };
            if (match.Workplace is not null)
            {
                used.Add(match.Workplace.Id);
            }

            draft.Items.Add(item);
        }

        SaveDraft(draft);
        CleanupOldDrafts();
        return draft;
    }

    /// <summary>
    /// Подразделение для новой строки: из Word, иначе у «соседа» той же профессии
    /// (чтобы второй «Специалист по снабжению» попал в ту же «Администрацию», а не в «Без подразделения»).
    /// </summary>
    private string ResolveDepartmentHint(
        ParsedSoutCard card,
        string preferOrganization,
        IReadOnlySet<string>? preferWorkplaceIds = null)
    {
        if (!string.IsNullOrWhiteSpace(card.Department))
        {
            return card.Department.Trim();
        }

        var org = ResolveOrganization(card.Organization, preferOrganization);
        var profession = ProfessionMatcher.Normalize(card.Profession);
        if (string.IsNullOrWhiteSpace(org) || profession.Length == 0)
        {
            return "Без подразделения";
        }

        var pool = _workplaces.LoadByOrganization(org).Where(w => !w.IsGroup).ToList();
        WorkplaceRecord? sibling = null;
        if (preferWorkplaceIds is { Count: > 0 })
        {
            sibling = pool.FirstOrDefault(w =>
                preferWorkplaceIds.Contains(w.Id)
                && ScoreProfession(
                    ProfessionMatcher.Normalize(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession),
                    profession) >= 100);
        }

        sibling ??= pool
            .Where(w => ScoreProfession(
                ProfessionMatcher.Normalize(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession),
                profession) >= 100)
            .OrderByDescending(w => !string.IsNullOrWhiteSpace(w.Department))
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(sibling?.Department))
        {
            return sibling!.Department.Trim();
        }

        if (!string.IsNullOrWhiteSpace(sibling?.Name) && sibling!.IsGroup)
        {
            return sibling.Name.Trim();
        }

        return "Без подразделения";
    }

    private WorkplaceRecord? TryCreateWorkplaceFromCard(
        ParsedSoutCard card,
        string preferOrganization,
        string? departmentHint = null)
    {
        var profession = ProfessionMatcher.Normalize(card.Profession);
        if (profession.Length == 0)
        {
            return null;
        }

        var org = ResolveOrganization(card.Organization, preferOrganization);
        if (string.IsNullOrWhiteSpace(org))
        {
            return null;
        }

        var department = !string.IsNullOrWhiteSpace(card.Department)
            ? card.Department.Trim()
            : !string.IsNullOrWhiteSpace(departmentHint)
                ? departmentHint.Trim()
                : "Без подразделения";

        // № РМ генерируется отдельно; номер карты СОУТ пишется в SoutReportNumber при Apply.
        var mappedClass = string.IsNullOrWhiteSpace(card.ConditionsClass)
            ? "2"
            : _classes.MapToCatalog(card.ConditionsClass);

        var created = SoutWorkplaceStaffSync.AddWorkplace(
            org,
            _workplaces,
            profession,
            profession,
            department,
            workplaceNumber: null,
            mappedClass);

        if (created is not null)
        {
            // Не ставим SoutReportNumber до Apply — иначе РМ считается «уже заполненным»
            // и вторая карта той же профессии может уйти в перезапись вместо создания.
            created.LinkedSoutCardWorkplaceId = created.Id;
            _workplaces.Upsert(created);
        }

        return created;
    }

    private static string ResolveOrganization(string? cardOrganization, string preferOrganization)
    {
        var cardOrg = (cardOrganization ?? string.Empty).Trim();
        var selected = (preferOrganization ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(cardOrg) && !string.IsNullOrWhiteSpace(selected)
            && OrganizationMatcher.Matches(cardOrg, selected))
        {
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(cardOrg))
        {
            return cardOrg;
        }

        return selected;
    }

    private sealed record MatchResult(WorkplaceRecord? Workplace, string Note);
    private sealed record CardMatchResult(ParsedSoutCard? Card, string Note);

    private static MatchResult FindBestWorkplace(
        ParsedSoutCard card,
        IReadOnlyList<WorkplaceRecord> workplaces,
        HashSet<string> usedIds,
        string preferOrganization,
        bool preferFilledForOverwrite = false)
    {
        var profession = ProfessionMatcher.Normalize(card.Profession);
        if (profession.Length == 0)
        {
            return new MatchResult(null, "В файле не найдена должность.");
        }

        var preferOrg = (preferOrganization ?? string.Empty).Trim();
        var pool = workplaces.Where(w => !usedIds.Contains(w.Id)).ToList();
        // Сначала только РМ выбранной организации — иначе чужая «похожая» должность перехватывает карту.
        if (preferOrg.Length > 0)
        {
            var sameOrg = pool
                .Where(w => OrganizationMatcher.Matches(preferOrg, w.Organization) ||
                            string.Equals(preferOrg, w.Organization, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameOrg.Count > 0)
            {
                pool = sameOrg;
            }
        }

        var candidates = pool
            .Select(w =>
            {
                var name = ProfessionMatcher.Normalize(
                    string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession);
                var score = ScoreProfession(name, profession);
                if (score <= 0)
                {
                    return (Workplace: w, Score: 0);
                }

                if (!string.IsNullOrWhiteSpace(card.Organization))
                {
                    if (OrganizationMatcher.Matches(card.Organization, w.Organization) ||
                        string.Equals(card.Organization, w.Organization, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 50;
                    }
                    else
                    {
                        score -= 40;
                    }
                }
                else if (preferOrg.Length > 0 &&
                         OrganizationMatcher.Matches(preferOrg, w.Organization))
                {
                    score += 20;
                }

                // Точный № карты — приоритетнее пустого РМ той же профессии.
                if (SameReportNumber(card.CardNumber, w.SoutReportNumber))
                {
                    score += 200;
                }

                if (!string.IsNullOrWhiteSpace(card.Department) &&
                    !string.IsNullOrWhiteSpace(w.Department) &&
                    DepartmentsClose(card.Department, w.Department))
                {
                    score += 25;
                }

                var filled = HasSoutData(w);
                if (preferFilledForOverwrite)
                {
                    // Галочка «перезаписать»: целимся в уже заполненные карты, не в пустые заготовки.
                    if (filled)
                    {
                        if (score >= 50)
                        {
                            score += 120;
                        }
                        else if (score >= 30)
                        {
                            score += 60;
                        }
                    }
                }
                else if (!filled && score >= 80)
                {
                    // Обычный импорт: пустые заготовки из штата предпочтительнее.
                    score += 80;
                }

                return (Workplace: w, Score: score);
            })
            .Where(x => preferFilledForOverwrite
                ? x.Score >= 50 || (HasSoutData(x.Workplace) && x.Score >= 30)
                : x.Score >= 80)
            .OrderByDescending(x => SameReportNumber(card.CardNumber, x.Workplace.SoutReportNumber))
            .ThenByDescending(x => preferFilledForOverwrite
                ? HasSoutData(x.Workplace)
                : !HasSoutData(x.Workplace))
            .ThenByDescending(x => x.Score)
            .ToList();

        if (candidates.Count == 0)
        {
            return new MatchResult(null, $"Нет РМ «{profession}» — будет создано.");
        }

        var best = candidates[0];
        var note = SameReportNumber(card.CardNumber, best.Workplace.SoutReportNumber)
            ? $"Авто по № карты: «{DisplayName(best.Workplace)}»"
            : HasSoutData(best.Workplace)
                ? (preferFilledForOverwrite
                    ? $"Перезапись: «{DisplayName(best.Workplace)}»"
                    : $"Авто (уже есть данные): «{DisplayName(best.Workplace)}»")
                : $"Авто: «{DisplayName(best.Workplace)}»";
        return new MatchResult(best.Workplace, note);
    }

    private static bool SameReportNumber(string? left, string? right)
    {
        var a = NormalizeReportNumber(left);
        var b = NormalizeReportNumber(right);
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeReportNumber(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return string.Empty;
        }

        // «№ 25-1», «25-1», «001» → сопоставимый ключ без ведущих нулей у чисто числовых.
        t = t.Replace("№", string.Empty, StringComparison.Ordinal).Trim();
        if (t.All(char.IsDigit))
        {
            t = t.TrimStart('0');
            if (t.Length == 0)
            {
                t = "0";
            }
        }

        return t;
    }

    private static bool DepartmentsClose(string left, string right)
    {
        var a = ProfessionMatcher.Normalize(left);
        var b = ProfessionMatcher.Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // «Отдел ПТО» ≈ «производственно-технический отдел»
        if (ProfessionMatcher.Matches(a, b) || ProfessionMatcher.MatchesWithAcronyms(a, b) ||
            ProfessionMatcher.SameSignificantTokens(a, b))
        {
            return true;
        }

        var ca = ProfessionMatcher.CanonicalKey(a);
        var cb = ProfessionMatcher.CanonicalKey(b);
        if (ca.Length > 0 && string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
               b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    private static CardMatchResult FindBestCardForWorkplace(
        WorkplaceRecord workplace,
        IReadOnlyList<ParsedSoutCard> cards)
    {
        var profession = ProfessionMatcher.Normalize(
            string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession);
        var ranked = cards
            .Select(card =>
            {
                var score = ScoreProfession(profession, card.Profession);
                if (score <= 0)
                {
                    return (Card: card, Score: 0);
                }

                if (!string.IsNullOrWhiteSpace(card.Organization) &&
                    OrganizationMatcher.Matches(card.Organization, workplace.Organization))
                {
                    score += 40;
                }

                if (!string.IsNullOrWhiteSpace(card.CardNumber) &&
                    !string.IsNullOrWhiteSpace(workplace.SoutReportNumber) &&
                    string.Equals(
                        card.CardNumber.TrimStart('0'),
                        workplace.SoutReportNumber.Trim().TrimStart('0'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 25;
                }

                if (!string.IsNullOrWhiteSpace(card.Department) &&
                    !string.IsNullOrWhiteSpace(workplace.Department) &&
                    DepartmentsClose(card.Department, workplace.Department))
                {
                    score += 20;
                }

                return (Card: card, Score: score);
            })
            .Where(x => x.Score >= 80)
            .OrderByDescending(x => x.Score)
            .ToList();

        return ranked.Count == 0
            ? new CardMatchResult(null, "нет")
            : new CardMatchResult(ranked[0].Card, "ok");
    }

    private static int ScoreProfession(string left, string? right)
    {
        var a = ProfessionMatcher.Normalize(left);
        var b = ProfessionMatcher.Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        // «Начальник ПТО» ≈ «Начальник производственно-технического отдела»
        // «Генеральный директор предприятия» ≈ «генеральный директор»
        if (ProfessionMatcher.Matches(a, b) || ProfessionMatcher.Matches(b, a))
        {
            return 100;
        }

        var ca = ProfessionMatcher.CanonicalKey(a);
        var cb = ProfessionMatcher.CanonicalKey(b);
        if (ca.Length > 0 && string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (ProfessionMatcher.MatchesWithAcronyms(a, b))
        {
            return 95;
        }

        if (ProfessionMatcher.CoreKey(a).Length > 0 &&
            string.Equals(ProfessionMatcher.CoreKey(a), ProfessionMatcher.CoreKey(b), StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        var aTokens = ProfessionMatcher.SignificantStemmedTokens(a);
        var bTokens = ProfessionMatcher.SignificantStemmedTokens(b);
        if (aTokens.Length > 0 && bTokens.Length > 0 &&
            string.Equals(aTokens[0], bTokens[0], StringComparison.OrdinalIgnoreCase))
        {
            // Одинаковое первое слово (начальник / инженер / мастер…) + хвост-аббревиатура.
            var shorter = a.Length <= b.Length ? aTokens : bTokens;
            var longer = a.Length <= b.Length ? bTokens : aTokens;
            if (shorter.Length >= 2 && longer.Length > shorter.Length &&
                shorter.Skip(1).All(t => t.Length is >= 2 and <= 6))
            {
                return 70;
            }
        }

        return 0;
    }

    /// <summary>
    /// Карта уже заполнена данными СОУТ.
    /// Не считать «заполненной» РМ из штата с классом «2» по умолчанию и без даты/№ карты —
    /// иначе массовый импорт Word пропускает почти все строки.
    /// </summary>
    private static bool HasSoutData(WorkplaceRecord w)
    {
        if (w.SoutAssessmentDate is not null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(w.SoutReportNumber))
        {
            return true;
        }

        var card = w.SoutCard;
        if (card is null)
        {
            return false;
        }

        if (card.PreparedDate is not null)
        {
            return true;
        }

        if (card.WorkersWomen > 0 || card.WorkersUnder18 > 0 || card.WorkersDisabled > 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(card.Equipment) ||
            !string.IsNullOrWhiteSpace(card.Materials) ||
            !string.IsNullOrWhiteSpace(card.AnalogousWorkplaces))
        {
            return true;
        }

        // Итоговый класс из реальной оценки (не путать с дефолтом «2» из синхронизации штата).
        var final = (card.FinalClass ?? string.Empty).Trim();
        return final.Length > 0 && !IsDefaultConditionsClass(final);
    }

    private static bool IsDefaultConditionsClass(string value)
    {
        var code = SoutConditionsClassStore.ExtractSubclassCode(value);
        return string.Equals(code, "2", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> MatchEmployeesBySnils(
        IReadOnlyList<OrganizationEmployee> employees,
        IReadOnlyList<string> snilsList)
    {
        var wanted = snilsList
            .Select(OneCEmployeeListParser.NormalizeSnils)
            .Where(s => s.Length == 11)
            .ToHashSet(StringComparer.Ordinal);
        return employees
            .Where(e => wanted.Contains(OneCEmployeeListParser.NormalizeSnils(e.Snils)))
            .Select(e => e.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DisplayName(WorkplaceRecord w)
    {
        var prof = string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession;
        return string.IsNullOrWhiteSpace(w.Department) ? prof : $"{prof} / {w.Department}";
    }

    private string DraftPath(string draftId)
    {
        var safe = Regex.Replace(draftId, @"[^a-zA-Z0-9]", string.Empty);
        if (safe.Length == 0)
        {
            safe = Guid.NewGuid().ToString("N");
        }

        return Path.Combine(_paths.SoutCardImportDraftsFolder, $"{safe}.json");
    }

    private void CleanupOldDrafts()
    {
        try
        {
            var cutoff = DateTime.UtcNow - DraftTtl;
            foreach (var file in Directory.EnumerateFiles(_paths.SoutCardImportDraftsFolder, "*.json"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // ignore
        }
    }
}

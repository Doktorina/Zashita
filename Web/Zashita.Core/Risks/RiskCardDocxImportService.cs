using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

/// <summary>
/// Импорт карт идентификации опасностей из Word → рабочие места модуля «Риски».
/// </summary>
public sealed class RiskCardDocxImportService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly TimeSpan DraftTtl = TimeSpan.FromHours(8);

    private readonly RiskDataPaths _paths;
    private readonly RiskWorkplaceStore _workplaces;
    private readonly RiskHazardCatalogStore _catalog;

    public RiskCardDocxImportService(
        RiskDataPaths paths,
        RiskWorkplaceStore workplaces,
        RiskHazardCatalogStore catalog)
    {
        _paths = paths;
        _workplaces = workplaces;
        _catalog = catalog;
    }

    public RiskCardImportDraft CreateDraft(
        string organization,
        Stream docxStream,
        string sourceFileName) =>
        CreateDraftFromCards(
            organization,
            RiskCardDocxImportParser.Parse(docxStream),
            Path.GetFileName(sourceFileName));

    /// <summary>Несколько Word-файлов → один черновик (карты из всех файлов).</summary>
    public RiskCardImportDraft CreateDraftFromFiles(
        string organization,
        IReadOnlyList<(Stream Stream, string FileName)> files)
    {
        var allCards = new List<ParsedRiskCard>();
        var names = new List<string>();
        foreach (var (stream, fileName) in files)
        {
            var cards = RiskCardDocxImportParser.Parse(stream);
            foreach (var card in cards)
            {
                // Пометим источник в MatchNote позже; организацию уже несёт карта.
                allCards.Add(card);
            }

            names.Add(Path.GetFileName(fileName));
        }

        return CreateDraftFromCards(
            organization,
            allCards,
            names.Count == 0 ? "файлы.docx" : string.Join(", ", names.Take(5)) + (names.Count > 5 ? "…" : ""));
    }

    /// <summary>
    /// Сразу разобрать все файлы, сопоставить компанию/профессию и записать в РМ.
    /// Вторая/третья карта той же профессии → новое РМ; уже внесённые строки не затираем.
    /// </summary>
    public RiskCardImportApplyResult ImportFilesAuto(
        string organization,
        IReadOnlyList<(Stream Stream, string FileName)> files,
        bool overwriteExisting)
    {
        var draft = CreateDraftFromFiles(organization, files);
        if (draft.Items.Count == 0)
        {
            return new RiskCardImportApplyResult
            {
                Messages = ["В файлах не найдено карт с таблицами опасностей."]
            };
        }

        var selected = new HashSet<string>(
            draft.Items.Select(i => i.ItemKey),
            StringComparer.OrdinalIgnoreCase);
        var overwrite = overwriteExisting
            ? new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                draft.Items.Where(i => i.OverwriteExisting).Select(i => i.ItemKey),
                StringComparer.OrdinalIgnoreCase);

        var result = Apply(
            draft,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            selected,
            overwrite);

        result.Messages.Insert(0, $"Файлов: {files.Count}, карт в Word: {draft.Items.Count}.");
        DeleteDraft(draft.Id);
        return result;
    }

    private RiskCardImportDraft CreateDraftFromCards(
        string organization,
        IReadOnlyList<ParsedRiskCard> cards,
        string sourceFileName)
    {
        var workplaces = _workplaces.LoadAll()
            .Where(w => !w.IsGroup)
            .ToList();

        var draft = new RiskCardImportDraft
        {
            Organization = organization,
            SourceFileName = sourceFileName
        };

        var usedWorkplaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in cards)
        {
            NormalizeCardScales(card);
            var match = FindBestWorkplace(card, workplaces, usedWorkplaceIds, preferOrganization: organization);

            // Заполненное РМ с другим № карты — не занимаем; Apply создаст новую строку.
            // РМ при разборе черновика не создаём — иначе при отмене/повторной загрузке копятся пустые дубли.
            if (match.Workplace is not null
                && HasRiskData(match.Workplace)
                && !SameRiskCardNumber(card.CardNumber, match.Workplace.RiskCardNumber))
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

            var item = new RiskCardImportDraftItem
            {
                Card = card,
                Selected = true,
                MatchedWorkplaceId = match.Workplace?.Id,
                MatchedWorkplaceName = match.Workplace is null
                    ? string.Empty
                    : DisplayName(match.Workplace),
                MatchNote = match.Note,
                ExistingLineCount = match.Workplace?.RiskLines?.Count(l => !IsEmptyLine(l)) ?? 0,
                OverwriteExisting = false
            };

            if (match.Workplace is not null)
            {
                usedWorkplaceIds.Add(match.Workplace.Id);
            }

            draft.Items.Add(item);
        }

        SaveDraft(draft);
        CleanupOldDrafts();
        return draft;
    }

    /// <summary>
    /// На странице карты: среди нескольких Word найти карту этой организации/профессии и записать.
    /// </summary>
    public (bool Ok, string Message, int LineCount) ApplyMatchingCardToWorkplaceFromFiles(
        WorkplaceRecord workplace,
        IReadOnlyList<(Stream Stream, string FileName)> files,
        bool overwriteExisting,
        bool persist = true)
    {
        var allCards = new List<ParsedRiskCard>();
        var fileNotes = new List<string>();
        foreach (var (stream, fileName) in files)
        {
            var cards = RiskCardDocxImportParser.Parse(stream);
            fileNotes.Add($"{Path.GetFileName(fileName)}: {cards.Count} карт");
            allCards.AddRange(cards);
        }

        if (allCards.Count == 0)
        {
            return (false,
                $"В файлах не найдено карт с таблицами опасностей ({string.Join("; ", fileNotes)}).",
                0);
        }

        foreach (var card in allCards)
        {
            NormalizeCardScales(card);
        }

        var match = FindBestCardForWorkplace(workplace, allCards);
        if (match.Card is null)
        {
            var sample = string.Join(", ", allCards.Take(8).Select(c =>
                string.IsNullOrWhiteSpace(c.Profession) ? $"№{c.CardNumber}" : c.Profession));
            return (false,
                $"Нет карты для «{DisplayName(workplace)}». В файлах найдены: {sample}.",
                0);
        }

        var existingCount = workplace.RiskLines?.Count(l => !IsEmptyLine(l)) ?? 0;
        if (existingCount > 0 && !overwriteExisting)
        {
            // Дополняем пустые поля шапки; таблицу опасностей не трогаем.
            ApplyCardToWorkplace(workplace, match.Card, mergePreserveExisting: true);
            if (persist)
            {
                _workplaces.Upsert(workplace);
            }

            return (true,
                $"Дополнено из Word (без перезаписи таблицы): карта № {match.Card.CardNumber}, «{match.Card.Profession}». Уже было {existingCount} строк.",
                existingCount);
        }

        ApplyCardToWorkplace(workplace, match.Card, mergePreserveExisting: !overwriteExisting);
        if (persist)
        {
            _workplaces.Upsert(workplace);
        }

        var who = string.IsNullOrWhiteSpace(match.Card.CompiledBy)
            ? string.Empty
            : $"; составил: {match.Card.CompiledBy}";
        return (true,
            $"Из Word: карта № {match.Card.CardNumber}, {match.Card.Lines.Count} опасностей ({match.Card.Profession}){who}. Файлов: {files.Count}.",
            match.Card.Lines.Count);
    }

    /// <summary>
    /// На странице карты: найти в Word карту этой организации/профессии и записать строки в РМ.
    /// </summary>
    public (bool Ok, string Message, int LineCount) ApplyMatchingCardToWorkplace(
        WorkplaceRecord workplace,
        Stream docxStream,
        bool overwriteExisting) =>
        ApplyMatchingCardToWorkplaceFromFiles(
            workplace,
            [(docxStream, "upload.docx")],
            overwriteExisting);

    public void ApplyCardToWorkplace(
        WorkplaceRecord workplace,
        ParsedRiskCard card,
        bool mergePreserveExisting = true)
    {
        NormalizeCardScales(card);

        if (!string.IsNullOrWhiteSpace(card.CardNumber)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.RiskCardNumber)))
        {
            workplace.RiskCardNumber = card.CardNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(card.OverallRisk)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.OverallRisk)))
        {
            workplace.OverallRisk = NormalizeOverallRisk(card.OverallRisk);
        }

        if (card.AssessmentDate is not null
            && (!mergePreserveExisting || workplace.RiskAssessmentDate is null))
        {
            workplace.RiskAssessmentDate = card.AssessmentDate;
        }

        if (!string.IsNullOrWhiteSpace(card.Department)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.Department)))
        {
            workplace.Department = card.Department.Trim();
        }

        if (!string.IsNullOrWhiteSpace(card.Profession)
            && (!mergePreserveExisting || string.IsNullOrWhiteSpace(workplace.Profession)))
        {
            workplace.Profession = ProfessionMatcher.Normalize(card.Profession);
            if (string.IsNullOrWhiteSpace(workplace.Name))
            {
                workplace.Name = workplace.Profession;
            }
        }

        var existingLines = (workplace.RiskLines ?? [])
            .Where(l => !IsEmptyLine(l))
            .ToList();

        // Уже есть таблица — не затираем строки (только шапка выше), пока нет «перезаписать».
        if (mergePreserveExisting && existingLines.Count > 0)
        {
            workplace.RiskAssessmentNotConducted = false;
            workplace.ModifiedAt = DateTime.UtcNow;
            return;
        }

        var catalog = _catalog.LoadAll();
        var byNumber = new Dictionary<string, RiskHazardCatalogItem>(StringComparer.OrdinalIgnoreCase);
        var byTitle = new Dictionary<string, RiskHazardCatalogItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog)
        {
            var num = NormalizeHazardNumber(item.Number);
            if (num.Length > 0 && !byNumber.ContainsKey(num))
            {
                byNumber[num] = item;
            }

            var titleKey = NormalizeTitleKey(item.Title);
            if (titleKey.Length > 0 && !byTitle.ContainsKey(titleKey))
            {
                byTitle[titleKey] = item;
            }
        }

        var catalogChanged = false;
        var lines = new List<WorkplaceRiskLine>();
        foreach (var parsed in card.Lines)
        {
            var (catalogItem, created) = ResolveOrCreateCatalogItem(parsed, byNumber, byTitle, catalog);
            if (created)
            {
                catalogChanged = true;
            }

            lines.Add(ToWorkplaceLine(parsed, catalogItem));
        }

        if (catalogChanged)
        {
            _catalog.SaveAll(catalog);
        }

        workplace.RiskAssessmentNotConducted = false;
        workplace.RiskLines = lines;
        foreach (var line in workplace.RiskLines)
        {
            var score = WorkplaceRiskScale.ComputeScore(line.Severity, line.Probability);
            if (score is not null)
            {
                line.InitialRisk = score.Value.ToString();
                line.Significance = WorkplaceRiskScale.ZoneForScore(score.Value);
            }
        }

        workplace.ModifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Для уже импортированных строк без связи с реестром — привязать/создать пункты по номеру и названию.
    /// </summary>
    public bool EnsureLinesLinkedToCatalog(WorkplaceRecord workplace, bool persist = true)
    {
        if (workplace.RiskLines is not { Count: > 0 })
        {
            return false;
        }

        var catalog = _catalog.LoadAll();
        var byNumber = new Dictionary<string, RiskHazardCatalogItem>(StringComparer.OrdinalIgnoreCase);
        var byTitle = new Dictionary<string, RiskHazardCatalogItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog)
        {
            var num = NormalizeHazardNumber(item.Number);
            if (num.Length > 0 && !byNumber.ContainsKey(num))
            {
                byNumber[num] = item;
            }

            var titleKey = NormalizeTitleKey(item.Title);
            if (titleKey.Length > 0 && !byTitle.ContainsKey(titleKey))
            {
                byTitle[titleKey] = item;
            }
        }

        var catalogChanged = false;
        var linesChanged = false;
        foreach (var line in workplace.RiskLines)
        {
            if (!string.IsNullOrWhiteSpace(line.HazardCatalogId) &&
                catalog.Any(c => string.Equals(c.Id, line.HazardCatalogId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.HazardTitle) && string.IsNullOrWhiteSpace(line.HazardNumber))
            {
                continue;
            }

            var parsed = new ParsedRiskCardLine
            {
                HazardNumber = line.HazardNumber,
                HazardTitle = line.HazardTitle,
                ControlMeasures = line.ControlMeasures,
                InitialRisk = line.InitialRisk,
                ResidualRisk = line.ResidualRisk
            };
            var (item, created) = ResolveOrCreateCatalogItem(parsed, byNumber, byTitle, catalog);
            if (created)
            {
                catalogChanged = true;
            }

            if (!string.Equals(line.HazardCatalogId, item.Id, StringComparison.OrdinalIgnoreCase))
            {
                line.HazardCatalogId = item.Id;
                linesChanged = true;
            }

            if (string.IsNullOrWhiteSpace(line.HazardNumber) && !string.IsNullOrWhiteSpace(item.Number))
            {
                line.HazardNumber = item.Number;
                linesChanged = true;
            }
        }

        if (catalogChanged)
        {
            _catalog.SaveAll(catalog);
        }

        if (linesChanged)
        {
            workplace.ModifiedAt = DateTime.UtcNow;
            if (persist)
            {
                _workplaces.Upsert(workplace);
            }
        }

        return catalogChanged || linesChanged;
    }

    public RiskCardImportDraft? LoadDraft(string draftId)
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
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RiskCardImportDraft>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public void SaveDraft(RiskCardImportDraft draft)
    {
        Directory.CreateDirectory(_paths.CardImportDraftsFolder);
        var path = DraftPath(draft.Id);
        File.WriteAllText(path, JsonSerializer.Serialize(draft, JsonOpts));
    }

    public void DeleteDraft(string draftId)
    {
        var path = DraftPath(draftId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Применяет выбранные карты. workplaceByItemKey — явный выбор РМ с формы (может перекрыть автоматч).
    /// Вторая карта той же профессии при занятом РМ → создаём новое РМ. Уже внесённые строки не затираем.
    /// </summary>
    public RiskCardImportApplyResult Apply(
        RiskCardImportDraft draft,
        IReadOnlyDictionary<string, string> workplaceByItemKey,
        IReadOnlySet<string> selectedKeys,
        IReadOnlySet<string> overwriteKeys)
    {
        var result = new RiskCardImportApplyResult();
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

            if (workplace is not null && claimedWorkplaceIds.Contains(workplace.Id))
            {
                workplace = null;
            }

            var canOverwrite = overwriteKeys.Contains(item.ItemKey) || item.OverwriteExisting;
            var sameCard = workplace is not null
                           && SameRiskCardNumber(item.Card.CardNumber, workplace.RiskCardNumber);

            // Заполненное РМ с другим № — не затираем, создадим новую карту по профессии.
            if (workplace is not null
                && !workplace.IsGroup
                && HasRiskData(workplace)
                && !sameCard
                && !(canOverwrite && explicitWorkplace))
            {
                workplace = null;
            }

            if ((workplace is null || workplace.IsGroup) && !explicitWorkplace)
            {
                var rematch = FindBestWorkplace(
                    item.Card,
                    _workplaces.LoadAll().Where(w => !w.IsGroup).ToList(),
                    claimedWorkplaceIds,
                    draft.Organization);
                if (rematch.Workplace is not null
                    && !claimedWorkplaceIds.Contains(rematch.Workplace.Id)
                    && (!HasRiskData(rematch.Workplace)
                        || SameRiskCardNumber(item.Card.CardNumber, rematch.Workplace.RiskCardNumber)))
                {
                    workplace = rematch.Workplace;
                }
            }

            if (workplace is null || workplace.IsGroup)
            {
                workplace = TryCreateWorkplaceFromCard(item.Card, draft.Organization, claimedWorkplaceIds);
                if (workplace is not null)
                {
                    result.Created++;
                    item.MatchedWorkplaceId = workplace.Id;
                    item.MatchedWorkplaceName = DisplayName(workplace);
                }
            }

            if (workplace is null || workplace.IsGroup)
            {
                result.Skipped++;
                result.Messages.Add($"Карта № {item.Card.CardNumber} ({item.Card.Profession}): нет рабочего места.");
                continue;
            }

            if (!OrganizationMatchesCardOrSelected(draft.Organization, workplace.Organization, item.Card.Organization))
            {
                result.Skipped++;
                result.Messages.Add($"Карта № {item.Card.CardNumber}: РМ из другой организации.");
                continue;
            }

            if (HasRiskData(workplace)
                && !SameRiskCardNumber(item.Card.CardNumber, workplace.RiskCardNumber)
                && !(canOverwrite && explicitWorkplace))
            {
                result.Skipped++;
                result.Messages.Add(
                    $"Карта № {item.Card.CardNumber} ({item.Card.Profession}): «{DisplayName(workplace)}» уже заполнена — не перезаписана.");
                continue;
            }

            ApplyCardToWorkplace(workplace, item.Card, mergePreserveExisting: !canOverwrite);
            _workplaces.Upsert(workplace);
            claimedWorkplaceIds.Add(workplace.Id);

            result.Applied++;
            result.Messages.Add(
                $"Карта № {item.Card.CardNumber} → «{DisplayName(workplace)}»: {workplace.RiskLines?.Count(l => !IsEmptyLine(l)) ?? 0} опасностей.");
        }

        return result;
    }

    private static (RiskHazardCatalogItem Item, bool Created) ResolveOrCreateCatalogItem(
        ParsedRiskCardLine line,
        Dictionary<string, RiskHazardCatalogItem> byNumber,
        Dictionary<string, RiskHazardCatalogItem> byTitle,
        List<RiskHazardCatalogItem> catalog)
    {
        var number = NormalizeHazardNumber(line.HazardNumber);
        var title = (line.HazardTitle ?? string.Empty).Trim();
        var titleKey = NormalizeTitleKey(title);

        if (number.Length > 0 && byNumber.TryGetValue(number, out var byNum))
        {
            if (string.IsNullOrWhiteSpace(byNum.Number))
            {
                byNum.Number = number;
            }

            return (byNum, false);
        }

        if (titleKey.Length > 0 && byTitle.TryGetValue(titleKey, out var byName))
        {
            if (string.IsNullOrWhiteSpace(byName.Number) && number.Length > 0)
            {
                byName.Number = number;
                byNumber[number] = byName;
            }

            return (byName, false);
        }

        if (titleKey.Length >= 12)
        {
            var soft = catalog.FirstOrDefault(c =>
            {
                var key = NormalizeTitleKey(c.Title);
                return key.Length >= 12 &&
                       (key.Contains(titleKey, StringComparison.Ordinal) ||
                        titleKey.Contains(key, StringComparison.Ordinal));
            });
            if (soft is not null)
            {
                if (string.IsNullOrWhiteSpace(soft.Number) && number.Length > 0)
                {
                    soft.Number = number;
                    byNumber[number] = soft;
                }

                return (soft, false);
            }
        }

        var created = new RiskHazardCatalogItem
        {
            Number = number,
            Title = title,
            DefaultMeasures = line.ControlMeasures ?? string.Empty,
            DefaultWhoMaySuffer = "работник",
            DefaultInitialRisk = string.IsNullOrWhiteSpace(line.InitialRisk) ? "Средний" : line.InitialRisk,
            DefaultResidualRisk = MapResidual(line.ResidualRisk) ?? "допустимый риск",
            IsActive = true
        };
        catalog.Add(created);
        if (number.Length > 0)
        {
            byNumber[number] = created;
        }

        if (titleKey.Length > 0)
        {
            byTitle[titleKey] = created;
        }

        return (created, true);
    }

    private static WorkplaceRiskLine ToWorkplaceLine(
        ParsedRiskCardLine line,
        RiskHazardCatalogItem? catalogItem)
    {
        var number = NormalizeHazardNumber(line.HazardNumber);
        if (catalogItem is not null && string.IsNullOrWhiteSpace(number))
        {
            number = NormalizeHazardNumber(catalogItem.Number);
        }

        return new WorkplaceRiskLine
        {
            HazardCatalogId = catalogItem?.Id ?? string.Empty,
            HazardNumber = number,
            HazardTitle = string.IsNullOrWhiteSpace(line.HazardTitle)
                ? (catalogItem?.Title ?? string.Empty)
                : line.HazardTitle,
            Source = line.Source,
            Severity = MapToOption(line.Severity, WorkplaceRiskScale.SeverityOptions) ?? line.Severity,
            Probability = MapToOption(line.Probability, WorkplaceRiskScale.ProbabilityOptions) ?? line.Probability,
            InitialRisk = line.InitialRisk,
            ControlMeasures = string.IsNullOrWhiteSpace(line.ControlMeasures)
                ? (catalogItem?.DefaultMeasures ?? string.Empty)
                : line.ControlMeasures,
            ResidualRisk = MapResidual(line.ResidualRisk) ?? line.ResidualRisk,
            Consequences = string.Empty,
            WhoMaySuffer = catalogItem?.DefaultWhoMaySuffer ?? string.Empty,
            Responsible = string.Empty
        };
    }

    private static string NormalizeTitleKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var t = value.Trim().ToLowerInvariant().Replace('ё', 'е');
        return string.Join(' ', t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void NormalizeCardScales(ParsedRiskCard card)
    {
        card.OverallRisk = NormalizeOverallRisk(card.OverallRisk);
        foreach (var line in card.Lines)
        {
            line.Severity = MapToOption(line.Severity, WorkplaceRiskScale.SeverityOptions) ?? line.Severity;
            line.Probability = MapToOption(line.Probability, WorkplaceRiskScale.ProbabilityOptions) ?? line.Probability;
            line.ResidualRisk = MapResidual(line.ResidualRisk) ?? line.ResidualRisk;
            line.HazardNumber = NormalizeHazardNumber(line.HazardNumber);
        }
    }

    private static string NormalizeOverallRisk(string? raw)
    {
        var t = (raw ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
        if (t.Contains("высок", StringComparison.Ordinal))
        {
            return "Высокий";
        }

        if (t.Contains("средн", StringComparison.Ordinal))
        {
            return "Средний";
        }

        if (t.Contains("низк", StringComparison.Ordinal) || t.Contains("допустим", StringComparison.Ordinal))
        {
            return "Низкий";
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase((raw ?? string.Empty).Trim().ToLowerInvariant());
    }

    private static string? MapResidual(string? raw)
    {
        var t = NormalizeCompareKey(raw);
        if (t.Length == 0)
        {
            return null;
        }

        if (t.Contains("недопустим", StringComparison.Ordinal))
        {
            return "недопустимый риск";
        }

        if (t.Contains("допустим", StringComparison.Ordinal))
        {
            return "допустимый риск";
        }

        return MapToOption(raw, WorkplaceRiskScale.ResidualRiskOptions);
    }

    /// <summary>Сопоставление тяжести/вероятности с учётом отличий формулировок в Word.</summary>
    public static string? MapToOption(string? raw, IReadOnlyList<string> options)
    {
        if (ReferenceEquals(options, WorkplaceRiskScale.SeverityOptions)
            || (options.Count == WorkplaceRiskScale.SeverityOptions.Count
                && options.Count > 0
                && string.Equals(options[0], WorkplaceRiskScale.SeverityOptions[0], StringComparison.Ordinal)))
        {
            return WorkplaceRiskScale.MapSeverity(raw);
        }

        if (ReferenceEquals(options, WorkplaceRiskScale.ProbabilityOptions)
            || (options.Count == WorkplaceRiskScale.ProbabilityOptions.Count
                && options.Count > 0
                && string.Equals(options[0], WorkplaceRiskScale.ProbabilityOptions[0], StringComparison.Ordinal)))
        {
            return WorkplaceRiskScale.MapProbability(raw);
        }

        var key = NormalizeCompareKey(raw);
        if (key.Length == 0)
        {
            return null;
        }

        foreach (var opt in options)
        {
            if (string.Equals(NormalizeCompareKey(opt), key, StringComparison.Ordinal))
            {
                return opt;
            }
        }

        string? best = null;
        var bestScore = 0;
        foreach (var opt in options)
        {
            var optKey = NormalizeCompareKey(opt);
            if (key.Contains(optKey, StringComparison.Ordinal) || optKey.Contains(key, StringComparison.Ordinal))
            {
                var score = Math.Min(key.Length, optKey.Length);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = opt;
                }
            }
        }

        return best;
    }

    private static string NormalizeCompareKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant().Replace('ё', 'е'))
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string NormalizeHazardNumber(string? raw)
    {
        var t = (raw ?? string.Empty).Trim().TrimEnd('.');
        return string.Join(' ', t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record MatchResult(WorkplaceRecord? Workplace, string Note);
    private sealed record CardMatchResult(ParsedRiskCard? Card, string Note);

    private static bool OrganizationMatchesCardOrSelected(
        string selectedOrg,
        string workplaceOrg,
        string cardOrg)
    {
        if (string.Equals(selectedOrg, workplaceOrg, StringComparison.OrdinalIgnoreCase) ||
            OrganizationMatcher.Matches(selectedOrg, workplaceOrg))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(cardOrg) &&
            (OrganizationMatcher.Matches(cardOrg, workplaceOrg) ||
             string.Equals(cardOrg, workplaceOrg, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static CardMatchResult FindBestCardForWorkplace(
        WorkplaceRecord workplace,
        IReadOnlyList<ParsedRiskCard> cards)
    {
        var profession = ProfessionMatcher.Normalize(
            string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession);

        var ranked = cards
            .Select(card =>
            {
                var score = ScoreProfessionMatch(profession, card.Profession);
                if (score <= 0)
                {
                    return (Card: card, Score: 0);
                }

                if (!string.IsNullOrWhiteSpace(card.Organization) &&
                    (OrganizationMatcher.Matches(card.Organization, workplace.Organization) ||
                     string.Equals(card.Organization, workplace.Organization, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 40;
                }

                if (!string.IsNullOrWhiteSpace(card.Department) &&
                    !string.IsNullOrWhiteSpace(workplace.Department) &&
                    string.Equals(
                        ProfessionMatcher.Normalize(card.Department),
                        ProfessionMatcher.Normalize(workplace.Department),
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                }

                if (!string.IsNullOrWhiteSpace(card.CardNumber) &&
                    !string.IsNullOrWhiteSpace(workplace.RiskCardNumber) &&
                    string.Equals(
                        card.CardNumber.TrimStart('0'),
                        workplace.RiskCardNumber.Trim().TrimStart('0'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }

                return (Card: card, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        if (ranked.Count == 0)
        {
            return new CardMatchResult(null, "нет совпадения по профессии");
        }

        return new CardMatchResult(ranked[0].Card, "ok");
    }

    private static int ScoreProfessionMatch(string workplaceProfession, string? cardProfession)
    {
        var name = ProfessionMatcher.Normalize(cardProfession);
        if (workplaceProfession.Length == 0 || name.Length == 0)
        {
            return 0;
        }

        if (ProfessionMatcher.Matches(workplaceProfession, name) ||
            ProfessionMatcher.Matches(name, workplaceProfession))
        {
            return 100;
        }

        var ca = ProfessionMatcher.CanonicalKey(workplaceProfession);
        var cb = ProfessionMatcher.CanonicalKey(name);
        if (ca.Length > 0 && string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (ProfessionMatcher.MatchesWithAcronyms(workplaceProfession, name))
        {
            return 95;
        }

        if (ProfessionMatcher.CoreKey(workplaceProfession).Length > 0 &&
            string.Equals(
                ProfessionMatcher.CoreKey(workplaceProfession),
                ProfessionMatcher.CoreKey(name),
                StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        return 0;
    }

    private WorkplaceRecord? TryCreateWorkplaceFromCard(
        ParsedRiskCard card,
        string preferOrganization,
        IReadOnlySet<string>? claimedWorkplaceIds = null)
    {
        var profession = ProfessionMatcher.Normalize(card.Profession);
        if (profession.Length == 0)
        {
            return null;
        }

        var org = ResolveCardOrganization(card.Organization, preferOrganization);
        if (string.IsNullOrWhiteSpace(org))
        {
            return null;
        }

        // Подразделение: из Word, иначе у «соседа» той же профессии (уже занятого в этом импорте).
        var department = !string.IsNullOrWhiteSpace(card.Department)
            ? card.Department.Trim()
            : ResolveDepartmentHint(profession, org, claimedWorkplaceIds);

        // Всегда создаём новую строку — не возвращаем уже существующее РМ
        // (иначе 2-я карта той же профессии попадает в заполненную первую).
        return SoutWorkplaceStaffSync.AddWorkplace(
            org,
            _workplaces,
            profession,
            profession,
            department,
            workplaceNumber: null,
            "2");
    }

    private string ResolveDepartmentHint(
        string profession,
        string organization,
        IReadOnlySet<string>? preferWorkplaceIds)
    {
        var pool = _workplaces.LoadByOrganization(organization).Where(w => !w.IsGroup).ToList();
        WorkplaceRecord? sibling = null;
        if (preferWorkplaceIds is { Count: > 0 })
        {
            sibling = pool.FirstOrDefault(w =>
                preferWorkplaceIds.Contains(w.Id)
                && ScoreProfessionMatch(
                    ProfessionMatcher.Normalize(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession),
                    profession) >= 100);
        }

        sibling ??= pool
            .Where(w => ScoreProfessionMatch(
                ProfessionMatcher.Normalize(string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession),
                profession) >= 100)
            .OrderByDescending(w => !string.IsNullOrWhiteSpace(w.Department))
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(sibling?.Department))
        {
            return sibling!.Department.Trim();
        }

        return "Без подразделения";
    }

    private static string ResolveCardOrganization(string? cardOrganization, string preferOrganization)
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

    private static MatchResult FindBestWorkplace(
        ParsedRiskCard card,
        IReadOnlyList<WorkplaceRecord> workplaces,
        HashSet<string> usedIds,
        string? preferOrganization = null)
    {
        var profession = ProfessionMatcher.Normalize(card.Profession);
        if (profession.Length == 0)
        {
            return new MatchResult(null, "В файле не найдена должность/профессия.");
        }

        var preferOrg = (preferOrganization ?? string.Empty).Trim();
        var pool = workplaces.Where(w => !usedIds.Contains(w.Id)).ToList();
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
                var score = ScoreProfessionMatch(name, profession);
                if (score <= 0)
                {
                    return (Workplace: w, Score: 0, Name: name);
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

                if (!string.IsNullOrWhiteSpace(card.Department) &&
                    !string.IsNullOrWhiteSpace(w.Department) &&
                    string.Equals(
                        ProfessionMatcher.Normalize(card.Department),
                        ProfessionMatcher.Normalize(w.Department),
                        StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                }

                if (SameRiskCardNumber(card.CardNumber, w.RiskCardNumber))
                {
                    score += 200;
                }

                // Пустые заготовки из штата заполняем раньше уже заполненных карт.
                if (!HasRiskData(w))
                {
                    score += 80;
                }

                return (Workplace: w, Score: score, Name: name);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => SameRiskCardNumber(card.CardNumber, x.Workplace.RiskCardNumber))
            .ThenByDescending(x => !HasRiskData(x.Workplace))
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return new MatchResult(null, $"Нет свободного РМ «{profession}» — будет создано.");
        }

        var best = candidates[0];
        var note = SameRiskCardNumber(card.CardNumber, best.Workplace.RiskCardNumber)
            ? $"Авто по № карты: «{DisplayName(best.Workplace)}»"
            : HasRiskData(best.Workplace)
                ? $"Авто (уже есть данные): «{DisplayName(best.Workplace)}»"
                : $"Авто: «{DisplayName(best.Workplace)}»";
        return new MatchResult(best.Workplace, note);
    }

    private static bool HasRiskData(WorkplaceRecord w)
    {
        if (w.RiskAssessmentDate is not null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(w.RiskCardNumber))
        {
            return true;
        }

        return (w.RiskLines?.Count(l => !IsEmptyLine(l)) ?? 0) > 0;
    }

    private static bool SameRiskCardNumber(string? left, string? right)
    {
        var a = NormalizeRiskCardNumber(left);
        var b = NormalizeRiskCardNumber(right);
        return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRiskCardNumber(string? value)
    {
        var t = (value ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return string.Empty;
        }

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

    private static string DisplayName(WorkplaceRecord w)
    {
        var prof = string.IsNullOrWhiteSpace(w.Profession) ? w.Name : w.Profession;
        if (!string.IsNullOrWhiteSpace(w.Department))
        {
            return $"{prof} / {w.Department}";
        }

        return prof;
    }

    private static bool IsEmptyLine(WorkplaceRiskLine line)
    {
        static bool Empty(string? s) =>
            string.IsNullOrWhiteSpace(s) || s.Trim() is "-" or "—" or "–";

        return Empty(line.HazardTitle) && Empty(line.HazardNumber) && Empty(line.Source);
    }

    private string DraftPath(string draftId)
    {
        var safe = Regex.Replace(draftId, @"[^a-zA-Z0-9]", string.Empty);
        if (safe.Length == 0)
        {
            safe = Guid.NewGuid().ToString("N");
        }

        return Path.Combine(_paths.CardImportDraftsFolder, $"{safe}.json");
    }

    private void CleanupOldDrafts()
    {
        try
        {
            var cutoff = DateTime.UtcNow - DraftTtl;
            foreach (var file in Directory.EnumerateFiles(_paths.CardImportDraftsFolder, "*.json"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // ignore cleanup errors
        }
    }
}

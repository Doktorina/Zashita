using System.Data;
using Microsoft.Extensions.Options;
using Zashita.Core.Employees;
using Zashita.Core.Excel;
using Zashita.Web.Services;

namespace Zashita.Web.Services.Connections;

/// <summary>
/// Простой режим: один Excel со всеми организациями → upsert в organization-employees.json.
/// </summary>
public sealed class OneCEmployeeSyncService
{
    private readonly ExcelWorkbookStore _excel;
    private readonly OrganizationEmployeesStore _employees;
    private readonly OrganizationStore _orgs;
    private readonly OneCSyncLogStore _log;
    private readonly StaffingChangeStore _staffingChanges;
    private readonly StaffingChangeResetService _staffingReset;
    private readonly MedDataOrganizationAligner _medAligner;
    private readonly IServiceProvider _services;
    private readonly IWebHostEnvironment _env;
    private readonly OneCOptions _options;
    private readonly ILogger<OneCEmployeeSyncService> _logger;
    private readonly object _gate = new();

    public OneCEmployeeSyncService(
        ExcelWorkbookStore excel,
        OrganizationEmployeesStore employees,
        OrganizationStore orgs,
        OneCSyncLogStore log,
        StaffingChangeStore staffingChanges,
        StaffingChangeResetService staffingReset,
        MedDataOrganizationAligner medAligner,
        IServiceProvider services,
        IWebHostEnvironment env,
        IOptions<OneCOptions> options,
        ILogger<OneCEmployeeSyncService> logger)
    {
        _excel = excel;
        _employees = employees;
        _orgs = orgs;
        _log = log;
        _staffingChanges = staffingChanges;
        _staffingReset = staffingReset;
        _medAligner = medAligner;
        _services = services;
        _env = env;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>false — демо/локально: синк 1С выключен, к SharedPath не ходим.</summary>
    public bool IsEnabled => _options.Enabled;

    /// <summary>Серверный путь OneC:SharedPath (по умолчанию /srv/share). Без локального inbox/UNC.</summary>
    public string ConfiguredSharePath =>
        IsEnabled ? ResolveServerSharePath(_options.SharedPath) : string.Empty;

    /// <summary>Тот же серверный путь — выгрузка только оттуда.</summary>
    public string EffectiveSharePath => ConfiguredSharePath;

    /// <summary>true, если синк выключен или серверная папка/файл недоступны.</summary>
    public bool SharePathMissing
    {
        get
        {
            if (!IsEnabled)
            {
                return true;
            }

            var path = ConfiguredSharePath;
            try
            {
                return !(Directory.Exists(path) || File.Exists(path));
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>Локальный кэш после успешного синка с сервера (не источник штата).</summary>
    public string WorkersFilePath =>
        Path.Combine(_env.ContentRootPath, "App_Data", OneCSyncContract.WorkersFileName);

    /// <summary>Файлы выгрузки только из серверного SharedPath.</summary>
    public IReadOnlyList<FileInfo> FindSharedFiles()
    {
        if (!IsEnabled)
        {
            return [];
        }

        var path = ConfiguredSharePath;
        if (SharePathMissing)
        {
            _logger.LogError(
                "OneC SharedPath «{Path}» недоступен (нет папки или прав у процесса). Синк 1С пропущен.",
                path);
            return [];
        }

        try
        {
            if (File.Exists(path) &&
                OneCSyncContract.SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                return [new FileInfo(path)];
            }

            if (!Directory.Exists(path))
            {
                return [];
            }

            return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                .Where(f => OneCSyncContract.SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .Where(f => f.Exists && f.Length > 0)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось прочитать файлы 1С из «{Path}»", path);
            return [];
        }
    }

    /// <summary>
    /// Только серверный путь. UNC (\\...), App_Data/inbox и пустые значения отклоняются → /srv/share.
    /// </summary>
    internal static string ResolveServerSharePath(string? raw)
    {
        var path = (raw ?? string.Empty).Trim()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (path.Length == 0)
        {
            return OneCSyncContract.DefaultSharedPath;
        }

        // UNC / SMB локальной сети — запрещены.
        if (path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal))
        {
            return OneCSyncContract.DefaultSharedPath;
        }

        // Старый локальный inbox и любые пути под App_Data — не источник 1С.
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("inbox/1c", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/App_Data/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("App_Data/", StringComparison.OrdinalIgnoreCase))
        {
            return OneCSyncContract.DefaultSharedPath;
        }

        return path;
    }

    /// <summary>
    /// Актуальная партия CSV: всегда оба живых файла
    /// (Employees_zup.csv + Employees_zup_anzhela.csv), плюс свежие неархивные за тот же день.
    /// Раньше окно 36 ч отбрасывало второй ЗУП, если его выгрузили позже — люди «пропадали»
    /// из своих компаний при полной перезаписи.
    /// </summary>
    public static IReadOnlyList<FileInfo> SelectCurrentCsvBatch(IReadOnlyList<FileInfo> files)
    {
        var csv = files
            .Where(f => f.Extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            .Where(f => !OneCSyncContract.IsDatedArchiveCsv(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
        if (csv.Count == 0)
        {
            return [];
        }

        var selected = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        // 1) Оба актуальных файла ЗУП — всегда, независимо от разницы дат.
        foreach (var live in csv.Where(f => OneCSyncContract.IsCurrentLiveCsv(f.Name)))
        {
            selected[live.FullName] = live;
        }

        // 2) Прочие неархивные CSV того же дня / ±36 ч (на случай доп. выгрузок).
        var anchor = selected.Values
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault() ?? csv[0];
        var newestUtc = anchor.LastWriteTimeUtc;
        var newestLocalDay = anchor.LastWriteTime.Date;
        foreach (var f in csv)
        {
            if (selected.ContainsKey(f.FullName))
            {
                continue;
            }

            if (OneCSyncContract.IsCurrentLiveCsv(f.Name))
            {
                selected[f.FullName] = f;
                continue;
            }

            if (f.LastWriteTime.Date == newestLocalDay
                || newestUtc - f.LastWriteTimeUtc <= TimeSpan.FromHours(36))
            {
                selected[f.FullName] = f;
            }
        }

        // Если живых имён нет (старый каталог только с датированными) — прежнее окно по дате.
        if (selected.Count == 0)
        {
            var fallbackNewest = csv[0];
            return csv
                .Where(f => f.LastWriteTime.Date == fallbackNewest.LastWriteTime.Date
                            || fallbackNewest.LastWriteTimeUtc - f.LastWriteTimeUtc
                            <= TimeSpan.FromHours(36))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
        }

        return selected.Values
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
    }

    /// <summary>Найти один актуальный .xlsx/.xls/.csv в SharedPath или inbox.</summary>
    public FileInfo? FindNewestSharedFile() => FindSharedFiles().FirstOrDefault();

    /// <summary>Индекс уволенных по CSV 1С (без активных трудовых отношений в организации).</summary>
    public OneCDismissedEmployeeIndex? BuildDismissedIndex()
    {
        var files = SelectCurrentCsvBatch(FindSharedFiles())
            .Select(f => f.FullName)
            .ToList();
        return files.Count == 0 ? null : OneCDismissedEmployeeIndex.BuildFromCsvFiles(files);
    }

    /// <summary>
    /// Полная замена: очистить сотрудников и организации, загрузить все CSV/Excel из SharedPath
    /// (Employees_zup.csv + Employees_zup_anzhela.csv и т.п.).
    /// </summary>
    public OneCSyncRunLog ReplaceFromShared(string source = "shared-replace")
    {
        lock (_gate)
        {
            var run = new OneCSyncRunLog
            {
                Source = source,
                StartedAt = DateTime.UtcNow
            };

            try
            {
                var files = FindSharedFiles();
                if (files.Count == 0)
                {
                    run.Success = false;
                    run.Message = SharePathMissing
                        ? $"Папка 1С «{ConfiguredSharePath}» недоступна (нет каталога или прав у пользователя службы). Ожидаются Employees_zup*.csv на сервере."
                        : $"Нет файлов .csv/.xlsx в «{ConfiguredSharePath}». Ожидаются Employees_zup*.csv.";
                    run.Errors.Add(run.Message);
                    run.FinishedAt = DateTime.UtcNow;
                    _log.Append(run);
                    return run;
                }

                run.FileName = string.Join(", ", files.Select(f => f.Name));

                var csvFiles = SelectCurrentCsvBatch(files).ToList();
                var excelFiles = files
                    .Where(f => !f.Extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                    .Where(f => csvFiles.Count == 0
                                || f.LastWriteTimeUtc >= csvFiles[0].LastWriteTimeUtc.AddHours(-36))
                    .ToList();

                if (csvFiles.Count > 0)
                {
                    run.FileName = string.Join(", ", csvFiles.Select(f => f.Name)
                        .Concat(excelFiles.Select(f => f.Name)));
                }

                // Полная перезапись только если на диске оба живых CSV.
                // Один файл → отмена, штат в App_Data не трогаем.
                var liveOnDisk = files
                    .Where(f => OneCSyncContract.IsCurrentLiveCsv(f.Name))
                    .Select(f => f.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (liveOnDisk.Count > 0)
                {
                    var missingLive = OneCSyncContract.CurrentLiveCsvNames
                        .Where(n => !liveOnDisk.Contains(n))
                        .ToList();
                    if (missingLive.Count > 0)
                    {
                        run.Success = false;
                        run.Message =
                            $"Синк отменён: нужны оба файла ({string.Join(" + ", OneCSyncContract.CurrentLiveCsvNames)}). " +
                            $"Нет: {string.Join(", ", missingLive)}. Текущий штат не изменяли.";
                        run.Errors.Add(run.Message);
                        _logger.LogError("{Message}", run.Message);
                        run.FinishedAt = DateTime.UtcNow;
                        _log.Append(run);
                        return run;
                    }
                }

                var byOrgRows = new Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>>(
                    StringComparer.OrdinalIgnoreCase);

                if (csvFiles.Count > 0)
                {
                    foreach (var (org, rows) in OneCEmployeesCsvParser.ParseFiles(csvFiles.Select(f => f.FullName)))
                    {
                        if (!byOrgRows.TryGetValue(org, out var list))
                        {
                            list = [];
                            byOrgRows[org] = list;
                        }

                        list.AddRange(rows);
                    }
                }

                foreach (var excel in excelFiles)
                {
                    // Когда есть CSV 1С — Excel не подмешиваем: иначе старые строки без
                    // даты увольнения «воскрешают» уже уволенных под другим ключом.
                    if (csvFiles.Count > 0)
                    {
                        _logger.LogInformation(
                            "Excel «{File}» пропущен: штат берём только из CSV 1С ({Count} файл(ов)).",
                            excel.Name,
                            csvFiles.Count);
                        continue;
                    }

                    File.Copy(excel.FullName, WorkersFilePath, overwrite: true);
                    _excel.LoadFromFile(WorkersFilePath);
                    foreach (var (org, rows) in ParseAllSheets())
                    {
                        if (!byOrgRows.TryGetValue(org, out var list))
                        {
                            list = [];
                            byOrgRows[org] = list;
                        }

                        var existingKeys = new HashSet<string>(
                            list.Select(x => OneCEmployeeListParser.BuildMatchKey(org, x.Row)),
                            StringComparer.OrdinalIgnoreCase);
                        foreach (var row in rows)
                        {
                            var key = OneCEmployeeListParser.BuildMatchKey(org, row);
                            if (existingKeys.Add(key))
                            {
                                list.Add((row, true));
                            }
                        }
                    }
                }

                if (byOrgRows.Count == 0)
                {
                    run.Success = false;
                    run.Message = "В файлах не найдено сотрудников.";
                    run.Errors.Add(run.Message);
                    run.FinishedAt = DateTime.UtcNow;
                    _log.Append(run);
                    return run;
                }

                byOrgRows = OneCEmployeeCsvMerge.ConsolidateOrganizations(byOrgRows);
                run.FilteredInactive = byOrgRows.Values
                    .SelectMany(list => list)
                    .Count(pair => !pair.IsActive);
                run.FilteredValidation = byOrgRows.Values
                    .SelectMany(list => list)
                    .Count(pair =>
                        string.IsNullOrWhiteSpace(pair.Row.Name)
                        || pair.Row.EmploymentDate is null);
                // Уволен только в своём ЮЛ; в другом ЮЛ с непрерывным стажем остаётся.
                byOrgRows = OneCEmployeeCsvMerge.ApplyPerLegalEntityDismissalRule(byOrgRows);
                ApplyReplace(byOrgRows, run, hardRebuild: true);
                run.Success = run.Errors.Count == 0;
                if (run.Success)
                {
                    var hr = run.StaffChanges?.Count ?? 0;
                    run.Message =
                        $"Полная перезапись из CSV: организаций {run.OrganizationsTouched}, в штате {run.Updated}, строк +{run.Added}, снято/архив {run.MarkedInactive}"
                        + $", отсеяно неактивных {run.FilteredInactive}, дублей схлопнуто {run.Deduplicated}."
                        + (hr == 0
                            ? " Кадровых изменений не было."
                            : $" Кадровых событий: {hr}.");
                }
                else
                {
                    run.Message = $"Замена с ошибками: {string.Join("; ", run.Errors.Take(3))}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "1C replace sync failed");
                run.Success = false;
                run.Errors.Add(ex.Message);
                run.Message = "Ошибка замены: " + ex.Message;
            }

            run.FinishedAt = DateTime.UtcNow;
            _log.Append(run);
            return run;
        }
    }

    /// <summary>Взять файл с серверного SharedPath, скопировать в workers.xlsx и upsert.</summary>
    public OneCSyncRunLog SyncFromShared(string source = "shared")
    {
        // CSV-выгрузки из ЗУП — всегда полная замена из всех файлов папки.
        var files = FindSharedFiles();
        if (files.Any(f => f.Extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            return ReplaceFromShared(source);
        }

        lock (_gate)
        {
            var newest = files.FirstOrDefault();
            if (newest is not null)
            {
                var appData = Path.Combine(_env.ContentRootPath, "App_Data");
                Directory.CreateDirectory(appData);
                File.Copy(newest.FullName, WorkersFilePath, overwrite: true);
                return SyncFromWorkersFile(source, newest.Name);
            }

            var empty = new OneCSyncRunLog
            {
                Success = false,
                Source = source,
                Message = SharePathMissing
                    ? $"Папка 1С «{ConfiguredSharePath}» недоступна. Проверьте mount/права пользователя службы на {OneCSyncContract.DefaultSharedPath}."
                    : $"Нет файла .xlsx/.xls/.csv в «{ConfiguredSharePath}». Положите Employees_zup*.csv на сервер в эту папку.",
                FinishedAt = DateTime.UtcNow
            };
            _log.Append(empty);
            return empty;
        }
    }

    /// <summary>Upsert из текущего workers.xlsx.</summary>
    public OneCSyncRunLog SyncFromWorkersFile(string source = "workers.xlsx", string? originalFileName = null)
    {
        lock (_gate)
        {
            var run = new OneCSyncRunLog
            {
                Source = source,
                FileName = originalFileName ?? OneCSyncContract.WorkersFileName,
                StartedAt = DateTime.UtcNow
            };

            try
            {
                if (!File.Exists(WorkersFilePath))
                {
                    run.Success = false;
                    run.Message = "Файл workers.xlsx не найден.";
                    run.Errors.Add(run.Message);
                    run.FinishedAt = DateTime.UtcNow;
                    _log.Append(run);
                    return run;
                }

                _excel.LoadFromFile(WorkersFilePath);
                var byOrg = ParseAllSheets();
                if (byOrg.Count == 0)
                {
                    run.Success = false;
                    run.Message = "В файле не найдено сотрудников по контракту 1С.";
                    run.Errors.Add(run.Message);
                    run.FinishedAt = DateTime.UtcNow;
                    _log.Append(run);
                    return run;
                }

                ApplyUpsert(byOrg, run);
                run.Success = run.Errors.Count == 0;
                run.Message = run.Success
                    ? $"Синхронизация завершена: +{run.Added}, обновлено {run.Updated}, неактивных {run.MarkedInactive}."
                    : $"Синхронизация с ошибками: {string.Join("; ", run.Errors.Take(3))}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "1C sync failed");
                run.Success = false;
                run.Errors.Add(ex.Message);
                run.Message = "Ошибка синхронизации: " + ex.Message;
            }

            run.FinishedAt = DateTime.UtcNow;
            _log.Append(run);
            return run;
        }
    }

    private Dictionary<string, List<OneCEmployeeRow>> ParseAllSheets()
    {
        var result = new Dictionary<string, List<OneCEmployeeRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheetName in _excel.SheetNames)
        {
            DataTable? table;
            try
            {
                table = _excel.GetSheet(sheetName);
            }
            catch
            {
                continue;
            }

            if (table is null || !OneCEmployeeListParser.TryParse(table, out var org, out var rows))
            {
                continue;
            }

            var organization = string.IsNullOrWhiteSpace(org) ? sheetName.Trim() : org.Trim();
            if (string.IsNullOrWhiteSpace(organization) || rows.Count == 0)
            {
                continue;
            }

            if (!result.TryGetValue(organization, out var list))
            {
                list = [];
                result[organization] = list;
            }

            list.AddRange(rows);
        }

        return result;
    }

    private void ApplyUpsert(Dictionary<string, List<OneCEmployeeRow>> byOrg, OneCSyncRunLog run)
    {
        var syncedAt = DateTime.UtcNow;
        run.OrganizationsTouched = byOrg.Count;
        var staffingEvents = new List<StaffingChangeEvent>();

        foreach (var (organizationFromFile, incomingRows) in byOrg)
        {
            var organization = PreferIpFullNameStaffKey(
                organizationFromFile,
                _orgs.EnsureSyncedOrganization(organizationFromFile));

            var existing = _employees.Get(organization, includeInactive: true).ToList();
            // также подтянуть со старого ключа из файла, если ещё не смержен
            if (!string.Equals(organization, organizationFromFile, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var extra in _employees.Get(organizationFromFile, includeInactive: true))
                {
                    if (existing.All(e => !string.Equals(e.Id, extra.Id, StringComparison.Ordinal)
                                          && !string.Equals(e.Name, extra.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Add(extra);
                    }
                }
            }

            var byKey = new Dictionary<string, OrganizationEmployee>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in existing)
            {
                var key = OneCEmployeeListParser.BuildMatchKey(organization, e);
                byKey[key] = e;
            }

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var next = new List<OrganizationEmployee>();

            foreach (var row in incomingRows)
            {
                if (string.IsNullOrWhiteSpace(row.Name))
                {
                    run.Skipped++;
                    continue;
                }

                var key = OneCEmployeeListParser.BuildMatchKey(organization, row);
                if (!seenKeys.Add(key))
                {
                    run.Skipped++;
                    continue;
                }

                if (byKey.TryGetValue(key, out var found))
                {
                    var oldProfession = found.Profession?.Trim() ?? string.Empty;
                    var oldDepartment = found.Department?.Trim() ?? string.Empty;
                    var wasInactive = !found.IsActive;
                    var newProfession = string.IsNullOrWhiteSpace(row.Profession)
                        ? oldProfession
                        : row.Profession.Trim();
                    var newDepartment = string.IsNullOrWhiteSpace(row.Department)
                        ? oldDepartment
                        : row.Department.Trim();

                    OneCEmployeeMapper.MergeRow(found, row with
                    {
                        Profession = newProfession,
                        Department = newDepartment
                    }, syncedAt);
                    if (!EmployeeHrStatus.IsEmployed(found))
                    {
                        found.IsActive = false;
                        if (string.IsNullOrWhiteSpace(found.HrStatus))
                        {
                            found.HrStatus = "Увольнение";
                        }

                        next.Add(found);
                        run.MarkedInactive++;
                        continue;
                    }

                    next.Add(found);
                    run.Updated++;

                    if (wasInactive)
                    {
                        staffingEvents.Add(new StaffingChangeEvent
                        {
                            Kind = StaffingChangeKind.Hired,
                            Organization = organization,
                            Person = found.Name,
                            Snils = found.Snils,
                            NewProfession = newProfession,
                            NewDepartment = newDepartment,
                            Message = "Сотрудник снова появился в выгрузке 1С (ранее был неактивен)."
                        });
                    }
                    else if (!ProfessionSame(oldProfession, newProfession)
                             || !ProfessionSame(oldDepartment, newDepartment))
                    {
                        var transfer = new StaffingChangeEvent
                        {
                            Kind = StaffingChangeKind.Transferred,
                            Organization = organization,
                            Person = found.Name,
                            Snils = found.Snils,
                            OldProfession = oldProfession,
                            NewProfession = newProfession,
                            OldDepartment = oldDepartment,
                            NewDepartment = newDepartment,
                            Message = $"Перевод в «{organization}»: «{oldProfession}» → «{newProfession}»."
                        };
                        _staffingReset.ApplyTransferReset(transfer);
                        staffingEvents.Add(transfer);
                    }
                }
                else
                {
                    var hired = new OrganizationEmployee
                    {
                        Id = Guid.NewGuid().ToString("N")
                    };
                    OneCEmployeeMapper.ApplyRow(hired, row, syncedAt);
                    if (!EmployeeHrStatus.IsEmployed(hired))
                    {
                        hired.IsActive = false;
                        if (string.IsNullOrWhiteSpace(hired.HrStatus))
                        {
                            hired.HrStatus = "Увольнение";
                        }

                        next.Add(hired);
                        run.MarkedInactive++;
                        continue;
                    }

                    next.Add(hired);
                    run.Added++;
                    staffingEvents.Add(new StaffingChangeEvent
                    {
                        Kind = StaffingChangeKind.Hired,
                        Organization = organization,
                        Person = hired.Name,
                        Snils = hired.Snils,
                        NewProfession = hired.Profession,
                        NewDepartment = hired.Department,
                        Message = "Новый сотрудник из выгрузки 1С."
                    });
                }
            }

            foreach (var old in existing)
            {
                var key = OneCEmployeeListParser.BuildMatchKey(organization, old);
                if (seenKeys.Contains(key))
                {
                    continue;
                }

                if (old.IsActive)
                {
                    staffingEvents.Add(new StaffingChangeEvent
                    {
                        Kind = StaffingChangeKind.Fired,
                        Organization = organization,
                        Person = old.Name,
                        Snils = old.Snils,
                        OldProfession = old.Profession,
                        OldDepartment = old.Department,
                        Message = "Отсутствует в последней выгрузке 1С (уволен / неактуален)."
                    });
                }

                old.IsActive = false;
                if (string.IsNullOrWhiteSpace(old.HrStatus) || !EmployeeHrStatus.IsDismissed(old.HrStatus))
                {
                    old.HrStatus = "Увольнение";
                }

                next.Add(old);
                run.MarkedInactive++;
            }

            _employees.Save(organization, next);
        }

        // Уволенных с датой из 1С не чистим — нужны для архива обучения.
        _employees.PurgeDuplicatePersonShells();
        PurgeDismissedFromActiveLists();

        if (staffingEvents.Count > 0)
        {
            StaffingChangeEvent.EnrichCrossOrganizationTransfers(staffingEvents);
            run.AttachStaffChanges(staffingEvents);
            _staffingChanges.Append(staffingEvents);
        }

        ApplyLegalEntitySplitsAfterSync();
    }

    private void ApplyReplace(
        Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>> byOrg,
        OneCSyncRunLog run,
        bool hardRebuild = false)
    {
        var syncedAt = DateTime.UtcNow;
        run.OrganizationsTouched = byOrg.Count;
        var nextData = new Dictionary<string, List<OrganizationEmployee>>(StringComparer.OrdinalIgnoreCase);
        var staffingEvents = new List<StaffingChangeEvent>();
        // Снимок до замены: Id/адрес при совпадении ключа; при hardRebuild «призраков» вне CSV не тащим.
        var previousSnapshot = _employees.SnapshotIncludingInactive();

        foreach (var (organizationFromFile, incomingRows) in byOrg)
        {
            var organization = PreferIpFullNameStaffKey(
                organizationFromFile,
                _orgs.EnsureSyncedOrganization(organizationFromFile));

            var seenKeys = new Dictionary<string, (OneCEmployeeRow Row, bool IsActive)>(StringComparer.OrdinalIgnoreCase);
            var next = new List<OrganizationEmployee>();
            var previous = previousSnapshot
                .Where(pair => OrganizationMatcher.Matches(pair.Key, organization)
                               || OrganizationMatcher.Matches(pair.Key, organizationFromFile)
                               || string.Equals(pair.Key, organization, StringComparison.OrdinalIgnoreCase)
                               || string.Equals(pair.Key, organizationFromFile, StringComparison.OrdinalIgnoreCase))
                .SelectMany(pair => pair.Value)
                .GroupBy(
                    e => OneCEmployeeListParser.BuildMatchKey(organization, e),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // При дублях в CSV (увольнение + работа) оставляем более «актуальную» строку.
            foreach (var (row, isActive) in incomingRows)
            {
                if (string.IsNullOrWhiteSpace(row.Name))
                {
                    run.Skipped++;
                    continue;
                }

                var key = OneCEmployeeListParser.BuildMatchKey(organization, row);
                if (seenKeys.TryGetValue(key, out var existing))
                {
                    if (OneCEmployeeCsvMerge.PreferIncomingEmployee(row, isActive, existing.Row, existing.IsActive))
                    {
                        seenKeys[key] = (row, isActive);
                    }
                    else
                    {
                        run.Skipped++;
                    }

                    continue;
                }

                seenKeys[key] = (row, isActive);
            }

            // Люди, уже попавшие в CSV (по СНИЛС/ФИО) — чтобы не плодить ложный «приём»/«увольнение»
            // при смене даты приёма или должности в ключе BuildMatchKey.
            var peopleStillInCsv = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, (row, _)) in seenKeys)
            {
                peopleStillInCsv.Add(PersonIdentityKey(organization, row.Snils, row.ExternalId, row.Name));
            }

            foreach (var (key, (row, isActive)) in seenKeys)
            {
                previous.TryGetValue(key, out var old);
                // Тот же человек уже был в штате, но ключ трудоустройства другой (дата/вид занятости).
                old ??= FindPreviousSamePerson(previous.Values, organization, row);
                var address = !string.IsNullOrWhiteSpace(row.HomeAddress)
                    ? row.HomeAddress.Trim()
                    : old?.HomeAddress ?? "";
                var newProfession = row.Profession?.Trim() ?? "";
                var newDepartment = row.Department?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(newProfession) && old is not null)
                {
                    newProfession = old.Profession?.Trim() ?? "";
                }

                if (string.IsNullOrWhiteSpace(newDepartment) && old is not null)
                {
                    newDepartment = old.Department?.Trim() ?? "";
                }

                // Уволенные из CSV всегда оставляем в справочнике (архив обучения).
                // Нельзя отфильтровывать по IsEmployed — иначе они пропадают из App_Data.
                if (!isActive)
                {
                    run.MarkedInactive++;
                    if (old is { IsActive: true })
                    {
                        staffingEvents.Add(new StaffingChangeEvent
                        {
                            Kind = StaffingChangeKind.Fired,
                            Organization = organization,
                            Person = row.Name.Trim(),
                            Snils = OneCEmployeeListParser.NormalizeSnils(row.Snils),
                            OldProfession = old.Profession,
                            OldDepartment = old.Department,
                            Message = "Уволен / неактивен по данным выгрузки 1С."
                        });
                    }

                    var dismissed = new OrganizationEmployee
                    {
                        Id = old?.Id ?? Guid.NewGuid().ToString("N")
                    };
                    OneCEmployeeMapper.ApplyRow(dismissed, row with
                    {
                        Profession = newProfession,
                        Department = newDepartment,
                        HomeAddress = address
                    }, syncedAt, isActive: false);
                    dismissed.IsActive = false;
                    dismissed.DismissalDate ??= old?.DismissalDate ?? row.DismissalDate
                        ?? (row.TransferDate is { } transfer && transfer.Date <= DateTime.Today
                            ? transfer
                            : null);
                    dismissed.TransferDate = null;
                    if (string.IsNullOrWhiteSpace(dismissed.HrStatus)
                        || !EmployeeHrStatus.IsDismissed(dismissed.HrStatus))
                    {
                        dismissed.HrStatus = "Увольнение";
                    }

                    next.Add(dismissed);
                    continue;
                }

                // Непрерывная работа в том же юрлице — сохраняем Id.
                // Новый приём / перевод из другой компании после увольнения — чистая карточка.
                var employee = new OrganizationEmployee
                {
                    Id = old is { IsActive: true }
                        ? old.Id
                        : Guid.NewGuid().ToString("N")
                };
                OneCEmployeeMapper.ApplyRow(employee, row with
                {
                    Profession = newProfession,
                    Department = newDepartment,
                    HomeAddress = address
                }, syncedAt, isActive);
                next.Add(employee);

                if (isActive)
                {
                    run.Added++;
                    // «Приём» только если человека реально не было в штате (по СНИЛС/ФИО).
                    if (old is null || !old.IsActive)
                    {
                        staffingEvents.Add(new StaffingChangeEvent
                        {
                            Kind = StaffingChangeKind.Hired,
                            Organization = organization,
                            Person = row.Name.Trim(),
                            Snils = OneCEmployeeListParser.NormalizeSnils(row.Snils),
                            NewProfession = newProfession,
                            NewDepartment = newDepartment,
                            Message = old is null
                                ? "Новый сотрудник из выгрузки 1С (CSV)."
                                : "Новый приём / перевод в компанию (чистая карточка)."
                        });
                    }
                    else
                    {
                        var oldProfession = old.Profession?.Trim() ?? "";
                        var oldDepartment = old.Department?.Trim() ?? "";
                        if (!ProfessionSame(oldProfession, newProfession)
                            || !ProfessionSame(oldDepartment, newDepartment))
                        {
                            var transfer = new StaffingChangeEvent
                            {
                                Kind = StaffingChangeKind.Transferred,
                                Organization = organization,
                                Person = row.Name.Trim(),
                                Snils = OneCEmployeeListParser.NormalizeSnils(row.Snils),
                                OldProfession = oldProfession,
                                NewProfession = newProfession,
                                OldDepartment = oldDepartment,
                                NewDepartment = newDepartment,
                                Message = $"Перевод в «{organization}»: «{oldProfession}» → «{newProfession}»."
                            };
                            _staffingReset.ApplyTransferReset(transfer);
                            staffingEvents.Add(transfer);
                        }
                    }
                }
            }

            // Кого нет в текущей выгрузке: при обычной замене — архив; при hardRebuild — не тащим.
            if (!hardRebuild)
            {
                foreach (var (key, old) in previous)
                {
                    if (seenKeys.ContainsKey(key))
                    {
                        continue;
                    }

                    // Тот же человек есть в CSV под другим ключом трудоустройства — не увольнять.
                    var personId = PersonIdentityKey(organization, old.Snils, old.ExternalId, old.Name);
                    if (peopleStillInCsv.Contains(personId))
                    {
                        continue;
                    }

                    if (old.IsActive)
                    {
                        staffingEvents.Add(new StaffingChangeEvent
                        {
                            Kind = StaffingChangeKind.Fired,
                            Organization = organization,
                            Person = old.Name,
                            Snils = old.Snils,
                            OldProfession = old.Profession,
                            OldDepartment = old.Department,
                            Message = "Отсутствует в последней выгрузке 1С (уволен / неактуален)."
                        });
                    }

                    old.IsActive = false;
                    if (string.IsNullOrWhiteSpace(old.HrStatus) || !EmployeeHrStatus.IsDismissed(old.HrStatus))
                    {
                        old.HrStatus = "Увольнение";
                    }

                    old.SyncedAt = syncedAt;
                    next.Add(old);
                    run.MarkedInactive++;
                }
            }
            else
            {
                foreach (var (key, old) in previous)
                {
                    if (seenKeys.ContainsKey(key) || !old.IsActive)
                    {
                        continue;
                    }

                    var personId = PersonIdentityKey(organization, old.Snils, old.ExternalId, old.Name);
                    if (peopleStillInCsv.Contains(personId))
                    {
                        continue;
                    }

                    staffingEvents.Add(new StaffingChangeEvent
                    {
                        Kind = StaffingChangeKind.Fired,
                        Organization = organization,
                        Person = old.Name,
                        Snils = old.Snils,
                        OldProfession = old.Profession,
                        OldDepartment = old.Department,
                        Message = "Удалён при полной перезаписи: нет в CSV 1С."
                    });
                    run.MarkedInactive++;
                }
            }

            if (nextData.TryGetValue(organization, out var already))
            {
                nextData[organization] = MergeEmployeeListsPreferringActive(already, next);
            }
            else
            {
                nextData[organization] = next;
            }

        }

        OrganizationLegalEntitySplit.SplitKskEmployees(nextData, out nextData);

        var crossOrgDeactivated = OneCEmployeeCsvMerge.KeepOnlyCurrentEmploymentAcrossOrganizations(nextData);
        if (crossOrgDeactivated > 0)
        {
            run.MarkedInactive += crossOrgDeactivated;
            _logger.LogInformation(
                "Оставлен только актуальный приём по юрлицам: снято дублей {Count}",
                crossOrgDeactivated);
        }

        // Дубли одного места работы (СНИЛС + ЮЛ + должность + подразделение + вид занятости)
        // — оставляем запись с max(дата приёма / перевода).
        var withinOrgCollapsed = CollapseDuplicateEmploymentsSameProfession(nextData);
        if (withinOrgCollapsed > 0)
        {
            run.Deduplicated += withinOrgCollapsed;
            _logger.LogInformation(
                "Схлопнуты полные дубли мест работы внутри юрлиц: {Count}",
                withinOrgCollapsed);
        }

        var activeFromCsv = nextData.Values.Sum(list => list.Count(EmployeeHrStatus.IsEmployed));
        if (hardRebuild)
        {
            var backup = _employees.ReplaceEntire(nextData);
            _logger.LogInformation(
                "Штат полностью перезаписан из CSV. Активных={Active}, бэкап={Backup}",
                activeFromCsv,
                backup);
        }
        else
        {
            _employees.ReplaceAll(nextData);
        }

        _employees.PurgeDuplicatePersonShells();
        // ИП: штат под «О.С.» → полное «Оксана Сергеевна», иначе пустая карточка в UI.
        var ipKeysMoved = _employees.PreferFullIpNameKeys();
        if (ipKeysMoved > 0)
        {
            _logger.LogInformation(
                "Штат ИП перенесён с инициалов на полное ФИО: {Count}",
                ipKeysMoved);
        }

        // При hardRebuild не схлопываем и не гоняем DeactivateByCurrentCsv:
        // ключи BuildMatchKey (дата/должность) часто не совпадают с только что записанными
        // строками и снова гасят весь штат — сайт «пишет OK», а данные как будто не обновились.
        if (!hardRebuild)
        {
            _employees.CollapseDuplicatePersonRecords();
            var forcedInactive = DeactivateByCurrentCsv(byOrg, forceOverwriteEmptyOrgs: false);
            if (forcedInactive > 0)
            {
                run.MarkedInactive += forcedInactive;
                _logger.LogInformation(
                    "Принудительно сняты с штата по CSV (увольнение): {Count}",
                    forcedInactive);
            }
        }

        run.Updated = activeFromCsv;

        // После PreferFullIpNameKeys ключи в store могли смениться (О.С. → полное ФИО).
        var orgKeysForList = _employees.GetOrganizations()
            .Concat(nextData.Keys)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _orgs.ReplaceAllFromOneC(orgKeysForList);
        _orgs.EnsurePreservedLegalEntities();
        _orgs.ConsolidateOrganizationAliases();
        _orgs.PurgeLegacyManualShellOrganizations();
        _orgs.MergeEmployeeOrganizations();
        _orgs.ConsolidateOrganizationAliases();
        AlignMedOrganizationNames();
        PurgeDismissedFromActiveLists();

        try
        {
            _services.GetService<OrganizationEmployeesService>()?.InvalidateStaffCaches();
            Zashita.Web.Pages.Training.StatusModel.InvalidateReportCache();
            Zashita.Web.Pages.Med.StatusModel.InvalidateRowsCache();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сбросить кэши штата после синка 1С");
        }

        if (staffingEvents.Count > 0)
        {
            StaffingChangeEvent.EnrichCrossOrganizationTransfers(staffingEvents);
            run.AttachStaffChanges(staffingEvents);
            _staffingChanges.Append(staffingEvents);
        }
    }

    private void ApplyLegalEntitySplitsAfterSync()
    {
        _employees.ApplyLegalEntitySplits();
        _orgs.EnsurePreservedLegalEntities();
        _orgs.ConsolidateOrganizationAliases();
        _orgs.PurgeLegacyManualShellOrganizations();
        _orgs.MergeEmployeeOrganizations();
        _orgs.ConsolidateOrganizationAliases();
        AlignMedOrganizationNames();
    }

    private void PurgeDismissedFromActiveLists()
    {
        try
        {
            // Lazy resolve: иначе цикл DI OneCEmployeeSyncService ↔ DismissedEmployeePurgeService.
            var purge = _services.GetService<DismissedEmployeePurgeService>();
            if (purge is null)
            {
                return;
            }

            var result = purge.PurgeFromAllLists();
            if (result.AnyChanges)
            {
                _logger.LogInformation(
                    "Уволенные убраны из медосмотров/списков: exams={Exams}, personList={Lines}",
                    result.RemovedExams,
                    result.RemovedPersonListLines);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось вычистить уволенных из медосмотров/списков");
        }
    }

    private void AlignMedOrganizationNames()
    {
        try
        {
            _medAligner.AlignToCanonicalOrganizations(_orgs.AllOrganizations);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось выровнять организации в медицинских данных");
        }
    }

    /// <summary>
    /// Один СНИЛС + ЮЛ + должность + подразделение + вид занятости:
    /// оставляем запись с поздней датой приёма/перевода.
    /// </summary>
    private static int CollapseDuplicateEmploymentsSameProfession(
        Dictionary<string, List<OrganizationEmployee>> byOrg)
    {
        var removed = 0;
        foreach (var (org, list) in byOrg)
        {
            if (list.Count < 2)
            {
                continue;
            }

            var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in list.GroupBy(
                         e => OneCStaffEligibility.EmploymentDedupeKey(org, e),
                         StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
            {
                var ranked = group
                    .OrderByDescending(e => EmployeeHrStatus.IsEmployed(e) ? 1 : 0)
                    .ThenByDescending(e => OneCStaffEligibility.RankDate(e))
                    .ThenByDescending(e => OneCEmployeeListParser.IsPrimaryEmployment(e.EmploymentType) ? 1 : 0)
                    .ToList();
                keepIds.Add(ranked[0].Id);
            }

            if (keepIds.Count == 0)
            {
                continue;
            }

            for (var i = list.Count - 1; i >= 0; i--)
            {
                var employee = list[i];
                var dedupeKey = OneCStaffEligibility.EmploymentDedupeKey(org, employee);
                var dupGroup = list.Count(e =>
                    string.Equals(
                        OneCStaffEligibility.EmploymentDedupeKey(org, e),
                        dedupeKey,
                        StringComparison.OrdinalIgnoreCase));
                if (dupGroup <= 1 || keepIds.Contains(employee.Id))
                {
                    continue;
                }

                list.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// После синка: активными остаются только люди, подтверждённые актуальной строкой CSV
    /// в том же юрлице. Сопоставление ЮЛ — по GetCoreName (ССТРОЙ ↔ СтандартСтрой),
    /// человека — по PersonStableKey (не по хрупкому BuildMatchKey с должностью/датой).
    /// КСК ≠ Строй Каркас (разные id); «КСК ООО» → ООО «КСК», «ООО "КСК"» → Строй Каркас.
    /// </summary>
    private int DeactivateByCurrentCsv(
        Dictionary<string, List<(OneCEmployeeRow Row, bool IsActive)>> byOrg,
        bool forceOverwriteEmptyOrgs = false)
    {
        // Ядро имени ЮЛ → актуальные люди (s:СНИЛС / n:ФИО_дата).
        var workingPeopleByOrgCore = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (organization, rows) in byOrg)
        {
            foreach (var (row, isActive) in rows)
            {
                if (!isActive || string.IsNullOrWhiteSpace(row.Name))
                {
                    continue;
                }

                var org = ResolveStaffOrganizationForKey(organization, row);
                var core = StaffOrgCoreKey(org);
                if (!workingPeopleByOrgCore.TryGetValue(core, out var people))
                {
                    people = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    workingPeopleByOrgCore[core] = people;
                }

                people.Add(OneCStaffEligibility.PersonStableKey(row));
            }
        }

        var snapshot = _employees.SnapshotIncludingInactive();
        var changed = 0;
        var next = new Dictionary<string, List<OrganizationEmployee>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (org, list) in snapshot)
        {
            var copy = list.Select(CloneEmployee).ToList();
            var core = StaffOrgCoreKey(org);
            workingPeopleByOrgCore.TryGetValue(core, out var workingPeople);
            var csvActiveForOrg = workingPeople?.Count ?? 0;
            var currentlyEmployed = copy.Count(EmployeeHrStatus.IsEmployed);
            // Защита от «обнуления» при частичной выгрузке. При hardRebuild — доверяем CSV.
            if (!forceOverwriteEmptyOrgs
                && currentlyEmployed > 0
                && csvActiveForOrg == 0)
            {
                next[org] = copy;
                continue;
            }

            foreach (var employee in copy)
            {
                var personKey = OneCStaffEligibility.PersonStableKey(employee);
                var mustWork = workingPeople is not null
                               && !string.IsNullOrWhiteSpace(personKey)
                               && workingPeople.Contains(personKey);

                if (mustWork)
                {
                    if (!EmployeeHrStatus.IsEmployed(employee))
                    {
                        employee.IsActive = true;
                        if (!EmployeeHrStatus.IsOnStaffStatus(employee.HrStatus))
                        {
                            employee.HrStatus = "Работа";
                        }

                        employee.DismissalDate = null;
                        employee.TransferDate = null;
                        changed++;
                    }

                    continue;
                }

                // Нет актуальной строки CSV для этого человека в этом ЮЛ — убрать из штата.
                if (!employee.IsActive
                    && EmployeeHrStatus.IsDismissed(employee.HrStatus)
                    && employee.DismissalDate is not null)
                {
                    continue;
                }

                if (employee.IsActive
                    || !EmployeeHrStatus.IsDismissed(employee.HrStatus)
                    || EmployeeHrStatus.IsOnStaffStatus(employee.HrStatus))
                {
                    employee.IsActive = false;
                    employee.HrStatus = "Увольнение";
                    employee.DismissalDate ??= DateTime.Today;
                    employee.TransferDate = null;
                    changed++;
                }
            }

            next[org] = copy;
        }

        if (changed > 0)
        {
            _employees.ReplaceAll(next);
        }

        return changed;
    }

    /// <summary>ССТРОЙ / СтандартСтрой → один id; КСК и Строй Каркас — разные id.</summary>
    private static string StaffOrgCoreKey(string organization)
    {
        var id = OrganizationCanonicalRegistry.TryGetLegalEntityId(organization);
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        var core = OrganizationMatcher.GetCoreName(organization);
        if (!string.IsNullOrWhiteSpace(core))
        {
            return core;
        }

        return OrganizationMatcher.Normalize(organization);
    }

    private static string ContractKey(string organization, OneCEmployeeRow row) =>
        OneCEmployeeListParser.BuildMatchKey(organization, row);

    private static string ContractKey(string organization, OrganizationEmployee employee) =>
        OneCEmployeeListParser.BuildMatchKey(organization, employee);

    /// <summary>«КСК ООО» → ООО «КСК»; «ООО "КСК"» → Строй Каркас.</summary>
    private static string ResolveStaffOrganizationForKey(string organization, OneCEmployeeRow row) =>
        OrganizationLegalEntitySplit.ResolveCsvOrganization(organization, row.Name, row.Snils);

    /// <summary>Ключ штата ИП = полное ФИО из CSV, не инициалы xlsx («О.С.»).</summary>
    private static string PreferIpFullNameStaffKey(string organizationFromFile, string synced)
    {
        var fromFile = (organizationFromFile ?? string.Empty).Trim();
        var key = string.IsNullOrWhiteSpace(synced) ? fromFile : synced.Trim();
        if (fromFile.StartsWith("ИП", StringComparison.OrdinalIgnoreCase)
            && !OrganizationMatcher.IsIpInitialsForm(fromFile)
            && OrganizationMatcher.IsIpInitialsForm(key))
        {
            return fromFile;
        }

        return key;
    }

    private static OrganizationEmployee CloneEmployee(OrganizationEmployee source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            LastName = source.LastName,
            FirstName = source.FirstName,
            Patronymic = source.Patronymic,
            Profession = source.Profession,
            Department = source.Department,
            Snils = source.Snils,
            ExternalId = source.ExternalId,
            BirthDate = source.BirthDate,
            EmploymentDate = source.EmploymentDate,
            TransferDate = source.TransferDate,
            DismissalDate = source.DismissalDate,
            EmploymentType = source.EmploymentType,
            HrStatus = source.HrStatus,
            HomeAddress = source.HomeAddress,
            IsActive = source.IsActive,
            SyncedAt = source.SyncedAt
        };

    /// <summary>«Бурильщик» и «Бурильщик *» — одна должность; иначе каждый sync плодит «перевод».</summary>
    private static bool ProfessionSame(string? left, string? right)
    {
        var a = ProfessionMatcher.Normalize(left);
        var b = ProfessionMatcher.Normalize(right);
        if (a.Length == 0 && b.Length == 0)
        {
            return true;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
               || ProfessionMatcher.Matches(a, b);
    }

    private static string PersonIdentityKey(
        string organization,
        string? snils,
        string? externalId,
        string? name) =>
        OneCEmployeeListParser.BuildPersonKey(
            organization,
            snils ?? string.Empty,
            externalId ?? string.Empty,
            name ?? string.Empty);

    /// <summary>
    /// Найти того же человека в прежнем штате, даже если дата приёма / вид занятости в ключе другие.
    /// Предпочитаем активную карточку.
    /// </summary>
    private static OrganizationEmployee? FindPreviousSamePerson(
        IEnumerable<OrganizationEmployee> previousPeople,
        string organization,
        OneCEmployeeRow row)
    {
        var want = PersonIdentityKey(organization, row.Snils, row.ExternalId, row.Name);
        if (string.IsNullOrWhiteSpace(want))
        {
            return null;
        }

        OrganizationEmployee? inactive = null;
        foreach (var emp in previousPeople)
        {
            var got = PersonIdentityKey(organization, emp.Snils, emp.ExternalId, emp.Name);
            if (!string.Equals(got, want, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (emp.IsActive)
            {
                return emp;
            }

            inactive ??= emp;
        }

        return inactive;
    }

    private static List<OrganizationEmployee> MergeEmployeeListsPreferringActive(
        List<OrganizationEmployee> first,
        List<OrganizationEmployee> second)
    {
        var byKey = new Dictionary<string, OrganizationEmployee>(StringComparer.OrdinalIgnoreCase);
        void Add(OrganizationEmployee e)
        {
            var key = OneCEmployeeListParser.BuildMatchKey("", e);
            if (!byKey.TryGetValue(key, out var old))
            {
                byKey[key] = e;
                return;
            }

            if (OneCEmployeeCsvMerge.PreferIncomingEmployee(e, old))
            {
                byKey[key] = e;
            }
        }

        foreach (var e in first)
        {
            Add(e);
        }

        foreach (var e in second)
        {
            Add(e);
        }

        return byKey.Values.ToList();
    }
}
using System.Text.RegularExpressions;
using Zashita.Core.Employees;

namespace Zashita.Core.Risks;

public static class SoutPreparationDataBuilder
{
    /// <summary>
    /// Шапка «Информация для СОУТ»: адрес/контакты из реквизитов организации, ФИО — руководитель.
    /// Телефон/e-mail — из EmployerContacts; если пусто — из поля Address (часто туда пишут всё подряд).
    /// </summary>
    public static SoutPreparationOrgHeader ResolveOrgHeader(
        string organization,
        OrganizationRequisites? requisites,
        IReadOnlyList<OrganizationEmployee>? employees)
    {
        var header = new SoutPreparationOrgHeader();
        var req = requisites ?? new OrganizationRequisites();
        var rawAddress = (req.Address ?? string.Empty).Trim();
        header.Address = CleanMeasurementAddress(rawAddress);

        ParseEmployerContacts(req.EmployerContacts, out var email, out var phone, out var contactFromField);
        // В карточке контакты часто лежат в Address вместе с ИНН/банком.
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(contactFromField))
        {
            ParseEmployerContacts(rawAddress, out var email2, out var phone2, out var other2);
            if (string.IsNullOrWhiteSpace(email))
            {
                email = email2;
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                phone = phone2;
            }

            if (string.IsNullOrWhiteSpace(contactFromField))
            {
                contactFromField = ExtractDirectorFromRequisitesText(rawAddress) ?? other2;
            }
        }

        header.Email = email;
        header.Phone = phone;

        var contact = RiskPrintChrome.GuessDirectorName(employees ?? []);
        if (string.IsNullOrWhiteSpace(contact))
        {
            contact = ExtractDirectorFromRequisitesText(rawAddress)
                      ?? contactFromField;
        }

        if (string.IsNullOrWhiteSpace(contact))
        {
            var ipName = OrganizationMatcher.InferIndividualEntrepreneurEmployee(organization);
            if (!string.IsNullOrWhiteSpace(ipName))
            {
                contact = RiskPrintChrome.FormatInitials(ipName);
            }
        }

        header.Contact = (contact ?? string.Empty).Trim();
        return header;
    }

    /// <summary>Адрес замеров — только географическая часть, без ИНН/банка/директора.</summary>
    private static string CleanMeasurementAddress(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // Обрезаем хвост с реквизитами, если они слиты в одно поле Address.
        var cut = Regex.Match(
            text,
            @"^(.*?)(?=(?:\s*ИНН\b|\s*КПП\b|\s*ОГРН\b|\s*ОКПО\b|\s*Банк\b|\s*р/?с\b|\s*к/?с\b|\s*БИК\b|\s*Телефон|\s*E-?mail|\s*Директор\b))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (cut.Success && cut.Groups[1].Value.Trim().Length >= 8)
        {
            text = cut.Groups[1].Value.Trim();
        }

        text = text
            .Replace("для доставки счета", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', ',', ';', ':', '.', '-', '/');
        return text;
    }

    private static string? ExtractDirectorFromRequisitesText(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var m = Regex.Match(
            text,
            @"Директор\s+([А-ЯЁA-Z][А-ЯЁа-яёA-Za-z.\-]+(?:\s+[А-ЯЁA-Z]\.?\s*[А-ЯЁA-Z]\.?|[А-ЯЁа-яёA-Za-z.\-]+){0,3})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return null;
        }

        var name = m.Groups[1].Value.Trim().Trim(',', ';', '.', ' ');
        return string.IsNullOrWhiteSpace(name) ? null : RiskPrintChrome.FormatInitials(name);
    }

    private static void ParseEmployerContacts(
        string? raw,
        out string email,
        out string phone,
        out string other)
    {
        email = string.Empty;
        phone = string.Empty;
        other = string.Empty;
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        var emailMatch = Regex.Match(text, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
        if (emailMatch.Success)
        {
            email = emailMatch.Value;
            text = text.Remove(emailMatch.Index, emailMatch.Length);
        }

        // Локальные «385-68-10» и полные «+7 (343) 385-68-00».
        var phoneMatch = Regex.Match(
            text,
            @"(\+?\d[\d\s\-()]{4,}\d|\b\d{2,3}[\s\-]\d{2}[\s\-]\d{2}\b)");
        if (phoneMatch.Success)
        {
            phone = Regex.Replace(phoneMatch.Value, @"\s{2,}", " ").Trim();
            text = text.Remove(phoneMatch.Index, phoneMatch.Length);
        }

        other = text
            .Replace("E-mail", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Email", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("тел.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("телефон", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("факс", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', ',', ';', ':', '.', '-', '/');
    }

    public static List<WorkplaceRecord> GetCandidateWorkplaces(
        SoutPreparationDocument document,
        IEnumerable<WorkplaceRecord> workplaces,
        bool onlyWithoutSout = false) =>
        FilterWorkplaces(document, workplaces, onlyWithoutSout);

    public static List<SoutPreparationPrintRow> BuildRows(
        SoutPreparationDocument document,
        IEnumerable<WorkplaceRecord> workplaces,
        IEnumerable<WorkplaceAssignmentDocument> assignments,
        IEnumerable<SoutCatalogRecord>? equipment = null,
        IEnumerable<SoutCatalogRecord>? materials = null,
        IEnumerable<OrganizationEmployee>? employees = null,
        bool onlyWithoutSout = false,
        IReadOnlyCollection<string>? selectedWorkplaceIds = null)
    {
        var org = document.Organization?.Trim() ?? string.Empty;
        var equipmentItems = (equipment ?? [])
            .Where(item => !item.IsGroup)
            .Where(item => SameOrg(item.Organization, org))
            .ToList();
        var materialItems = (materials ?? [])
            .Where(item => !item.IsGroup)
            .Where(item => SameOrg(item.Organization, org))
            .ToList();

        var assignmentLines = assignments
            .Where(doc => SameOrg(doc.Organization, org))
            .SelectMany(doc => doc.Lines.Select(line => (doc.Department, line)))
            .ToList();

        var workplaceItems = FilterWorkplaces(document, workplaces, onlyWithoutSout, selectedWorkplaceIds);

        var employeeList = (employees ?? []).ToList();
        var rows = new List<SoutPreparationPrintRow>();
        var index = 1;
        foreach (var workplace in workplaceItems)
        {
            var matchedAssignments = assignmentLines
                .Where(pair =>
                    string.Equals(pair.line.WorkplaceId, workplace.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.line.WorkplaceName, workplace.Name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(workplace.WorkplaceNumber) &&
                     string.Equals(pair.line.WorkplaceNumber, workplace.WorkplaceNumber, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => pair.line)
                .ToList();

            var employeeName = matchedAssignments
                .Select(line => line.Employee)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                ?? string.Empty;

            var employee = FindEmployee(employeeList, employeeName);
            var total = workplace.WorkerCount > 0 ? workplace.WorkerCount : Math.Max(matchedAssignments.Count, 1);
            var women = CountWomen(matchedAssignments, employeeList, employeeName);
            var equipmentText = ResolveEquipment(workplace, equipmentItems);
            var materialsText = ResolveMaterials(workplace, materialItems);
            var equipmentCombined = string.Join("; ",
                new[] { equipmentText, materialsText }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var rowIndex = index++;
            rows.Add(new SoutPreparationPrintRow
            {
                Index = rowIndex,
                WorkplaceNumber = string.IsNullOrWhiteSpace(workplace.WorkplaceNumber)
                    ? rowIndex.ToString()
                    : workplace.WorkplaceNumber,
                Profession = ResolveProfession(workplace),
                EmployeeName = employeeName,
                Snils = OneCEmployeeListParser.FormatSnils(employee?.Snils ?? OverrideOr(workplace, "snils", string.Empty)),
                Department = string.IsNullOrWhiteSpace(workplace.Department)
                    ? document.Department
                    : workplace.Department,
                OkCode = string.IsNullOrWhiteSpace(workplace.OkCode)
                    ? OverrideOr(workplace, "okCode", string.Empty)
                    : workplace.OkCode.Trim(),
                WorkWeekHours = OverrideOr(workplace, "workWeekHours", "40"),
                ShiftHours = OverrideOr(workplace, "shiftHours", "8"),
                TotalWorkers = total.ToString(),
                WomenCount = women > 0 ? women.ToString() : "—",
                MinorsCount = OverrideOr(workplace, "minorsCount", "—"),
                DisabledCount = OverrideOr(workplace, "disabledCount", "—"),
                Equipment = equipmentCombined,
                Materials = materialsText,
                EquipmentHours = OverrideOr(workplace, "equipmentHours", "6"),
                HarmfulFactors = OverrideOr(workplace, "harmfulFactors", string.Empty),
                ExtraPayPercent = OverrideOr(workplace, "extraPayPercent", "—"),
                Milk = OverrideOr(workplace, "milk", "—"),
                ExtraLeave = OverrideOr(workplace, "extraLeave", "—"),
                ShortDay = OverrideOr(workplace, "shortDay", "—"),
                PensionBenefits = OverrideOr(workplace, "pensionBenefits", "—"),
                MedicalExam = OverrideOr(workplace, "medicalExam", "да"),
                PreviousConditionsClass = string.IsNullOrWhiteSpace(workplace.ConditionsClass)
                    ? "—"
                    : workplace.ConditionsClass,
                AccidentOrDisease = OverrideOr(workplace, "accidentOrDisease", "нет"),
                WorkDescription = OverrideOr(workplace, "workDescription", equipmentCombined)
            });
        }

        return rows;
    }

    public static List<SoutPreparationPrintRow> BuildInfoNeededRows(
        SoutPreparationDocument document,
        IEnumerable<WorkplaceRecord> workplaces,
        IEnumerable<WorkplaceAssignmentDocument> assignments,
        IEnumerable<SoutCatalogRecord>? equipment,
        IEnumerable<SoutCatalogRecord>? materials,
        IEnumerable<OrganizationEmployee>? employees,
        IReadOnlyCollection<string>? selectedWorkplaceIds = null)
    {
        var org = document.Organization?.Trim() ?? string.Empty;
        var equipmentItems = (equipment ?? [])
            .Where(item => !item.IsGroup)
            .Where(item => SameOrg(item.Organization, org))
            .ToList();
        var materialItems = (materials ?? [])
            .Where(item => !item.IsGroup)
            .Where(item => SameOrg(item.Organization, org))
            .ToList();
        var assignmentLines = assignments
            .Where(doc => SameOrg(doc.Organization, org))
            .SelectMany(doc => doc.Lines.Select(line => (doc.Department, line)))
            .ToList();
        var workplaceItems = FilterWorkplaces(document, workplaces, onlyWithoutSout: false, selectedWorkplaceIds);
        var employeeList = (employees ?? [])
            .Where(e => e.IsActive && !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
        var rows = new List<SoutPreparationPrintRow>();
        var index = 1;
        var claimedEmployeeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var workplace in workplaceItems)
        {
            var card = workplace.SoutCard ?? new SoutCardDetails();
            var resolved = ResolveEmployeesForWorkplace(
                workplace,
                card,
                assignmentLines,
                employeeList,
                claimedEmployeeKeys);

            if (resolved.Count == 0)
            {
                rows.Add(BuildInfoNeededRow(
                    workplace,
                    card,
                    null,
                    equipmentItems,
                    materialItems,
                    index++));
                continue;
            }

            foreach (var employee in resolved)
            {
                ClaimEmployee(claimedEmployeeKeys, employee);
                rows.Add(BuildInfoNeededRow(
                    workplace,
                    card,
                    employee,
                    equipmentItems,
                    materialItems,
                    index++));
            }
        }

        return rows;
    }

    private static SoutPreparationPrintRow BuildInfoNeededRow(
        WorkplaceRecord workplace,
        SoutCardDetails card,
        OrganizationEmployee? employee,
        IReadOnlyList<SoutCatalogRecord> equipmentItems,
        IReadOnlyList<SoutCatalogRecord> materialItems,
        int index)
    {
        var equipmentText = !string.IsNullOrWhiteSpace(card.Equipment)
            ? card.Equipment.Trim()
            : ResolveEquipment(workplace, equipmentItems);
        var materialsText = !string.IsNullOrWhiteSpace(card.Materials)
            ? card.Materials.Trim()
            : ResolveMaterials(workplace, materialItems);
        var equipmentCombined = string.Join("; ",
            new[] { equipmentText, materialsText }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var workDescription = card.ExtraLines.TryGetValue("022", out var line022) && !string.IsNullOrWhiteSpace(line022)
            ? line022.Trim()
            : equipmentCombined;
        var okCode = !string.IsNullOrWhiteSpace(workplace.OkCode)
            ? workplace.OkCode.Trim()
            : card.ExtraLines.TryGetValue("011", out var line011) && !string.IsNullOrWhiteSpace(line011)
                ? line011.Trim()
                : OverrideOr(workplace, "okCode", string.Empty);
        var total = card.AnalogousWorkerCount > 0
            ? card.AnalogousWorkerCount
            : workplace.WorkerCount > 0
                ? workplace.WorkerCount
                : 1;
        var women = card.WorkersWomen;
        var minors = card.WorkersUnder18;
        var disabled = card.WorkersDisabled;
        // Если в карте РМ не заполнено «женщин», но строка по сотруднице — ставим 1.
        if (women <= 0 && string.Equals(InferSex(employee?.Name), "ж", StringComparison.OrdinalIgnoreCase))
        {
            women = 1;
        }

        return new SoutPreparationPrintRow
        {
            Index = index,
            WorkplaceNumber = string.IsNullOrWhiteSpace(workplace.WorkplaceNumber)
                ? string.Empty
                : workplace.WorkplaceNumber.Trim(),
            Profession = ResolveProfession(workplace),
            EmployeeName = employee?.Name?.Trim() ?? string.Empty,
            Snils = OneCEmployeeListParser.FormatSnils(employee?.Snils ?? OverrideOr(workplace, "snils", string.Empty)),
            Department = string.IsNullOrWhiteSpace(workplace.Department) ? string.Empty : workplace.Department.Trim(),
            OkCode = okCode,
            Sex = InferSex(employee?.Name),
            ScheduleFrom = OverrideOr(workplace, "scheduleFrom", "08:00"),
            ScheduleTo = OverrideOr(workplace, "scheduleTo", "17:00"),
            LunchBreak = OverrideOr(workplace, "lunchBreak", "1"),
            WorkWeekHours = OverrideOr(workplace, "workWeekHours", "40"),
            ShiftHours = OverrideOr(workplace, "shiftHours", "8"),
            TotalWorkers = total.ToString(),
            WomenCount = women > 0 ? women.ToString() : string.Empty,
            MinorsCount = minors > 0 ? minors.ToString() : string.Empty,
            DisabledCount = disabled > 0 ? disabled.ToString() : string.Empty,
            AnalogousWorkplaces = string.IsNullOrWhiteSpace(card.AnalogousWorkplaces)
                ? "1"
                : card.AnalogousWorkplaces.Trim(),
            Equipment = equipmentCombined,
            Materials = materialsText,
            EquipmentHours = OverrideOr(workplace, "equipmentHours", "6"),
            HarmfulFactors = OverrideOr(workplace, "harmfulFactors", string.Empty),
            ExtraPayPercent = OverrideOr(workplace, "extraPayPercent", "—"),
            Milk = OverrideOr(workplace, "milk", "—"),
            ExtraLeave = OverrideOr(workplace, "extraLeave", "—"),
            ShortDay = OverrideOr(workplace, "shortDay", "—"),
            PensionBenefits = OverrideOr(workplace, "pensionBenefits", "—"),
            MedicalExam = OverrideOr(workplace, "medicalExam", "да"),
            PreviousConditionsClass = string.IsNullOrWhiteSpace(workplace.ConditionsClass)
                ? "—"
                : workplace.ConditionsClass.Trim(),
            AccidentOrDisease = OverrideOr(workplace, "accidentOrDisease", "нет"),
            WorkDescription = string.IsNullOrWhiteSpace(workDescription) ? equipmentCombined : workDescription
        };
    }

    /// <summary>
    /// Сотрудники на РМ для «Информация для СОУТ»:
    /// 1) EmployeeIds / стр. 021 карты, 2) закрепления, 3) штат по подразделению+профессии.
    /// </summary>
    private static List<OrganizationEmployee> ResolveEmployeesForWorkplace(
        WorkplaceRecord workplace,
        SoutCardDetails card,
        IReadOnlyList<(string Department, WorkplaceAssignmentLine line)> assignmentLines,
        IReadOnlyList<OrganizationEmployee> employees,
        ISet<string> claimedEmployeeKeys)
    {
        var result = new List<OrganizationEmployee>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(OrganizationEmployee? employee)
        {
            if (employee is null || string.IsNullOrWhiteSpace(employee.Name))
            {
                return;
            }

            var key = EmployeeClaimKey(employee);
            if (seen.Contains(key) || claimedEmployeeKeys.Contains(key))
            {
                return;
            }

            seen.Add(key);
            result.Add(employee);
        }

        foreach (var employee in ResolveCardEmployees(card, employees))
        {
            TryAdd(employee);
        }

        foreach (var employee in ResolveEmployeesFromLine021(card, employees))
        {
            TryAdd(employee);
        }

        foreach (var employee in ResolveAssignmentEmployees(workplace, assignmentLines, employees))
        {
            TryAdd(employee);
        }

        if (result.Count == 0)
        {
            foreach (var employee in ResolveEmployeesByProfession(workplace, employees, claimedEmployeeKeys))
            {
                TryAdd(employee);
            }
        }

        return result;
    }

    private static List<OrganizationEmployee> ResolveCardEmployees(
        SoutCardDetails card,
        IReadOnlyList<OrganizationEmployee> employees)
    {
        var ids = (card.EmployeeIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0 || employees.Count == 0)
        {
            return [];
        }

        var resolved = new List<OrganizationEmployee>();
        foreach (var id in ids)
        {
            var employee = FindEmployeeByAnyId(employees, id);
            if (employee is not null)
            {
                resolved.Add(employee);
            }
        }

        return resolved;
    }

    private static List<OrganizationEmployee> ResolveEmployeesFromLine021(
        SoutCardDetails card,
        IReadOnlyList<OrganizationEmployee> employees)
    {
        if (employees.Count == 0
            || card.ExtraLines is null
            || !card.ExtraLines.TryGetValue("021", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var tokens = raw
            .Split([';', ',', '|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .ToList();
        if (tokens.Count == 0)
        {
            return [];
        }

        var resolved = new List<OrganizationEmployee>();
        foreach (var token in tokens)
        {
            var byId = FindEmployeeByAnyId(employees, token);
            if (byId is not null)
            {
                resolved.Add(byId);
                continue;
            }

            var byName = FindEmployee(employees, token);
            if (byName is not null)
            {
                resolved.Add(byName);
            }
        }

        return resolved;
    }

    private static List<OrganizationEmployee> ResolveAssignmentEmployees(
        WorkplaceRecord workplace,
        IReadOnlyList<(string Department, WorkplaceAssignmentLine line)> assignmentLines,
        IReadOnlyList<OrganizationEmployee> employees)
    {
        var names = assignmentLines
            .Where(pair =>
                string.Equals(pair.line.WorkplaceId, workplace.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.line.WorkplaceName, workplace.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(workplace.WorkplaceNumber) &&
                 string.Equals(pair.line.WorkplaceNumber, workplace.WorkplaceNumber, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.line.Employee)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = new List<OrganizationEmployee>();
        foreach (var name in names)
        {
            var employee = FindEmployee(employees, name);
            if (employee is not null)
            {
                resolved.Add(employee);
            }
            else
            {
                // ФИО из закрепления есть, даже если в штате не нашли — создаём «виртуальную» запись для печати.
                resolved.Add(new OrganizationEmployee { Name = name.Trim() });
            }
        }

        return resolved;
    }

    private static List<OrganizationEmployee> ResolveEmployeesByProfession(
        WorkplaceRecord workplace,
        IReadOnlyList<OrganizationEmployee> employees,
        ISet<string> claimedEmployeeKeys)
    {
        var wantProf = ProfessionMatcher.Normalize(
            string.IsNullOrWhiteSpace(workplace.Profession) ? workplace.Name : workplace.Profession);
        if (string.IsNullOrWhiteSpace(wantProf) || employees.Count == 0)
        {
            return [];
        }

        var wantDept = (workplace.Department ?? string.Empty).Trim();
        var matched = employees
            .Where(e =>
                string.Equals(
                    ProfessionMatcher.Normalize(e.Profession),
                    wantProf,
                    StringComparison.OrdinalIgnoreCase))
            .Where(e =>
                string.IsNullOrWhiteSpace(wantDept)
                || string.IsNullOrWhiteSpace(e.Department)
                || string.Equals(e.Department.Trim(), wantDept, StringComparison.OrdinalIgnoreCase))
            .Where(e => !claimedEmployeeKeys.Contains(EmployeeClaimKey(e)))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (matched.Count == 0)
        {
            return [];
        }

        var limit = workplace.SoutCard?.AnalogousWorkerCount > 0
            ? workplace.SoutCard.AnalogousWorkerCount
            : workplace.WorkerCount > 0
                ? workplace.WorkerCount
                : matched.Count;

        if (limit > 0 && matched.Count > limit)
        {
            return matched.Take(limit).ToList();
        }

        return matched;
    }

    private static OrganizationEmployee? FindEmployeeByAnyId(
        IReadOnlyList<OrganizationEmployee> employees,
        string id)
    {
        if (string.IsNullOrWhiteSpace(id) || employees.Count == 0)
        {
            return null;
        }

        var needle = id.Trim();
        var needleSnils = OneCEmployeeListParser.NormalizeSnils(needle);

        return employees.FirstOrDefault(e =>
                   string.Equals(e.Id, needle, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(e.ExternalId, needle, StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(needleSnils)
                       && string.Equals(
                           OneCEmployeeListParser.NormalizeSnils(e.Snils),
                           needleSnils,
                           StringComparison.OrdinalIgnoreCase))
                   || string.Equals(e.Name?.Trim(), needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string EmployeeClaimKey(OrganizationEmployee employee)
    {
        var snils = OneCEmployeeListParser.NormalizeSnils(employee.Snils);
        if (snils.Length >= 11)
        {
            return "snils:" + snils;
        }

        if (!string.IsNullOrWhiteSpace(employee.Id))
        {
            return "id:" + employee.Id.Trim();
        }

        return "name:" + (employee.Name ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static void ClaimEmployee(ISet<string> claimed, OrganizationEmployee employee) =>
        claimed.Add(EmployeeClaimKey(employee));

    private static OrganizationEmployee? ResolveAssignmentEmployee(
        WorkplaceRecord workplace,
        IReadOnlyList<(string Department, WorkplaceAssignmentLine line)> assignmentLines,
        IReadOnlyList<OrganizationEmployee> employees)
    {
        return ResolveAssignmentEmployees(workplace, assignmentLines, employees).FirstOrDefault();
    }

    private static string InferSex(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return string.Empty;
        }

        return LooksFemale(fullName) ? "ж" : "м";
    }

    public static List<SoutPreparationEmployeeLookup> BuildEmployeeLookup(
        IEnumerable<OrganizationEmployee> employees,
        IEnumerable<WorkplaceRecord> workplaces,
        IEnumerable<WorkplaceAssignmentDocument> assignments,
        string organization)
    {
        var org = organization?.Trim() ?? string.Empty;
        var workplaceList = workplaces
            .Where(w => !w.IsGroup && w.IsUsed)
            .Where(w => SameOrg(w.Organization, org))
            .ToList();
        var assignmentLines = assignments
            .Where(doc => SameOrg(doc.Organization, org))
            .SelectMany(doc => doc.Lines)
            .ToList();

        var result = new List<SoutPreparationEmployeeLookup>();
        foreach (var employee in employees
                     .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                     .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var name = employee.Name.Trim();
            var matchedWp = assignmentLines
                .Where(line => string.Equals(line.Employee, name, StringComparison.OrdinalIgnoreCase))
                .Select(line => workplaceList.FirstOrDefault(w =>
                    string.Equals(w.Id, line.WorkplaceId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(w.Name, line.WorkplaceName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(line.WorkplaceNumber) &&
                     string.Equals(w.WorkplaceNumber, line.WorkplaceNumber, StringComparison.OrdinalIgnoreCase))))
                .FirstOrDefault(w => w is not null);

            matchedWp ??= workplaceList.FirstOrDefault(w =>
                string.Equals(w.Profession, employee.Profession, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w.Name, employee.Profession, StringComparison.OrdinalIgnoreCase));

            result.Add(new SoutPreparationEmployeeLookup
            {
                Name = name,
                Profession = string.IsNullOrWhiteSpace(employee.Profession)
                    ? matchedWp?.Profession ?? matchedWp?.Name ?? string.Empty
                    : employee.Profession.Trim(),
                Department = string.IsNullOrWhiteSpace(employee.Department)
                    ? matchedWp?.Department ?? string.Empty
                    : employee.Department.Trim(),
                Snils = OneCEmployeeListParser.FormatSnils(employee.Snils),
                OkCode = matchedWp?.OkCode?.Trim() ?? string.Empty
            });
        }

        return result;
    }

    private static List<WorkplaceRecord> FilterWorkplaces(
        SoutPreparationDocument document,
        IEnumerable<WorkplaceRecord> workplaces,
        bool onlyWithoutSout,
        IReadOnlyCollection<string>? selectedWorkplaceIds = null)
    {
        var org = document.Organization?.Trim() ?? string.Empty;
        var forOrg = workplaces
            .Where(item => !item.IsGroup && item.IsUsed)
            .Where(item => SameOrg(item.Organization, org))
            .Where(item => !onlyWithoutSout || NeedsSout(item))
            .OrderBy(item => item.WorkplaceNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var forDepartment = string.IsNullOrWhiteSpace(document.Department)
            ? forOrg
            : forOrg
                .Where(item => string.Equals(item.Department, document.Department, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var workplaceItems = forDepartment.Count > 0 ? forDepartment : forOrg;

        if (selectedWorkplaceIds is { Count: > 0 })
        {
            var selected = selectedWorkplaceIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            workplaceItems = workplaceItems
                .Where(item => selected.Contains(item.Id))
                .ToList();
        }

        return workplaceItems;
    }

    private static bool NeedsSout(WorkplaceRecord workplace) =>
        workplace.RequiresUnscheduledSout ||
        workplace.SoutAssessmentDate is null ||
        (workplace.SoutAssessmentDate is { } d && d.Date.AddYears(5) < DateTime.Today);

    private static OrganizationEmployee? FindEmployee(IReadOnlyList<OrganizationEmployee> employees, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || employees.Count == 0)
        {
            return null;
        }

        var needle = name.Trim();
        return employees.FirstOrDefault(e =>
                   string.Equals(e.Name, needle, StringComparison.OrdinalIgnoreCase))
               ?? employees.FirstOrDefault(e =>
                   !string.IsNullOrWhiteSpace(e.Name) &&
                   (e.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    needle.Contains(e.Name, StringComparison.OrdinalIgnoreCase)));
    }

    private static int CountWomen(
        IReadOnlyList<WorkplaceAssignmentLine> assignments,
        IReadOnlyList<OrganizationEmployee> employees,
        string primaryName)
    {
        var names = assignments
            .Select(a => a.Employee)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0 && !string.IsNullOrWhiteSpace(primaryName))
        {
            names.Add(primaryName);
        }

        return names.Count(name => LooksFemale(name) || LooksFemale(FindEmployee(employees, name)?.Name));
    }

    private static bool LooksFemale(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return false;
        }

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var last = parts[0];
        return last.EndsWith("а", StringComparison.OrdinalIgnoreCase)
               || last.EndsWith("ая", StringComparison.OrdinalIgnoreCase)
               || last.EndsWith("я", StringComparison.OrdinalIgnoreCase);
    }

    public static SoutPreparationCommissionInfo ResolveCommission(
        SoutPreparationDocument document,
        IEnumerable<SoutCommissionRecord>? commissions,
        IEnumerable<OrganizationEmployee>? employees = null)
    {
        var info = new SoutPreparationCommissionInfo();
        var org = document.Organization?.Trim() ?? string.Empty;
        var list = (commissions ?? [])
            .Where(item => !item.IsGroup)
            .Where(item => SameOrg(item.Organization, org))
            .ToList();

        static bool IsSout(SoutCommissionRecord item) =>
            item.Name.Contains("специальной оценки", StringComparison.OrdinalIgnoreCase) ||
            item.Name.Contains("СОУТ", StringComparison.OrdinalIgnoreCase);

        var active = list
            .Where(item => !item.IsInactive && IsSout(item))
            .OrderByDescending(item => item.OrderDate ?? DateTime.MinValue)
            .FirstOrDefault()
            ?? list
                .Where(IsSout)
                .OrderByDescending(item => item.OrderDate ?? DateTime.MinValue)
                .FirstOrDefault()
            ?? list
                .Where(item => !item.IsInactive)
                .OrderByDescending(item => item.OrderDate ?? DateTime.MinValue)
                .FirstOrDefault();

        if (active is null)
        {
            return info;
        }

        active.Members ??= [];
        if (active.Members.Count == 0 && !string.IsNullOrWhiteSpace(active.Composition))
        {
            active.Members = SoutCommissionMemberHelpers.MigrateFromCompositionText(active.Composition);
        }

        if (active.Members.Count > 0)
        {
            var chairman = active.Members.FirstOrDefault(m =>
                               SoutCommissionMemberHelpers.IsChairmanStatus(m.Status))
                           ?? active.Members[0];
            info.ChairmanName = chairman.Name?.Trim() ?? string.Empty;
            info.ChairmanProfession = chairman.Position?.Trim() ?? string.Empty;
            info.Members = active.Members
                .Where(m => !ReferenceEquals(m, chairman))
                .Where(m => !string.IsNullOrWhiteSpace(m.Name) || !string.IsNullOrWhiteSpace(m.Position))
                .Select(m => new SoutPreparationCommissionMember
                {
                    Name = m.Name?.Trim() ?? string.Empty,
                    Profession = m.Position?.Trim() ?? string.Empty
                })
                .ToList();
            EnrichCommissionProfessions(info, employees);
            return info;
        }

        if (string.IsNullOrWhiteSpace(active.Composition))
        {
            return info;
        }

        var names = active.Composition
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        if (names.Count == 0)
        {
            return info;
        }

        info.ChairmanName = names[0];
        info.Members = names.Skip(1).Select(name => new SoutPreparationCommissionMember { Name = name }).ToList();
        EnrichCommissionProfessions(info, employees);
        return info;
    }

    private static void EnrichCommissionProfessions(
        SoutPreparationCommissionInfo info,
        IEnumerable<OrganizationEmployee>? employees)
    {
        var staff = (employees ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .ToList();
        if (staff.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(info.ChairmanProfession) && !string.IsNullOrWhiteSpace(info.ChairmanName))
        {
            info.ChairmanProfession = FindEmployeeByName(staff, info.ChairmanName)?.Profession?.Trim() ?? string.Empty;
        }

        foreach (var member in info.Members)
        {
            if (string.IsNullOrWhiteSpace(member.Profession) && !string.IsNullOrWhiteSpace(member.Name))
            {
                member.Profession = FindEmployeeByName(staff, member.Name)?.Profession?.Trim() ?? string.Empty;
            }
        }
    }

    /// <summary>Сопоставление ФИО: полное, сокращённое («Иванов И.И.») или по фамилии.</summary>
    private static OrganizationEmployee? FindEmployeeByName(IReadOnlyList<OrganizationEmployee> staff, string name)
    {
        var needle = NormalizePersonName(name);
        if (needle.Length == 0)
        {
            return null;
        }

        var exact = staff.FirstOrDefault(e =>
            string.Equals(NormalizePersonName(e.Name), needle, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var needleParts = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (needleParts.Length == 0)
        {
            return null;
        }

        var surname = needleParts[0];
        var candidates = staff
            .Where(e =>
            {
                var parts = NormalizePersonName(e.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 && string.Equals(parts[0], surname, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var initials = ExtractInitials(needleParts.Skip(1));
        if (initials.Length == 0)
        {
            return candidates.FirstOrDefault(e =>
                NormalizePersonName(e.Name).Contains(needle, StringComparison.OrdinalIgnoreCase)
                || needle.Contains(NormalizePersonName(e.Name), StringComparison.OrdinalIgnoreCase));
        }

        return candidates.FirstOrDefault(e =>
        {
            var parts = NormalizePersonName(e.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var empInitials = ExtractInitials(parts.Skip(1));
            return empInitials.StartsWith(initials, StringComparison.OrdinalIgnoreCase)
                   || initials.StartsWith(empInitials, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string NormalizePersonName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ',
            value.Replace('.', ' ')
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ExtractInitials(IEnumerable<string> parts)
    {
        var chars = parts
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]))
            .ToArray();
        return new string(chars);
    }

    private static string ResolveProfession(WorkplaceRecord workplace)
    {
        if (!string.IsNullOrWhiteSpace(workplace.Profession))
        {
            return workplace.Profession;
        }

        return workplace.Name;
    }

    private static string ResolveEquipment(WorkplaceRecord workplace, IReadOnlyList<SoutCatalogRecord> equipment)
    {
        var fromOverride = OverrideOr(workplace, "equipment", string.Empty);
        if (!string.IsNullOrWhiteSpace(fromOverride))
        {
            return fromOverride;
        }

        var matched = equipment
            .Where(item => IsLinkedToWorkplace(item, workplace))
            .Select(FormatCatalogItem)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return matched.Count > 0 ? string.Join(", ", matched) : string.Empty;
    }

    private static string ResolveMaterials(WorkplaceRecord workplace, IReadOnlyList<SoutCatalogRecord> materials)
    {
        var fromOverride = OverrideOr(workplace, "materials", string.Empty);
        if (!string.IsNullOrWhiteSpace(fromOverride))
        {
            return fromOverride;
        }

        var matched = materials
            .Where(item => IsLinkedToWorkplace(item, workplace) ||
                           MatchesWorkplace(item.Description, workplace) ||
                           MatchesWorkplace(item.Comment, workplace))
            .Select(item => string.IsNullOrWhiteSpace(item.FullName) ? item.Name : item.FullName)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return matched.Count > 0 ? string.Join(", ", matched) : string.Empty;
    }

    /// <summary>Привязка по списку WorkplaceIds или по тексту «Место установки».</summary>
    public static bool IsLinkedToWorkplace(SoutCatalogRecord item, WorkplaceRecord workplace)
    {
        item.WorkplaceIds ??= [];
        if (item.WorkplaceIds.Count > 0 &&
            item.WorkplaceIds.Any(id => string.Equals(id, workplace.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return MatchesWorkplace(item.InstallationLocation, workplace);
    }

    private static bool MatchesWorkplace(string? haystack, WorkplaceRecord workplace)
    {
        if (string.IsNullOrWhiteSpace(haystack))
        {
            return false;
        }

        var text = haystack.Trim();
        return Contains(text, workplace.WorkplaceNumber)
               || Contains(text, workplace.Name)
               || Contains(text, workplace.Profession)
               || Contains(text, workplace.Department);
    }

    private static bool Contains(string haystack, string? needle) =>
        !string.IsNullOrWhiteSpace(needle) &&
        haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FormatCatalogItem(SoutCatalogRecord item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            parts.Add(item.Name.Trim());
        }

        if (!string.IsNullOrWhiteSpace(item.InventoryNumber))
        {
            parts.Add($"№ {item.InventoryNumber.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(item.Manufacturer))
        {
            parts.Add($"марка {item.Manufacturer.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(item.FactoryNumber))
        {
            parts.Add($"зав. № {item.FactoryNumber.Trim()}");
        }

        return string.Join(", ", parts);
    }

    private static string OverrideOr(WorkplaceRecord workplace, string key, string fallback)
    {
        workplace.PrintOverrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return workplace.PrintOverrides.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static bool SameOrg(string? left, string right) =>
        string.IsNullOrWhiteSpace(right)
        || OrganizationMatcher.Matches(left, right);
}

public sealed class SoutPreparationCommissionInfo
{
    public string ChairmanName { get; set; } = string.Empty;
    public string ChairmanProfession { get; set; } = string.Empty;
    public List<SoutPreparationCommissionMember> Members { get; set; } = [];
}

public sealed class SoutPreparationCommissionMember
{
    public string Name { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
}

public sealed class SoutPreparationEmployeeLookup
{
    public string Name { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Snils { get; set; } = string.Empty;
    public string OkCode { get; set; } = string.Empty;
}

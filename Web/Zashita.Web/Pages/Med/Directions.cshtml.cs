using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Med;
using Zashita.Core.Training;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Med;

[RequireLogin]
public class DirectionsModel : PageModel
{
    private readonly MedDirectionStore _store;
    private readonly MedExamStore _exams;
    private readonly MedicalOrganizationStore _medicalOrganizations;
    private readonly MedStandardStore _standards;
    private readonly MedFactorStore _factors;
    private readonly MedContextService _med;
    private readonly OrganizationEmployeesService _employees;
    private readonly OrganizationRequisitesStore _requisites;
    private readonly MedDirectionSignatureStorage _signatures;
    private readonly IWebHostEnvironment _env;

    public DirectionsModel(
        MedDirectionStore store,
        MedExamStore exams,
        MedicalOrganizationStore medicalOrganizations,
        MedStandardStore standards,
        MedFactorStore factors,
        MedContextService med,
        OrganizationEmployeesService employees,
        OrganizationRequisitesStore requisites,
        MedDirectionSignatureStorage signatures,
        IWebHostEnvironment env)
    {
        _store = store;
        _exams = exams;
        _medicalOrganizations = medicalOrganizations;
        _standards = standards;
        _factors = factors;
        _med = med;
        _employees = employees;
        _requisites = requisites;
        _signatures = signatures;
        _env = env;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<MedDirectionRecord> Rows { get; private set; } = [];
    public IReadOnlyList<MedicalOrganizationRecord> MedicalOrganizations { get; private set; } = [];
    public IReadOnlyList<string> EmployeeNames { get; private set; } = [];
    public IReadOnlyList<EmployeeLookupItem> EmployeeLookup { get; private set; } = [];
    public IReadOnlyList<FactorPickItem> FactorCatalog { get; private set; } = [];
    public IReadOnlyList<string> ProfessionOptions { get; private set; } = [];
    public IReadOnlyList<StandardFactorPack> StandardsForProfession { get; private set; } = [];
    public IReadOnlyList<MedDirectionFactorLine> SelectedFactors { get; private set; } = [];
    public IReadOnlyList<string> ExamTypeOptions { get; } =
    [
        "Предварительный",
        "Периодический",
        "Внеочередной",
        "Экспертиза профпригодности",
        "Предсменный / Предрейсовый",
        "Психиатрическое освидетельствование"
    ];
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EmployeeName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ExamType { get; set; } = "Предварительный";

    /// <summary>Факторы из планирования (п. 6.2, …) — только они попадут в направление.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ItemNumbers { get; set; }

    /// <summary>Вызов со страницы планирования медосмотров.</summary>
    [BindProperty(SupportsGet = true)]
    public bool FromSchedule { get; set; }

    [BindProperty] public DateTime Date { get; set; } = DateTime.Today;
    [BindProperty] public DateTime? ExamDate { get; set; }
    [BindProperty] public string MedicalOrgId { get; set; } = string.Empty;
    [BindProperty] public string LastName { get; set; } = string.Empty;
    [BindProperty] public string FirstName { get; set; } = string.Empty;
    [BindProperty] public string MiddleName { get; set; } = string.Empty;
    [BindProperty] public string Gender { get; set; } = string.Empty;
    [BindProperty] public DateTime? BirthDate { get; set; }
    [BindProperty] public string Snils { get; set; } = string.Empty;
    [BindProperty] public string Department { get; set; } = string.Empty;
    [BindProperty] public string ProfessionOrWorkType { get; set; } = string.Empty;
    [BindProperty] public string HireProfession { get; set; } = string.Empty;
    [BindProperty] public string PolicyNumber { get; set; } = string.Empty;
    [BindProperty] public string HomeAddress { get; set; } = string.Empty;
    [BindProperty] public string EmployerContacts { get; set; } = string.Empty;
    [BindProperty] public string OwnershipOkved { get; set; } = string.Empty;
    [BindProperty] public string AuthorizedRepresentative { get; set; } = string.Empty;
    [BindProperty] public string AuthorizedRepresentativePosition { get; set; } = string.Empty;
    [BindProperty] public IFormFile? SignatureFile { get; set; }
    [BindProperty] public bool ClearSignature { get; set; }
    public string SignatureImageUrl { get; private set; } = string.Empty;
    [BindProperty] public string ExperienceTotal { get; set; } = string.Empty;
    [BindProperty] public string ExperienceProfession { get; set; } = string.Empty;
    [BindProperty] public string ExperienceHazardous { get; set; } = string.Empty;

    [BindProperty] public List<string> FactorName { get; set; } = [];
    [BindProperty] public List<string> FactorCode988 { get; set; } = [];
    [BindProperty] public List<string> FactorCode29n { get; set; } = [];
    [BindProperty] public List<string> FactorLevel { get; set; } = [];
    [BindProperty] public List<string> FactorClass { get; set; } = [];

    public void OnGet()
    {
        Organization = _med.GetOrganization(HttpContext);
        ViewData["ActiveModule"] = "медосмотры";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = "Направления на медосмотр";
        ViewData["Subtitle"] = Organization;
        Rows = _store.LoadByOrganization(Organization);
        MedicalOrganizations = _medicalOrganizations.LoadByOrganization(Organization);
        EmployeeLookup = BuildEmployeeLookup(Organization);
        EmployeeNames = EmployeeLookup.Select(x => x.Name).ToList();
        ProfessionOptions = EmployeeLookup
            .Select(x => x.Profession.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => !x.Contains("удален", StringComparison.OrdinalIgnoreCase)
                        && !x.Contains("удалён", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        FactorCatalog = BuildFactorCatalog(_factors.LoadAll());
        StandardsForProfession = _standards.LoadByOrganization(Organization)
            .Select(s => new StandardFactorPack(
                s.Id,
                s.Name,
                s.Profession ?? string.Empty,
                s.Department ?? string.Empty,
                (s.Rules ?? [])
                    .Where(r => r.IsActive)
                    .Select(r => new StandardFactorRule(
                        MedItemNumber.Display(r.ItemNumber),
                        string.IsNullOrWhiteSpace(r.FactorName)
                            ? MedItemNumber.Display(r.ItemNumber)
                            : r.FactorName.Trim(),
                        MedPeriodicity.Normalize(r.Periodicity),
                        string.IsNullOrWhiteSpace(r.ExamKind) ? "Периодический" : r.ExamKind.Trim()))
                    .ToList()))
            .ToList();
        Message = TempData["Message"] as string;

        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var row = _store.GetById(EditId);
            if (row is not null)
            {
                Date = row.Date;
                ExamDate = row.ExamDate;
                ExamType = row.ExamType;
                LastName = row.LastName;
                FirstName = row.FirstName;
                MiddleName = row.MiddleName;
                EmployeeName = string.Join(' ', new[] { row.LastName, row.FirstName, row.MiddleName }.Where(x => !string.IsNullOrWhiteSpace(x)));
                Gender = row.Gender;
                BirthDate = row.BirthDate;
                Snils = OneCEmployeeListParser.FormatSnils(row.Snils);
                Department = row.Department;
                ProfessionOrWorkType = row.ProfessionOrWorkType;
                HireProfession = MedStandardAssignmentBuilder.IsPreliminaryKind(row.ExamType)
                    ? (string.IsNullOrWhiteSpace(row.HireProfession) ? row.ProfessionOrWorkType : row.HireProfession)
                    : string.Empty;
                PolicyNumber = row.PolicyNumber;
                HomeAddress = row.HomeAddress;
                EmployerContacts = row.EmployerContacts;
                OwnershipOkved = row.OwnershipOkved;
                AuthorizedRepresentative = row.AuthorizedRepresentative;
                AuthorizedRepresentativePosition = row.AuthorizedRepresentativePosition;
                SignatureImageUrl = CacheBustSignatureUrl(row.SignatureImageUrl, row.Id);
                ExperienceTotal = NormalizeOptionalExperience(row.ExperienceTotal);
                ExperienceProfession = string.IsNullOrWhiteSpace(row.ExperienceProfession) || row.ExperienceProfession == "0"
                    ? FormatProfessionExperienceYears(
                        (_employees.FindByName(Organization, string.Join(' ',
                            new[] { row.LastName, row.FirstName, row.MiddleName }.Where(x => !string.IsNullOrWhiteSpace(x))))
                        ?? _employees.FindByName(Organization, EmployeeName))?.EmploymentDate)
                    : row.ExperienceProfession;
                ExperienceHazardous = NormalizeOptionalExperience(row.ExperienceHazardous);
                NormalizeAuthorizedFields();
                SelectedFactors = row.Factors;

                var matchedMo = MedicalOrganizations.FirstOrDefault(x =>
                    string.Equals(x.Name, row.MedicalOrgName, StringComparison.OrdinalIgnoreCase));
                MedicalOrgId = matchedMo?.Id
                    ?? _medicalOrganizations.GetDefault(Organization)?.Id
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(HomeAddress))
                {
                    var emp = _employees.FindByName(Organization, EmployeeName)
                        ?? _employees.FindByName(Organization,
                            string.Join(' ', new[] { row.LastName, row.FirstName, row.MiddleName }
                                .Where(x => !string.IsNullOrWhiteSpace(x))));
                    if (!string.IsNullOrWhiteSpace(emp?.HomeAddress))
                    {
                        HomeAddress = emp.HomeAddress.Trim();
                    }
                }

                ApplyOrgRequisitesPrefill();
            }
        }
        else if (!string.IsNullOrWhiteSpace(EmployeeName))
        {
            ApplyEmployeePrefill(EmployeeName.Trim());
            if (!FromSchedule &&
                (string.IsNullOrWhiteSpace(ExamType) ||
                 string.Equals(ExamType, "Предварительный", StringComparison.OrdinalIgnoreCase)))
            {
                ExamType = "Периодический";
            }
            else if (string.IsNullOrWhiteSpace(ExamType))
            {
                ExamType = "Периодический";
            }

            if (SelectedFactors.Count == 0)
            {
                if (FromSchedule || !string.IsNullOrWhiteSpace(ItemNumbers))
                {
                    // С планирования — только факторы из таблицы потребностей, не весь стандарт.
                    SelectedFactors = BuildFactorsFromItemNumbers(
                        ItemNumbers,
                        Organization,
                        ProfessionOrWorkType,
                        Department,
                        ExamType,
                        _employees.FindByName(Organization, EmployeeName));
                }
                else
                {
                    SelectedFactors = BuildFactorsFromStandard(
                        Organization,
                        ProfessionOrWorkType,
                        Department,
                        ExamType,
                        _employees.FindByName(Organization, EmployeeName));
                }
            }

            EnsureAuthorizedDefaults();
            ApplyOrgRequisitesPrefill();
            MedicalOrgId = _medicalOrganizations.GetDefault(Organization)?.Id ?? string.Empty;
        }
        else
        {
            EnsureAuthorizedDefaults();
            ApplyOrgRequisitesPrefill();
            MedicalOrgId = _medicalOrganizations.GetDefault(Organization)?.Id ?? string.Empty;
        }
    }

    private void ApplyOrgRequisitesPrefill()
    {
        var requisites = _requisites.Get(Organization);
        if (string.IsNullOrWhiteSpace(EmployerContacts)
            && !string.IsNullOrWhiteSpace(requisites.EmployerContacts))
        {
            EmployerContacts = requisites.EmployerContacts;
        }

        if (string.IsNullOrWhiteSpace(OwnershipOkved)
            && !string.IsNullOrWhiteSpace(requisites.OwnershipOkved))
        {
            OwnershipOkved = requisites.OwnershipOkved;
        }
    }

    private void EnsureAuthorizedDefaults()
    {
        if (string.IsNullOrWhiteSpace(AuthorizedRepresentativePosition))
        {
            AuthorizedRepresentativePosition = "Специалист по охране труда";
        }

        if (string.IsNullOrWhiteSpace(AuthorizedRepresentative) ||
            LooksLikeJobTitle(AuthorizedRepresentative))
        {
            var signatory = ResolveOtSpecialistSignatory(Organization);
            if (!string.IsNullOrWhiteSpace(signatory))
            {
                AuthorizedRepresentative = signatory;
            }
        }
    }

    private void NormalizeAuthorizedFields()
    {
        // Старые записи: в одном поле была должность вместо ФИО.
        if (string.IsNullOrWhiteSpace(AuthorizedRepresentativePosition) &&
            LooksLikeJobTitle(AuthorizedRepresentative))
        {
            AuthorizedRepresentativePosition = AuthorizedRepresentative.Trim();
            AuthorizedRepresentative = string.Empty;
        }

        EnsureAuthorizedDefaults();
    }

    private string ResolveOtSpecialistSignatory(string organization)
    {
        var candidates = _employees.LoadForMedReport(organization)
            .Where(e => e.IsActive && !string.IsNullOrWhiteSpace(e.Name))
            .Where(e => (e.Profession ?? string.Empty).Contains("охране труда", StringComparison.OrdinalIgnoreCase)
                        || (e.Profession ?? string.Empty).Contains("охраны труда", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => string.Equals(
                ProfessionMatcher.Normalize(e.Profession),
                "Специалист по охране труда",
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var preferred = candidates.FirstOrDefault(e =>
            e.Name.Contains("Чудинова", StringComparison.OrdinalIgnoreCase));
        var person = preferred ?? candidates.FirstOrDefault();
        return person is null ? string.Empty : FormatInitialsAndLastName(person.Name);
    }

    private static bool LooksLikeJobTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        return text.Contains("специалист", StringComparison.OrdinalIgnoreCase)
               || text.Contains("руководитель", StringComparison.OrdinalIgnoreCase)
               || text.Contains("директор", StringComparison.OrdinalIgnoreCase)
               || text.Contains("инженер", StringComparison.OrdinalIgnoreCase)
               || text.Contains("начальник", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatInitialsAndLastName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        var last = parts[0];
        var initials = string.Join('.', parts.Skip(1).Select(p => p[0].ToString().ToUpperInvariant())) + ".";
        return $"{initials} {last}";
    }

    private void ApplyEmployeePrefill(string fullName)
    {
        EmployeeName = fullName;
        var employee = _employees.FindByName(Organization, fullName);
        var fio = !string.IsNullOrWhiteSpace(employee?.LastName)
            ? (employee.LastName, employee.FirstName, employee.Patronymic)
            : EmployeeFio.Split(fullName);
        LastName = fio.LastName;
        FirstName = fio.FirstName;
        MiddleName = fio.Patronymic;

        if (employee is null)
        {
            return;
        }

        BirthDate = employee.BirthDate;
        Snils = OneCEmployeeListParser.FormatSnils(employee.Snils);
        Department = employee.Department ?? string.Empty;
        ProfessionOrWorkType = ProfessionMatcher.Normalize(employee.Profession ?? string.Empty);
        HireProfession = MedStandardAssignmentBuilder.IsPreliminaryKind(ExamType)
            ? ProfessionOrWorkType
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(employee.HomeAddress))
        {
            HomeAddress = employee.HomeAddress.Trim();
        }

        // Общий и вредный стаж не подставляем; в профессии — от даты приёма.
        ExperienceTotal = string.Empty;
        ExperienceHazardous = string.Empty;
        ExperienceProfession = FormatProfessionExperienceYears(employee.EmploymentDate);
    }

    /// <summary>Стаж в годах с одной десятичной: 6 мес → 0,5; 18 мес → 1,5.</summary>
    internal static string FormatProfessionExperienceYears(DateTime? employmentDate, DateTime? asOf = null)
    {
        if (employmentDate is null || employmentDate.Value.Year < 1900)
        {
            return string.Empty;
        }

        var start = employmentDate.Value.Date;
        var end = (asOf ?? DateTime.Today).Date;
        if (end < start)
        {
            return "0";
        }

        var months = (end.Year - start.Year) * 12 + (end.Month - start.Month);
        if (end.Day < start.Day)
        {
            months--;
        }

        if (months < 0)
        {
            months = 0;
        }

        var years = months / 12.0;
        if (Math.Abs(years - Math.Round(years)) < 0.001)
        {
            return ((int)Math.Round(years)).ToString(CultureInfo.GetCultureInfo("ru-RU"));
        }

        return years.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string NormalizeOptionalExperience(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed is "" or "0" or "0,0" or "0.0" ? string.Empty : trimmed;
    }

    private static string ResolveProfessionExperience(
        string? posted,
        OrganizationEmployee? employee,
        MedDirectionRecord? existing)
    {
        var value = (posted ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(value) && value is not ("0" or "0,0" or "0.0"))
        {
            return value;
        }

        var fromHire = FormatProfessionExperienceYears(employee?.EmploymentDate);
        if (!string.IsNullOrWhiteSpace(fromHire))
        {
            return fromHire;
        }

        return NormalizeOptionalExperience(existing?.ExperienceProfession);
    }

    public IActionResult OnPostSave()
    {
        Organization = _med.GetOrganization(HttpContext);
        var id = string.IsNullOrWhiteSpace(EditId) ? Guid.NewGuid().ToString("N") : EditId!;
        var existing = _store.GetById(id);
        var orgRecord = string.IsNullOrWhiteSpace(MedicalOrgId)
            ? _medicalOrganizations.GetDefault(Organization)
            : _medicalOrganizations.GetById(MedicalOrgId);

        var last = TrimOrEmpty(LastName);
        var first = TrimOrEmpty(FirstName);
        var middle = TrimOrEmpty(MiddleName);
        if (string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(first))
        {
            var fio = SplitName(EmployeeName);
            last = fio.last;
            first = fio.first;
            middle = fio.middle;
        }

        if (string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(first))
        {
            TempData["Message"] = "Укажите ФИО: выберите сотрудника из штата или заполните фамилию и имя.";
            return RedirectToPage(new { editId = string.IsNullOrWhiteSpace(EditId) ? null : id });
        }

        var fullName = string.Join(' ', new[] { last, first, middle }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var employeeName = TrimOrEmpty(EmployeeName);
        var employee = _employees.FindByName(Organization, fullName)
            ?? (!string.IsNullOrWhiteSpace(employeeName) ? _employees.FindByName(Organization, employeeName) : null);

        var professionOrWorkType = TrimOrEmpty(ProfessionOrWorkType);
        var department = string.IsNullOrWhiteSpace(Department)
            ? employee?.Department ?? existing?.Department ?? string.Empty
            : Department.Trim();
        var finalProfession = string.IsNullOrWhiteSpace(professionOrWorkType)
            ? employee?.Profession ?? existing?.ProfessionOrWorkType ?? string.Empty
            : professionOrWorkType;
        var examType = string.IsNullOrWhiteSpace(ExamType) ? "Предварительный" : ExamType.Trim();
        var isPreliminary = MedStandardAssignmentBuilder.IsPreliminaryKind(examType);
        var factors = BuildFactorsFromForm();
        if (factors.Count == 0)
        {
            factors = BuildFactorsFromStandard(Organization, finalProfession, department, examType, employee);
        }

        string signatureUrl;
        try
        {
            signatureUrl = ResolveSignatureUrl(id, existing?.SignatureImageUrl);
        }
        catch (Exception ex)
        {
            TempData["Message"] = ex.Message;
            return RedirectToPage(new { editId = string.IsNullOrWhiteSpace(EditId) ? null : id });
        }

        try
        {
            var direction = new MedDirectionRecord
            {
                Id = id,
                Date = Date.Date,
                ExamDate = ExamDate,
                ExamType = examType,
                MedicalOrgName = orgRecord?.Name ?? existing?.MedicalOrgName ?? string.Empty,
                MedicalOrgDetails = orgRecord?.DetailsSummary ?? existing?.MedicalOrgDetails ?? string.Empty,
                MedicalOrgAddress = orgRecord?.Address ?? existing?.MedicalOrgAddress ?? string.Empty,
                Ogrn = orgRecord?.Ogrn ?? existing?.Ogrn ?? string.Empty,
                Inn = orgRecord?.Inn ?? existing?.Inn ?? string.Empty,
                Kpp = orgRecord?.Kpp ?? existing?.Kpp ?? string.Empty,
                LicenseInfo = orgRecord?.LicenseInfo ?? existing?.LicenseInfo ?? string.Empty,
                LastName = last,
                FirstName = first,
                MiddleName = middle,
                Gender = string.IsNullOrWhiteSpace(Gender) ? existing?.Gender ?? string.Empty : Gender.Trim(),
                BirthDate = BirthDate ?? employee?.BirthDate ?? existing?.BirthDate,
                Snils = OneCEmployeeListParser.FormatSnils(
                    string.IsNullOrWhiteSpace(Snils)
                        ? employee?.Snils ?? existing?.Snils ?? string.Empty
                        : Snils),
                PolicyNumber = TrimOrEmpty(PolicyNumber),
                HomeAddress = string.IsNullOrWhiteSpace(HomeAddress)
                    ? employee?.HomeAddress ?? existing?.HomeAddress ?? string.Empty
                    : HomeAddress.Trim(),
                Organization = Organization,
                EmployerContacts = ResolveEmployerContacts(EmployerContacts),
                OwnershipOkved = ResolveOwnershipOkved(OwnershipOkved),
                Department = string.IsNullOrWhiteSpace(Department) ? employee?.Department ?? existing?.Department ?? string.Empty : Department.Trim(),
                ProfessionOrWorkType = string.IsNullOrWhiteSpace(professionOrWorkType)
                    ? employee?.Profession ?? existing?.ProfessionOrWorkType ?? string.Empty
                    : professionOrWorkType,
                HireProfession = isPreliminary
                    ? (string.IsNullOrWhiteSpace(HireProfession) ? finalProfession : HireProfession.Trim())
                    : string.Empty,
                ExperienceTotal = TrimOrEmpty(ExperienceTotal),
                ExperienceProfession = ResolveProfessionExperience(ExperienceProfession, employee, existing),
                ExperienceHazardous = TrimOrEmpty(ExperienceHazardous),
                AuthorizedRepresentative = string.IsNullOrWhiteSpace(AuthorizedRepresentative) ||
                                           LooksLikeJobTitle(AuthorizedRepresentative)
                    ? ResolveOtSpecialistSignatory(Organization)
                    : AuthorizedRepresentative.Trim(),
                AuthorizedRepresentativePosition = string.IsNullOrWhiteSpace(AuthorizedRepresentativePosition)
                    ? "Специалист по охране труда"
                    : AuthorizedRepresentativePosition.Trim(),
                SignatureImageUrl = signatureUrl,
                Factors = factors,
                PrintOverrides = existing?.PrintOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                IsPosted = existing?.IsPosted ?? false
            };
            _store.Upsert(direction);

            var orgExams = _exams.LoadByOrganization(Organization).ToList();
            MedDirectionExamSync.ApplyDirectionIssued(direction, orgExams);
            _exams.UpsertMany(orgExams);

            var savedAddress = string.IsNullOrWhiteSpace(HomeAddress)
                ? employee?.HomeAddress ?? existing?.HomeAddress ?? string.Empty
                : HomeAddress.Trim();
            if (employee is not null && !string.IsNullOrWhiteSpace(savedAddress))
            {
                _employees.SaveHomeAddress(Organization, employee.Id, employee.Name, savedAddress);
            }
        }
        catch (Exception ex)
        {
            TempData["Message"] = $"Не удалось сохранить направление: {ex.Message}";
            return RedirectToPage(new { editId = string.IsNullOrWhiteSpace(EditId) ? null : id });
        }

        TempData["Message"] = "Направление сохранено.";
        return RedirectToPage(new { editId = id });
    }

    private string ResolveSignatureUrl(string directionId, string? existingUrl)
    {
        var current = MedDirectionSignatureStorage.NormalizeStoredUrl(existingUrl);
        if (ClearSignature)
        {
            _signatures.Delete(current);
            current = string.Empty;
        }

        if (SignatureFile is null || SignatureFile.Length == 0)
        {
            // Если в JSON путь есть, а файла нет — попробовать найти по id.
            if (!string.IsNullOrWhiteSpace(current)
                && !_signatures.TryRead(current, directionId, out _, out _, out _))
            {
                return _signatures.FindPublicUrlByDirectionId(directionId) ?? string.Empty;
            }

            return current;
        }

        const long maxBytes = 2 * 1024 * 1024;
        if (SignatureFile.Length > maxBytes)
        {
            throw new InvalidOperationException("Файл подписи больше 2 МБ.");
        }

        var ext = Path.GetExtension(SignatureFile.FileName ?? string.Empty).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp"))
        {
            throw new InvalidOperationException("Подпись: допустимы PNG, JPG, WEBP.");
        }

        using var stream = SignatureFile.OpenReadStream();
        var saved = _signatures.Save(directionId, stream, ext);
        if (!string.IsNullOrWhiteSpace(current)
            && !string.Equals(current, saved, StringComparison.OrdinalIgnoreCase))
        {
            _signatures.Delete(current);
        }

        return saved;
    }

    /// <summary>Для формы редактирования — data URL, чтобы превью не ломалось.</summary>
    private string CacheBustSignatureUrl(string? url, string? directionId = null) =>
        _signatures.TryResolveDataUrl(url, directionId) ?? string.Empty;

    public IActionResult OnPostDelete(string deleteId)
    {
        if (!string.IsNullOrWhiteSpace(deleteId))
        {
            var existing = _store.GetById(deleteId);
            if (existing is not null)
            {
                _signatures.Delete(existing.SignatureImageUrl);
            }

            _store.Delete(deleteId);
        }

        return RedirectToPage();
    }

    public IActionResult OnPostAutoPeriodicDirections()
    {
        Organization = _med.GetOrganization(HttpContext);
        var employees = _employees.LoadForMedReport(Organization)
            .Where(x => x.IsActive)
            .ToList();
        var standards = _standards.LoadByOrganization(Organization);
        var medicalDefault = _medicalOrganizations.GetDefault(Organization);
        var existing = _store.LoadByOrganization(Organization);
        var created = 0;
        var skipped = 0;

        foreach (var employee in employees)
        {
            var standard = MedStandardAssignmentBuilder.ResolveForEmployee(employee, standards);
            if (standard is null || !standard.IsPeriodic)
            {
                skipped++;
                continue;
            }

            var rules = MedStandardAssignmentBuilder.ResolveRulesByExamKind(standard, "Периодический");
            if (rules.Count == 0)
            {
                skipped++;
                continue;
            }

            var name = employee.Name?.Trim() ?? string.Empty;
            var duplicate = existing.Any(x =>
                string.Equals(x.ExamType, "Периодический", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(string.Join(' ', new[] { x.LastName, x.FirstName, x.MiddleName }.Where(v => !string.IsNullOrWhiteSpace(v))).Trim(),
                    name,
                    StringComparison.OrdinalIgnoreCase) &&
                x.Date.Date >= DateTime.Today.AddDays(-30));
            if (duplicate)
            {
                skipped++;
                continue;
            }

            var fio = SplitName(name);
            var mo = !string.IsNullOrWhiteSpace(standard.DefaultMedicalOrganizationId)
                ? _medicalOrganizations.GetById(standard.DefaultMedicalOrganizationId) ?? medicalDefault
                : medicalDefault;
            var direction = new MedDirectionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Date = DateTime.Today,
                ExamType = "Периодический",
                MedicalOrgName = mo?.Name ?? string.Empty,
                MedicalOrgDetails = mo?.DetailsSummary ?? string.Empty,
                MedicalOrgAddress = mo?.Address ?? string.Empty,
                Ogrn = mo?.Ogrn ?? string.Empty,
                Inn = mo?.Inn ?? string.Empty,
                Kpp = mo?.Kpp ?? string.Empty,
                LicenseInfo = mo?.LicenseInfo ?? string.Empty,
                LastName = fio.last,
                FirstName = fio.first,
                MiddleName = fio.middle,
                BirthDate = employee.BirthDate,
                Snils = OneCEmployeeListParser.FormatSnils(employee.Snils),
                Organization = Organization,
                Department = employee.Department,
                ProfessionOrWorkType = employee.Profession,
                HireProfession = string.Empty,
                HomeAddress = employee.HomeAddress ?? string.Empty,
                AuthorizedRepresentative = ResolveOtSpecialistSignatory(Organization),
                AuthorizedRepresentativePosition = "Специалист по охране труда",
                Factors = rules.Select(r => new MedDirectionFactorLine
                {
                    FactorName = string.IsNullOrWhiteSpace(r.FactorName) ? MedItemNumber.Display(r.ItemNumber) : r.FactorName,
                    Code29n = MedItemNumber.Display(r.ItemNumber)
                }).ToList()
            };
            _store.Upsert(direction);
            var orgExams = _exams.LoadByOrganization(Organization).ToList();
            MedDirectionExamSync.ApplyDirectionIssued(direction, orgExams);
            _exams.UpsertMany(orgExams);
            created++;
        }

        TempData["Message"] = $"Автосоздание направлений: создано {created}, пропущено {skipped}.";
        return RedirectToPage();
    }

    private List<MedDirectionFactorLine> BuildFactorsFromForm()
    {
        var list = new List<MedDirectionFactorLine>();
        var count = Math.Max(FactorName.Count, FactorCode29n.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var name = i < FactorName.Count ? FactorName[i]?.Trim() ?? "" : "";
            var code29 = i < FactorCode29n.Count ? FactorCode29n[i]?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(code29))
            {
                continue;
            }

            var key = $"{MedItemNumber.Normalize(code29)}|{name}";
            if (!seen.Add(key))
            {
                continue;
            }

            list.Add(new MedDirectionFactorLine
            {
                FactorName = name,
                Code988 = i < FactorCode988.Count ? FactorCode988[i]?.Trim() ?? "" : "",
                Code29n = string.IsNullOrWhiteSpace(code29) ? "" : MedItemNumber.Display(code29),
                Level = i < FactorLevel.Count ? FactorLevel[i]?.Trim() ?? "" : "",
                ConditionsClass = i < FactorClass.Count ? FactorClass[i]?.Trim() ?? "" : ""
            });
        }

        return list;
    }

    private List<MedDirectionFactorLine> BuildFactorsFromStandard(
        string organization,
        string profession,
        string department,
        string examType,
        OrganizationEmployee? employee)
    {
        var standards = _standards.LoadByOrganization(organization);
        var probe = employee is not null
            ? new OrganizationEmployee
            {
                Name = employee.Name,
                Profession = string.IsNullOrWhiteSpace(profession) ? employee.Profession : profession,
                Department = string.IsNullOrWhiteSpace(department) ? employee.Department : department
            }
            : new OrganizationEmployee
            {
                Profession = profession,
                Department = department
            };

        var standard = MedStandardAssignmentBuilder.ResolveForEmployee(probe, standards);
        if (standard is null)
        {
            return [];
        }

        return ResolveRulesForDirection(standard, examType)
            .Select(r => new MedDirectionFactorLine
            {
                FactorName = string.IsNullOrWhiteSpace(r.FactorName)
                    ? MedItemNumber.Display(r.ItemNumber)
                    : r.FactorName,
                Code29n = MedItemNumber.Display(r.ItemNumber)
            })
            .ToList();
    }

    /// <summary>
    /// Факторы строго по списку пунктов из планирования (не весь стандарт профессии).
    /// Имена подтягиваем из стандарта / справочника факторов.
    /// </summary>
    private List<MedDirectionFactorLine> BuildFactorsFromItemNumbers(
        string? itemNumbersRaw,
        string organization,
        string profession,
        string department,
        string examType,
        OrganizationEmployee? employee)
    {
        var wanted = (itemNumbersRaw ?? string.Empty)
            .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MedItemNumber.Normalize)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var nameByItem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in FactorCatalog)
        {
            var key = MedItemNumber.Normalize(f.ItemNumber);
            if (key.Length > 0 && !nameByItem.ContainsKey(key) && !string.IsNullOrWhiteSpace(f.Name))
            {
                nameByItem[key] = f.Name.Trim();
            }
        }

        var standards = _standards.LoadByOrganization(organization);
        var probe = employee is not null
            ? new OrganizationEmployee
            {
                Name = employee.Name,
                Profession = string.IsNullOrWhiteSpace(profession) ? employee.Profession : profession,
                Department = string.IsNullOrWhiteSpace(department) ? employee.Department : department
            }
            : new OrganizationEmployee
            {
                Profession = profession,
                Department = department
            };
        var standard = MedStandardAssignmentBuilder.ResolveForEmployee(probe, standards);
        if (standard is not null)
        {
            foreach (var rule in ResolveRulesForDirection(standard, examType)
                         .Concat((standard.Rules ?? []).Where(r => r.IsActive)))
            {
                var key = MedItemNumber.Normalize(rule.ItemNumber);
                if (key.Length == 0 || nameByItem.ContainsKey(key))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(rule.FactorName))
                {
                    nameByItem[key] = rule.FactorName.Trim();
                }
            }
        }

        return wanted
            .Select(item => new MedDirectionFactorLine
            {
                Code29n = MedItemNumber.Display(item),
                FactorName = nameByItem.TryGetValue(item, out var name) && name.Length > 0
                    ? name
                    : MedItemNumber.Display(item)
            })
            .ToList();
    }

    private static List<MedStandardRule> ResolveRulesForDirection(MedStandardRecord standard, string examType)
    {
        var rules = MedStandardAssignmentBuilder.ResolveRulesByExamKind(standard, examType);
        if (rules.Count > 0)
        {
            return rules;
        }

        if (examType.Contains("предварител", StringComparison.OrdinalIgnoreCase))
        {
            rules = MedStandardAssignmentBuilder.ResolveRulesByExamKind(standard, "Периодический");
            if (rules.Count > 0)
            {
                return rules;
            }
        }

        return (standard.Rules ?? []).Where(r => r.IsActive).ToList();
    }

    private static List<FactorPickItem> BuildFactorCatalog(IReadOnlyList<MedFactorNode> nodes)
    {
        var groups = nodes
            .Where(n => n.IsGroup)
            .ToDictionary(n => n.Id, n => n.Name, StringComparer.Ordinal);

        return TrainingCatalogTree
            .Order(nodes.ToList(), n => n.Id, n => n.ParentId, n => n.IsGroup, n => n.Name)
            .Where(item => !item.Node.IsGroup)
            .Select(item =>
            {
                var x = item.Node;
                var itemNumber = string.IsNullOrWhiteSpace(x.ItemNumber)
                    ? "—"
                    : MedItemNumber.Display(x.ItemNumber);
                var periodicity = string.IsNullOrWhiteSpace(x.Periodicity)
                    ? MedPeriodicity.OnceAYear
                    : MedPeriodicity.Normalize(x.Periodicity);
                var groupName = !string.IsNullOrWhiteSpace(x.ParentId) && groups.TryGetValue(x.ParentId, out var g)
                    ? g
                    : "—";
                return new FactorPickItem(
                    x.Id,
                    itemNumber,
                    string.IsNullOrWhiteSpace(x.Name) ? "—" : x.Name.Trim(),
                    periodicity,
                    groupName);
            })
            .ToList();
    }

    private List<EmployeeLookupItem> BuildEmployeeLookup(string organization)
    {
        return _employees.LoadForMedReport(organization)
            .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x =>
            {
                var fio = !string.IsNullOrWhiteSpace(x.LastName)
                    ? (x.LastName, x.FirstName, x.Patronymic)
                    : EmployeeFio.Split(x.Name);
                return new EmployeeLookupItem(
                    x.Name.Trim(),
                    fio.LastName,
                    fio.FirstName,
                    fio.Patronymic,
                    string.Empty,
                    x.BirthDate,
                    OneCEmployeeListParser.FormatSnils(x.Snils),
                    x.Department ?? string.Empty,
                    x.Profession ?? string.Empty,
                    x.HomeAddress ?? string.Empty);
            })
            .ToList();
    }

    private static (string last, string first, string middle) SplitName(string? fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (
            parts.ElementAtOrDefault(0) ?? string.Empty,
            parts.ElementAtOrDefault(1) ?? string.Empty,
            parts.ElementAtOrDefault(2) ?? string.Empty);
    }

    private string ResolveEmployerContacts(string? value)
    {
        var trimmed = TrimOrEmpty(value);
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return TrimOrEmpty(_requisites.Get(Organization).EmployerContacts);
    }

    private string ResolveOwnershipOkved(string? value)
    {
        var trimmed = TrimOrEmpty(value);
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return TrimOrEmpty(_requisites.Get(Organization).OwnershipOkved);
    }

    private static string TrimOrEmpty(string? value) => value?.Trim() ?? string.Empty;

    public sealed record EmployeeLookupItem(
        string Name,
        string LastName,
        string FirstName,
        string MiddleName,
        string Gender,
        DateTime? BirthDate,
        string Snils,
        string Department,
        string Profession,
        string HomeAddress);

    public sealed record FactorPickItem(
        string Id,
        string ItemNumber,
        string Name,
        string Periodicity,
        string GroupName);

    public sealed record StandardFactorPack(
        string Id,
        string Name,
        string Profession,
        string Department,
        IReadOnlyList<StandardFactorRule> Rules);

    public sealed record StandardFactorRule(
        string ItemNumber,
        string FactorName,
        string Periodicity,
        string ExamKind);
}

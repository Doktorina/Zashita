using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Organizations;

[RequireLogin]
public class IndexModel : PageModel
{
    private readonly OrganizationStore _organizations;
    private readonly OrganizationRenameService _renameService;
    private readonly OrganizationEmployeesService _employees;
    private readonly OrganizationRequisitesStore _requisites;
    private readonly RiskAssessmentObjectStore _riskObjects;
    private readonly GlobalSearchService _search;

    public IndexModel(
        OrganizationStore organizations,
        OrganizationRenameService renameService,
        OrganizationEmployeesService employees,
        OrganizationRequisitesStore requisites,
        RiskAssessmentObjectStore riskObjects,
        GlobalSearchService search)
    {
        _organizations = organizations;
        _renameService = renameService;
        _employees = employees;
        _requisites = requisites;
        _riskObjects = riskObjects;
        _search = search;
    }

    [BindProperty(SupportsGet = true)]
    public string? RenameFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Company { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditGroupId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool CreateGroup { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EditRiskObjectId { get; set; }

    public IReadOnlyList<string> Organizations => _organizations.AllOrganizations;
    public IReadOnlyList<OrganizationGroup> Groups => _organizations.GetGroups();
    public OrganizationGroup? EditGroup { get; private set; }
    public bool IsHeadAdmin => AuthHelper.IsHeadAdmin(HttpContext);
    public bool CanManageVisibility => AuthHelper.CanManageOrganizationVisibility(HttpContext);
    public string SelectedOrganization => _organizations.GetSelectedOrganization(HttpContext);
    public bool IsOrganizationHidden(string organization) => _organizations.IsOrganizationHidden(organization);
    public IReadOnlyList<OrganizationEmployee> CompanyEmployees { get; private set; } = [];
    public IReadOnlyList<RiskAssessmentObject> CompanyRiskObjects { get; private set; } = [];
    public OrganizationRequisites SelectedRequisites { get; private set; } = new();
    public IReadOnlyDictionary<string, string> RequisiteSummaries { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet()
    {
        if (TempData["Message"] is string tempMessage)
        {
            Message = tempMessage;
            IsError = string.Equals(TempData["IsError"] as string, "1", StringComparison.Ordinal);
        }

        LoadCompanyEmployees();
        LoadRequisites();
        LoadEditGroup();
        if (EditGroup is not null)
        {
            CreateGroup = false;
        }
    }

    public IActionResult OnPostAdd(string? name, string? orgType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            IsError = true;
            Message = "Введите название организации.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        var type = string.IsNullOrWhiteSpace(orgType) ? "ИП" : orgType.Trim();
        if (!OrganizationStore.CreatableOrganizationTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            IsError = true;
            Message = "Выберите тип организации: ИП, ООО или Другое.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        var formatted = OrganizationStore.FormatOrganizationName(name, type);
        if (!_organizations.AddOrganization(name, type))
        {
            IsError = true;
            Message = string.IsNullOrWhiteSpace(formatted)
                ? "Введите корректное название организации."
                : $"Организация «{formatted}» уже есть в списке.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        _search.RebuildIndex();
        Message = $"Организация «{formatted}» добавлена.";
        return RedirectToPage(new { company = formatted });
    }

    public IActionResult OnPostRename(string? oldName, string? newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            IsError = true;
            Message = "Укажите старое и новое название организации.";
            RenameFrom = oldName;
            SafeReload();
            return Page();
        }

        var from = oldName.Trim();
        var to = newName.Trim();

        try
        {
            if (!_organizations.RenameOrganization(from, to))
            {
                IsError = true;
                Message = "Не удалось переименовать организацию. Проверьте, что новое имя уникально.";
                RenameFrom = from;
                Company = from;
                SafeReload();
                return Page();
            }

            _renameService.Propagate(from, to);

            var current = _organizations.GetSelectedOrganization(HttpContext);
            if (string.Equals(current, from, StringComparison.OrdinalIgnoreCase)
                || string.Equals(current, to, StringComparison.OrdinalIgnoreCase))
            {
                _organizations.SetWorkspaceContext(HttpContext, to, null);
            }

            try
            {
                _search.RebuildIndex();
            }
            catch
            {
                // Индекс поиска не должен блокировать переименование.
            }

            TempData["Message"] = $"Организация переименована: «{from}» → «{to}». Данные обновлены.";

            var company = string.Equals(Company, from, StringComparison.OrdinalIgnoreCase)
                ? to
                : Company;

            return RedirectToPage(new { company });
        }
        catch (Exception)
        {
            IsError = true;
            Message = $"Не удалось полностью переименовать организацию «{from}». Попробуйте ещё раз.";
            RenameFrom = from;
            Company = string.IsNullOrWhiteSpace(Company) ? from : Company;
            SafeReload();
            return Page();
        }
    }

    private void SafeReload()
    {
        try
        {
            LoadCompanyEmployees();
            LoadRequisites();
        }
        catch
        {
            CompanyEmployees = [];
            SelectedRequisites = new OrganizationRequisites();
            RequisiteSummaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public IActionResult OnPostRemove(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_organizations.RemoveOrganization(name))
        {
            IsError = true;
            Message = "Не удалось удалить организацию.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        _employees.RemoveOrganization(name);
        _requisites.Remove(name);

        var current = _organizations.GetSelectedOrganization(HttpContext);
        if (string.Equals(current, name, StringComparison.OrdinalIgnoreCase))
        {
            _organizations.ClearSelectedOrganization(HttpContext);
        }

        _search.RebuildIndex();
        Message = "Организация удалена.";

        if (string.Equals(Company, name, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage();
        }

        return RedirectToPage(new { company = Company });
    }

    public IActionResult OnPostSave()
    {
        _organizations.Save();
        _search.RebuildIndex();
        Message = "Список организаций сохранён.";
        return RedirectToPage(new { company = Company });
    }

    public IActionResult OnPostToggleHidden(string? name, bool hidden)
    {
        if (!AuthHelper.CanManageOrganizationVisibility(HttpContext))
        {
            IsError = true;
            Message = "Скрытие организаций доступно только после входа в систему.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(name) || !_organizations.SetOrganizationHidden(name, hidden))
        {
            IsError = true;
            Message = hidden
                ? "Не удалось скрыть организацию."
                : "Не удалось показать организацию в списке.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        // Если скрыли текущую рабочую организацию — переключим сессию на видимую.
        _ = _organizations.GetSelectedOrganization(HttpContext);
        _search.RebuildIndex();
        Message = hidden
            ? $"Организация «{name.Trim()}» скрыта из выпадающего списка."
            : $"Организация «{name.Trim()}» снова отображается в списке.";
        return RedirectToPage(new { company = Company });
    }

    public IActionResult OnPostHideOutsideVictoria()
    {
        if (!AuthHelper.CanManageOrganizationVisibility(HttpContext))
        {
            IsError = true;
            Message = "Скрытие организаций доступно только после входа в систему.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        var count = _organizations.HideOrganizationsOutsideVictoriaContour();
        _ = _organizations.GetSelectedOrganization(HttpContext);
        _search.RebuildIndex();
        Message = count > 0
            ? $"Скрыто организаций вне контура «Виктория»: {count}."
            : "Все организации вне контура «Виктория» уже скрыты.";
        return RedirectToPage(new { company = Company });
    }

    public IActionResult OnPostSaveEmployee(
        string? company,
        string? employeeId,
        string? name,
        string? profession,
        string? department,
        string? snils,
        DateTime? birthDate,
        DateTime? employmentDate,
        string? employmentType,
        string? hrStatus,
        string? homeAddress)
    {
        if (string.IsNullOrWhiteSpace(company))
        {
            IsError = true;
            Message = "Не выбрана организация.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            IsError = true;
            Message = "Укажите ФИО сотрудника.";
            Company = company;
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        _employees.SaveEmployee(
            company,
            employeeId,
            name,
            profession,
            department,
            snils,
            birthDate,
            employmentDate,
            homeAddress,
            transferDate: null,
            employmentType,
            hrStatus);
        Message = string.IsNullOrWhiteSpace(employeeId) ? "Сотрудник добавлен." : "Данные сотрудника сохранены.";
        return RedirectToPage(new { company });
    }

    public IActionResult OnPostDeleteEmployee(string? company, string? employeeId)
    {
        if (!string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(employeeId))
        {
            _employees.DeleteEmployee(company, employeeId);
            Message = "Сотрудник удалён.";
        }

        return RedirectToPage(new { company });
    }

    public IActionResult OnPostSaveGroup(string? groupId, string? groupName, string[]? groupOrganizations)
    {
        var savedId = _organizations.UpsertGroup(groupId, groupName, groupOrganizations);
        if (string.IsNullOrWhiteSpace(savedId))
        {
            TempData["IsError"] = "1";
            TempData["Message"] = "Укажите название группы и отметьте хотя бы одну компанию.";
            return RedirectToPage(new
            {
                company = Company,
                editGroupId = groupId,
                createGroup = string.IsNullOrWhiteSpace(groupId)
            });
        }

        var name = groupName?.Trim() ?? string.Empty;
        TempData["Message"] = string.IsNullOrWhiteSpace(groupId)
            ? $"Группа «{name}» создана."
            : $"Группа «{name}» сохранена.";
        return RedirectToPage(new { company = Company, editGroupId = savedId });
    }

    public IActionResult OnPostDeleteGroup(string? groupId)
    {
        var deleted = string.IsNullOrWhiteSpace(groupId) ? null : _organizations.GetGroup(groupId);
        if (!_organizations.DeleteGroup(groupId))
        {
            TempData["IsError"] = "1";
            TempData["Message"] = "Не удалось удалить группу.";
            return RedirectToPage(new { company = Company, editGroupId = groupId });
        }

        if (string.Equals(_organizations.GetSelectedGroupId(HttpContext), groupId, StringComparison.OrdinalIgnoreCase))
        {
            _organizations.SetSelectedGroupId(HttpContext, OrganizationStore.AllGroupsId);
        }

        TempData["Message"] = deleted is null ? "Группа удалена." : $"Группа «{deleted.Name}» удалена.";
        return RedirectToPage(new { company = Company });
    }

    public IActionResult OnPostSaveRequisites(
        string? company,
        string? inn,
        string? kpp,
        string? ogrn,
        string? address,
        string? employerContacts,
        string? ownershipOkved,
        int? bankPrimaryIndex)
    {
        if (string.IsNullOrWhiteSpace(company))
        {
            IsError = true;
            Message = "Выберите организацию.";
            LoadCompanyEmployees();
            LoadRequisites();
            return Page();
        }

        var org = company.Trim();
        var labels = Request.Form["bankLabel"];
        var bankNames = Request.Form["bankName"];
        var biks = Request.Form["bik"];
        var cities = Request.Form["bankCity"];
        var checking = Request.Form["checkingAccount"];
        var correspondent = Request.Form["correspondentAccount"];
        var count = new[] { labels.Count, bankNames.Count, biks.Count, cities.Count, checking.Count, correspondent.Count }.Max();
        var banks = new List<OrganizationBankAccount>();
        var primary = bankPrimaryIndex ?? 0;
        for (var i = 0; i < count; i++)
        {
            string At(Microsoft.Extensions.Primitives.StringValues values) =>
                i < values.Count ? values[i].ToString() ?? string.Empty : string.Empty;

            banks.Add(new OrganizationBankAccount
            {
                Label = At(labels),
                BankName = At(bankNames),
                Bik = At(biks),
                BankCity = At(cities),
                CheckingAccount = At(checking),
                CorrespondentAccount = At(correspondent),
                IsPrimary = i == primary
            });
        }

        _requisites.Save(org, new OrganizationRequisites
        {
            Inn = inn ?? string.Empty,
            Kpp = kpp ?? string.Empty,
            Ogrn = ogrn ?? string.Empty,
            Address = address ?? string.Empty,
            Banks = banks,
            EmployerContacts = employerContacts ?? string.Empty,
            OwnershipOkved = ownershipOkved ?? string.Empty
        });

        TempData["Message"] = $"Реквизиты «{org}» сохранены.";
        return RedirectToPage(new { company = org });
    }

    public IActionResult OnPostSaveRiskObject(
        string? company,
        string? objectId,
        string? objectName,
        string? officialName)
    {
        var org = (company ?? Company ?? string.Empty).Trim();
        var id = (objectId ?? string.Empty).Trim();
        var name = (objectName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(id))
        {
            TempData["IsError"] = "1";
            TempData["Message"] = "Не удалось сохранить объект.";
            return RedirectToPage(new { company = org });
        }

        if (name.Length == 0)
        {
            TempData["IsError"] = "1";
            TempData["Message"] = "Укажите название объекта.";
            return RedirectToPage(new { company = org, editRiskObjectId = id });
        }

        var renameId = RiskObjectAssessmentScope.IsOrganizationWide(id)
            ? RiskAssessmentObjectIds.OrganizationWide
            : id;

        if (_riskObjects.Rename(org, renameId, name, officialName ?? string.Empty))
        {
            TempData["Message"] = $"Объект сохранён: «{name}».";
        }
        else
        {
            TempData["IsError"] = "1";
            TempData["Message"] = "Не удалось сохранить: объект не найден или такое имя уже есть.";
            return RedirectToPage(new { company = org, editRiskObjectId = id });
        }

        return RedirectToPage(new { company = org });
    }

    public IActionResult OnPostDeleteRiskObject(string? company, string? objectId)
    {
        var org = (company ?? Company ?? string.Empty).Trim();
        var id = (objectId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(org)
            || string.IsNullOrWhiteSpace(id)
            || RiskObjectAssessmentScope.IsOrganizationWide(id))
        {
            TempData["IsError"] = "1";
            TempData["Message"] = "Системный объект «по организации» удалить нельзя.";
            return RedirectToPage(new { company = org });
        }

        if (_riskObjects.Delete(org, id))
        {
            TempData["Message"] = "Объект удалён из списка.";
        }
        else
        {
            TempData["IsError"] = "1";
            TempData["Message"] = "Не удалось удалить объект.";
        }

        return RedirectToPage(new { company = org });
    }

    private void LoadEditGroup()
    {
        EditGroup = string.IsNullOrWhiteSpace(EditGroupId)
            ? null
            : _organizations.GetGroup(EditGroupId);
    }

    private void LoadCompanyEmployees()
    {
        if (string.IsNullOrWhiteSpace(Company))
        {
            Company = SelectedOrganization;
        }

        if (string.IsNullOrWhiteSpace(Company))
        {
            CompanyEmployees = [];
            CompanyRiskObjects = [];
            return;
        }

        CompanyEmployees = _employees.LoadDirectory(Company);
        CompanyRiskObjects = _riskObjects.Load(Company);
    }

    private void LoadRequisites()
    {
        RequisiteSummaries = _requisites.GetSummaries(Organizations);
        SelectedRequisites = string.IsNullOrWhiteSpace(Company)
            ? new OrganizationRequisites()
            : _requisites.Get(Company);
    }
}

public sealed class OrganizationGroupFormModel
{
    public string? Company { get; set; }
    public OrganizationGroup? Group { get; set; }
    public IReadOnlyList<string> Organizations { get; set; } = [];
}
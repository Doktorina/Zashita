using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Employees;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Risks.Commissions;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly SoutCommissionStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesService _employees;

    public EditModel(
        SoutCommissionStore store,
        OrganizationStore orgs,
        OrganizationEmployeesService employees)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ParentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IsGroup { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public SoutCommissionRecord Record { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<SoutCommissionRecord> GroupOptions { get; private set; } = [];
    public IReadOnlyList<OrganizationEmployee> EmployeeOptions { get; private set; } = [];
    public IReadOnlyList<string> StatusOptions { get; private set; } = SoutCommissionMemberHelpers.StatusOptions();
    public IReadOnlyList<string> ComplianceOptions { get; private set; } = SoutCommissionCompliance.All;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;

        // Без папки «Комиссии по рискам» новая комиссия не попадает в список модуля.
        var riskFolderId = _store.EnsureRiskFolder(Organization);
        var all = _store.Load(Organization);
        var scoped = SoutCommissionScope.Filter(all, SoutCommissionScopeKind.Risks);

        if (IsNew)
        {
            var defaultParent = string.IsNullOrWhiteSpace(ParentId)
                ? riskFolderId
                : ParentId.Trim();

            Record = new SoutCommissionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Organization = Organization,
                ParentId = defaultParent,
                IsGroup = IsGroup,
                Members = IsGroup
                    ? []
                    :
                    [
                        new SoutCommissionMember { Status = SoutCommissionMemberHelpers.RiskChairmanStatus }
                    ]
            };
        }
        else
        {
            var existing = scoped.FirstOrDefault(item => string.Equals(item.Id, Id, StringComparison.Ordinal))
                           ?? _store.Find(Organization, Id!);
            if (existing is null)
            {
                return RedirectToPage("/Risks/Commissions");
            }

            if (!SoutCommissionScope.BelongsToScope(existing, SoutCommissionScopeKind.Risks, all))
            {
                return RedirectToPage("/Sout/Commissions/Edit", new { id = existing.Id });
            }

            Record = existing;
            Record.Members ??= [];
            // Папка «Комиссии по рискам» — это группа без состава; открытие ✎ должно вести
            // на создание/редактирование комиссии, а не на пустую карточку группы.
            if (existing.IsGroup
                && string.Equals(
                    (existing.Name ?? string.Empty).Trim(),
                    RiskCommissionResolver.RiskFolderName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToPage("/Risks/Commissions/Edit", new { parentId = existing.Id });
            }

            if (!Record.IsGroup && Record.Members.Count == 0 && !string.IsNullOrWhiteSpace(Record.Composition))
            {
                Record.Members = SoutCommissionMemberHelpers.MigrateFromCompositionText(Record.Composition);
            }

            IsGroup = existing.IsGroup;
        }

        LoadLookups(scoped);
        ViewData["Title"] = IsNew
            ? (IsGroup ? "Комиссия (группа, создание)" : "Комиссия (создание)")
            : Record.Name;
        ViewData["Subtitle"] = Organization;
        return Page();
    }

    public IActionResult OnPostSave() => Save(close: false);

    public IActionResult OnPostSaveAndClose() => Save(close: true);

    private IActionResult Save(bool close)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        Record = BindRecordFromForm();
        IsGroup = Record.IsGroup;
        var scoped = SoutCommissionScope.Filter(_store.Load(Organization), SoutCommissionScopeKind.Risks);
        LoadLookups(scoped);
        ViewData["ActiveModule"] = "риски";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = Record.Name;
        ViewData["Subtitle"] = Organization;

        if (string.IsNullOrWhiteSpace(Record.Name))
        {
            Message = "Укажите наименование.";
            IsError = true;
            return Page();
        }

        var all = _store.Load(Organization);
        var riskFolderId = _store.EnsureRiskFolder(Organization);
        all = _store.Load(Organization);

        // Группы (кроме самой корневой папки рисков) тоже держим в дереве рисков.
        if (Record.IsGroup)
        {
            var isRootRiskFolder = string.Equals(
                (Record.Name ?? string.Empty).Trim(),
                RiskCommissionResolver.RiskFolderName,
                StringComparison.OrdinalIgnoreCase);
            if (!isRootRiskFolder && string.IsNullOrWhiteSpace(Record.ParentId))
            {
                Record.ParentId = riskFolderId;
            }
        }
        else
        {
            SoutCommissionScopeMigrator.EnsureRecordInScope(
                Record,
                all,
                SoutCommissionScopeKind.Risks,
                Organization);
            if (string.IsNullOrWhiteSpace(Record.ParentId))
            {
                Record.ParentId = riskFolderId;
            }
        }

        _store.Upsert(Organization, Record);
        if (!string.IsNullOrWhiteSpace(ReturnUrl) && IsSafeReturnUrl(ReturnUrl))
        {
            TempData["SelectedCommissionId"] = Record.Id;
            TempData["Message"] = "Комиссия сохранена.";
            return LocalRedirect(ReturnUrl);
        }

        if (close)
        {
            return RedirectToPage("/Risks/Commissions");
        }

        Id = Record.Id;
        Message = "Записано.";
        return Page();
    }

    private static bool IsSafeReturnUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var value = url.Trim();
        return value.StartsWith("/Risks/", StringComparison.OrdinalIgnoreCase)
               && !value.StartsWith("//", StringComparison.Ordinal);
    }

    private void LoadLookups(IReadOnlyList<SoutCommissionRecord> scoped)
    {
        GroupOptions = scoped.Where(item => item.IsGroup).ToList();
        EmployeeOptions = _employees.Load(Organization)
            .Where(e => e.IsActive && !string.IsNullOrWhiteSpace(e.Name))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        StatusOptions = SoutCommissionMemberHelpers.StatusOptions();
        ComplianceOptions = SoutCommissionCompliance.All;
    }

    private SoutCommissionRecord BindRecordFromForm()
    {
        var existing = !IsNew && !string.IsNullOrWhiteSpace(Id)
            ? _store.Find(Organization, Id!)
            : null;

        var record = existing ?? new SoutCommissionRecord
        {
            Id = Request.Form["recordId"].ToString() ?? Guid.NewGuid().ToString("N"),
            Organization = Organization,
            CreatedAt = DateTime.UtcNow
        };

        record.IsGroup = Request.Form["isGroup"] == "true";
        record.ParentId = string.IsNullOrWhiteSpace(Request.Form["parentId"]) ? null : Request.Form["parentId"].ToString()?.Trim();
        record.Name = Request.Form["name"].ToString()?.Trim() ?? string.Empty;
        record.OrderNumber = Request.Form["orderNumber"].ToString()?.Trim() ?? string.Empty;
        record.IsInactive = Request.Form["isInactive"] == "true" || Request.Form["isInactive"] == "on";
        record.CompositionCompliance = Request.Form["compositionCompliance"].ToString()?.Trim() ?? string.Empty;
        if (int.TryParse(Request.Form["certifiedInRostechnadzorCount"], out var certified) && certified >= 0)
        {
            record.CertifiedInRostechnadzorCount = certified;
        }
        else
        {
            record.CertifiedInRostechnadzorCount = null;
        }

        if (DateTime.TryParse(Request.Form["orderDate"], out var orderDate))
        {
            record.OrderDate = orderDate;
        }
        else
        {
            record.OrderDate = null;
        }

        if (DateTime.TryParse(Request.Form["compositionApprovalDate"], out var approvalDate))
        {
            record.CompositionApprovalDate = approvalDate;
        }
        else
        {
            record.CompositionApprovalDate = null;
        }

        if (int.TryParse(Request.Form["compositionCheckPeriodMonths"], out var period) && period > 0)
        {
            record.CompositionCheckPeriodMonths = period;
        }
        else
        {
            record.CompositionCheckPeriodMonths = null;
        }

        if (record.IsGroup)
        {
            record.Members = [];
            record.Composition = string.Empty;
        }
        else
        {
            record.Members = BindMembersFromForm();
            SoutCommissionMemberHelpers.SyncComposition(record);
            if (string.IsNullOrWhiteSpace(record.CompositionCompliance))
            {
                record.CompositionCompliance = SoutCommissionCompliance.Suggest(record);
            }
        }

        record.ModifiedAt = DateTime.UtcNow;
        return record;
    }

    private List<SoutCommissionMember> BindMembersFromForm()
    {
        var statuses = Request.Form["memberStatus"];
        var positions = Request.Form["memberPosition"];
        var names = Request.Form["memberName"];
        var count = Math.Max(statuses.Count, Math.Max(positions.Count, names.Count));
        var members = new List<SoutCommissionMember>();

        for (var i = 0; i < count; i++)
        {
            var status = i < statuses.Count ? statuses[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var position = i < positions.Count ? positions[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var name = i < names.Count ? names[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(status) &&
                string.IsNullOrWhiteSpace(position) &&
                string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                status = SoutCommissionMemberHelpers.NextDefaultStatus(members);
            }

            members.Add(new SoutCommissionMember
            {
                Status = status,
                Position = position,
                Name = name
            });
        }

        return members;
    }
}

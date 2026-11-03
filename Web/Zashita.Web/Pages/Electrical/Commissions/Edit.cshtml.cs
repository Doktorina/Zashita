using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Electrical;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Electrical.Commissions;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly ElectricalCommissionStore _store;
    private readonly ElectricalKnowledgeCheckStore _knowledge;
    private readonly OrganizationStore _orgs;

    public EditModel(
        ElectricalCommissionStore store,
        ElectricalKnowledgeCheckStore knowledge,
        OrganizationStore orgs)
    {
        _store = store;
        _knowledge = knowledge;
        _orgs = orgs;
    }

    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ParentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IsGroup { get; set; }

    public ElectricalCommissionRecord Record { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<ElectricalCommissionRecord> GroupOptions { get; private set; } = [];
    public IReadOnlyList<(string Name, string Position)> EmployeeOptions { get; private set; } = [];
    public IReadOnlyList<string> StatusOptions { get; private set; } = ElectricalCommissionMemberHelpers.StatusOptions();
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        SetModuleViewData();

        if (IsNew)
        {
            Record = new ElectricalCommissionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Organization = Organization,
                ParentId = string.IsNullOrWhiteSpace(ParentId) ? null : ParentId.Trim(),
                IsGroup = IsGroup,
                Members = IsGroup
                    ? []
                    :
                    [
                        new ElectricalCommissionMember { Status = ElectricalCommissionMemberHelpers.ChairmanStatus }
                    ]
            };
        }
        else
        {
            var existing = _store.Find(Organization, Id!);
            if (existing is null)
            {
                return RedirectToPage("/Electrical/Commissions");
            }

            Record = existing;
            Record.Members ??= [];
            IsGroup = existing.IsGroup;
        }

        LoadLookups();
        ViewData["Title"] = IsNew
            ? (IsGroup ? "Комиссия по ЭБ (группа)" : "Комиссия по ЭБ (создание)")
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
        LoadLookups();
        SetModuleViewData();
        ViewData["Title"] = Record.Name;
        ViewData["Subtitle"] = Organization;

        if (string.IsNullOrWhiteSpace(Record.Name))
        {
            Message = "Укажите наименование.";
            IsError = true;
            return Page();
        }

        _store.Upsert(Organization, Record);
        if (close)
        {
            return RedirectToPage("/Electrical/Commissions");
        }

        Id = Record.Id;
        Message = "Записано.";
        return Page();
    }

    private void SetModuleViewData()
    {
        ViewData["ActiveModule"] = "обучение";
        ViewData["IsModulePage"] = true;
    }

    private void LoadLookups()
    {
        GroupOptions = _store.Load(Organization).Where(item => item.IsGroup).ToList();
        // ФИО только из реестра проверки знаний ЭБ.
        EmployeeOptions = _knowledge.Load(Organization)
            .Where(r => !string.IsNullOrWhiteSpace(r.EmployeeName))
            .GroupBy(r => r.EmployeeName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return (Name: first.EmployeeName.Trim(), Position: first.Position?.Trim() ?? string.Empty);
            })
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        StatusOptions = ElectricalCommissionMemberHelpers.StatusOptions();
    }

    private ElectricalCommissionRecord BindRecordFromForm()
    {
        var existing = !IsNew && !string.IsNullOrWhiteSpace(Id)
            ? _store.Find(Organization, Id!)
            : null;

        var record = existing ?? new ElectricalCommissionRecord
        {
            Id = Request.Form["recordId"].ToString() ?? Guid.NewGuid().ToString("N"),
            Organization = Organization,
            CreatedAt = DateTime.UtcNow
        };

        record.IsGroup = Request.Form["isGroup"] == "true";
        record.ParentId = string.IsNullOrWhiteSpace(Request.Form["parentId"])
            ? null
            : Request.Form["parentId"].ToString()?.Trim();
        record.Name = Request.Form["name"].ToString()?.Trim() ?? string.Empty;
        record.OrderNumber = Request.Form["orderNumber"].ToString()?.Trim() ?? string.Empty;
        record.IsInactive = existing?.IsInactive ?? false;
        record.CompositionCompliance = existing?.CompositionCompliance ?? string.Empty;
        record.CertifiedInRostechnadzorCount = existing?.CertifiedInRostechnadzorCount;

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
            ElectricalCommissionMemberHelpers.SyncComposition(record);
            record.CompositionCompliance = ElectricalCommissionCalculator.SuggestCompliance(record);
            record.CertifiedInRostechnadzorCount = (record.Members ?? []).Count(m => m.CertifiedInRostechnadzor);
        }

        record.ModifiedAt = DateTime.UtcNow;
        return record;
    }

    private List<ElectricalCommissionMember> BindMembersFromForm()
    {
        var statuses = Request.Form["memberStatus"];
        var positions = Request.Form["memberPosition"];
        var names = Request.Form["memberName"];
        var certifiedFlags = Request.Form["memberCertified"];
        var count = Math.Max(statuses.Count, Math.Max(positions.Count, names.Count));
        var members = new List<ElectricalCommissionMember>();

        for (var i = 0; i < count; i++)
        {
            var status = i < statuses.Count ? statuses[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var position = i < positions.Count ? positions[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var name = i < names.Count ? names[i].ToString()?.Trim() ?? string.Empty : string.Empty;
            var certified = i < certifiedFlags.Count && certifiedFlags[i].ToString() == "true";
            if (string.IsNullOrWhiteSpace(status) &&
                string.IsNullOrWhiteSpace(position) &&
                string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                status = members.Count == 0
                    ? ElectricalCommissionMemberHelpers.ChairmanStatus
                    : ElectricalCommissionMemberHelpers.MemberStatus(members.Count);
            }

            members.Add(new ElectricalCommissionMember
            {
                Status = status,
                Position = position,
                Name = name,
                CertifiedInRostechnadzor = certified
            });
        }

        return members;
    }

    public string EmployeesJson => JsonSerializer.Serialize(
        EmployeeOptions.Select(e => new { name = e.Name, position = e.Position }));
}

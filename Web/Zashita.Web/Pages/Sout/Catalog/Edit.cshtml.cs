using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Med;
using Zashita.Core.Risks;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Sout.Catalog;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly SoutCatalogStore _store;
    private readonly WorkplaceStore _workplaces;
    private readonly OrganizationStore _orgs;
    private readonly MedFactorStore _factors;

    public EditModel(SoutCatalogStore store, WorkplaceStore workplaces, OrganizationStore orgs, MedFactorStore factors)
    {
        _store = store;
        _workplaces = workplaces;
        _orgs = orgs;
        _factors = factors;
    }

    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "equipment";

    [BindProperty(SupportsGet = true)]
    public string? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ParentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IsGroup { get; set; }

    public SoutCatalogRecord Record { get; set; } = new();
    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public bool IsEquipment => string.Equals(Kind, "equipment", StringComparison.OrdinalIgnoreCase);
    public string Organization { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public IReadOnlyList<SoutCatalogRecord> GroupOptions { get; private set; } = [];
    public IReadOnlyList<WorkplaceRecord> WorkplaceOptions { get; private set; } = [];
    public IReadOnlyList<string> FactorOptions { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public IActionResult OnGet()
    {
        if (!LoadPage())
        {
            return RedirectToPage("/Sout/Catalog", new { kind = Kind });
        }

        if (IsNew)
        {
            Record = new SoutCatalogRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Organization = Organization,
                ParentId = string.IsNullOrWhiteSpace(ParentId) ? null : ParentId.Trim(),
                IsGroup = IsGroup,
                WorkplaceIds = []
            };
            var all = IsEquipment ? _store.LoadEquipment(Organization) : _store.LoadMaterials(Organization);
            Record.Code = _store.SuggestNextCode(all);
        }
        else
        {
            var existing = IsEquipment
                ? _store.FindEquipment(Organization, Id!)
                : _store.FindMaterials(Organization, Id!);
            if (existing is null)
            {
                return RedirectToPage("/Sout/Catalog", new { kind = Kind });
            }

            Record = existing;
            Record.WorkplaceIds ??= [];
            IsGroup = existing.IsGroup;
        }

        return Page();
    }

    public IActionResult OnPostSave() => Save(close: false);

    public IActionResult OnPostSaveAndClose() => Save(close: true);

    private IActionResult Save(bool close)
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        if (!IsEquipment && !string.Equals(Kind, "materials", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage("/Sout/Catalog", new { kind = Kind });
        }

        GroupOptions = IsEquipment
            ? _store.LoadEquipment(Organization).Where(item => item.IsGroup).ToList()
            : _store.LoadMaterials(Organization).Where(item => item.IsGroup).ToList();
        WorkplaceOptions = LoadWorkplaceOptions();
        FactorOptions = LoadFactorOptions();

        Record = BindRecordFromForm();
        IsGroup = Record.IsGroup;
        Title = IsEquipment
            ? (Record.IsGroup ? "Оборудование (группа)" : "Оборудование")
            : (Record.IsGroup ? "Материал, сырье (группа)" : "Материал, сырье");
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew ? $"{Title} (создание)" : Title;
        ViewData["Subtitle"] = "общий для всех ЮЛ";

        if (string.IsNullOrWhiteSpace(Record.Name))
        {
            Message = "Укажите наименование.";
            IsError = true;
            return Page();
        }

        if (IsEquipment)
        {
            _store.UpsertEquipment(Organization, Record);
        }
        else
        {
            _store.UpsertMaterials(Organization, Record);
        }

        if (close)
        {
            return RedirectToPage("/Sout/Catalog", new { kind = Kind });
        }

        Id = Record.Id;
        Message = "Записано.";
        return Page();
    }

    private bool LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "специальная-оценка";
        ViewData["IsModulePage"] = true;
        WorkplaceOptions = LoadWorkplaceOptions();
        FactorOptions = LoadFactorOptions();

        if (IsEquipment)
        {
            Title = Record.IsGroup || IsGroup ? "Оборудование (группа)" : "Оборудование";
            GroupOptions = _store.LoadEquipment(Organization).Where(item => item.IsGroup).ToList();
        }
        else if (string.Equals(Kind, "materials", StringComparison.OrdinalIgnoreCase))
        {
            Title = Record.IsGroup || IsGroup ? "Материал, сырье (группа)" : "Материал, сырье";
            GroupOptions = _store.LoadMaterials(Organization).Where(item => item.IsGroup).ToList();
        }
        else
        {
            return false;
        }

        ViewData["Title"] = IsNew ? $"{Title} (создание)" : Title;
        ViewData["Subtitle"] = "общий для всех ЮЛ";
        return true;
    }

    private SoutCatalogRecord BindRecordFromForm()
    {
        var existing = !IsNew && !string.IsNullOrWhiteSpace(Id)
            ? (IsEquipment ? _store.FindEquipment(Organization, Id!) : _store.FindMaterials(Organization, Id!))
            : null;

        var record = existing ?? new SoutCatalogRecord
        {
            Id = string.IsNullOrWhiteSpace(Request.Form["recordId"]) ? Guid.NewGuid().ToString("N") : Request.Form["recordId"].ToString()!,
            Organization = Organization,
            CreatedAt = DateTime.UtcNow
        };

        record.IsGroup = Request.Form["isGroup"] == "true";
        record.ParentId = string.IsNullOrWhiteSpace(Request.Form["parentId"]) ? null : Request.Form["parentId"].ToString()?.Trim();
        record.Code = Request.Form["code"].ToString()?.Trim() ?? string.Empty;
        record.Name = Request.Form["name"].ToString()?.Trim() ?? string.Empty;
        record.FullName = Request.Form["fullName"].ToString()?.Trim() ?? string.Empty;
        record.TypeName = Request.Form["typeName"].ToString()?.Trim() ?? string.Empty;
        record.InventoryNumber = Request.Form["inventoryNumber"].ToString()?.Trim() ?? string.Empty;
        record.RegistrationNumber = Request.Form["registrationNumber"].ToString()?.Trim() ?? string.Empty;
        record.StatePlate = Request.Form["statePlate"].ToString()?.Trim() ?? string.Empty;
        record.FactoryNumber = Request.Form["factoryNumber"].ToString()?.Trim() ?? string.Empty;
        record.SerialNumber = Request.Form["serialNumber"].ToString()?.Trim() ?? string.Empty;
        record.PassportNumber = Request.Form["passportNumber"].ToString()?.Trim() ?? string.Empty;
        record.Manufacturer = Request.Form["manufacturer"].ToString()?.Trim() ?? string.Empty;
        record.FactorsSet = Request.Form["factorsSet"].ToString()?.Trim() ?? string.Empty;
        record.Description = Request.Form["description"].ToString()?.Trim() ?? string.Empty;
        record.Instruction = Request.Form["instruction"].ToString()?.Trim() ?? string.Empty;
        record.Comment = Request.Form["comment"].ToString()?.Trim() ?? string.Empty;

        if (DateTime.TryParse(Request.Form["releaseDate"], out var releaseDate))
        {
            record.ReleaseDate = releaseDate;
        }

        if (DateTime.TryParse(Request.Form["installationDate"], out var installationDate))
        {
            record.InstallationDate = installationDate;
        }

        var linkedIds = Request.Form["linkedWorkplaceId"]
            .Select(v => v.ToString()?.Trim() ?? string.Empty)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        record.WorkplaceIds = linkedIds;

        var linkedWorkplaces = LoadWorkplaceOptions()
            .Where(w => linkedIds.Contains(w.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (linkedWorkplaces.Count > 0)
        {
            record.InstallationLocation = string.Join(", ",
                linkedWorkplaces
                    .Select(w => string.IsNullOrWhiteSpace(w.WorkplaceNumber) ? w.Name : w.WorkplaceNumber)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        else
        {
            record.InstallationLocation = Request.Form["installationLocation"].ToString()?.Trim() ?? string.Empty;
        }

        record.ModifiedAt = DateTime.UtcNow;
        return record;
    }

    private List<WorkplaceRecord> LoadWorkplaceOptions() =>
        _workplaces.LoadTreeByOrganization(Organization)
            .Where(w => !w.IsGroup)
            .OrderBy(w => w.Department, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.WorkplaceNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private IReadOnlyList<string> LoadFactorOptions()
    {
        var names = _factors.LoadAll()
            .Where(node => node.IsActive && !string.IsNullOrWhiteSpace(node.Name))
            .Select(node => node.IsGroup
                ? node.Name.Trim()
                : string.IsNullOrWhiteSpace(node.ItemNumber)
                    ? node.Name.Trim()
                    : $"{node.ItemNumber} {node.Name.Trim()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return
            [
                "Общие факторы",
                "Химические вещества",
                "Производственное оборудование"
            ];
        }

        return names;
    }
}

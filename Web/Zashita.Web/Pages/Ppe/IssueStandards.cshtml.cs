using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe;

[RequireLogin]
public class IssueStandardsModel : PageModel
{
    private readonly IssueStandardsStore _standards;
    private readonly PpeNomenclatureStore _nomenclature;
    private readonly EtnNomenclatureStore _etn;
    private readonly PollutantsEtnStore _pollutants;
    private readonly Appendix1NormsStore _appendix1;
    private readonly OrganizationStore _orgs;
    private readonly PpeEmployeesService _employees;

    public IssueStandardsModel(
        IssueStandardsStore standards,
        PpeNomenclatureStore nomenclature,
        EtnNomenclatureStore etn,
        PollutantsEtnStore pollutants,
        Appendix1NormsStore appendix1,
        OrganizationStore orgs,
        PpeEmployeesService employees)
    {
        _standards = standards;
        _nomenclature = nomenclature;
        _etn = etn;
        _pollutants = pollutants;
        _appendix1 = appendix1;
        _orgs = orgs;
        _employees = employees;
    }

    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<IssueStandard> Standards { get; private set; } = [];
    public IReadOnlyList<PpeNomenclatureItem> Nomenclature { get; private set; } = [];
    public IReadOnlyList<PpeNomenclatureItem> FilteredNomenclature { get; private set; } = [];
    public IReadOnlyList<string> Professions { get; private set; } = [];
    public IReadOnlyList<string> ProtectionTypes { get; private set; } = [];
    public string? Message { get; private set; }
    public string? InfoMessage { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? EditId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ProtectionTypeFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Возврат в мастер карточки после сохранения: returnTo=sios.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnDoc { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ReturnStep { get; set; }

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Profession { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string ItemCategory { get; set; } = "СИЗ";

    public bool IncludeSiz => IssueStandard.IncludesSiz(ItemCategory);

    public bool IncludeDsiz => IssueStandard.IncludesDsiz(ItemCategory);

    public static readonly string[] ItemCategoryOptions = ["СИЗ", "ДСИЗ", "СИЗ+ДСИЗ"];

    public IReadOnlyList<string> CategoryOptions => ItemCategoryOptions;

    [BindProperty]
    public List<string> SelectedItems { get; set; } = [];

    public void OnGet()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        LoadLists();
        if (!string.IsNullOrWhiteSpace(EditId))
        {
            var std = Standards.FirstOrDefault(s => s.Id == EditId);
            if (std is not null)
            {
                Name = std.Name;
                if (string.IsNullOrWhiteSpace(Profession))
                {
                    Profession = std.Profession;
                }

                ItemCategory = NormalizeItemCategory(std.Category);
                SelectedItems = std.Items.ToList();
            }
        }
        else if (string.Equals(ReturnTo, "sios", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Profession))
            {
                var kind = PpeCardTypes.IncludesDsiz(ReturnDoc ?? "") ? "ДСИЗ" : "СИЗ";
                Name = $"Нормы выдачи ({kind}) — {Profession}";
            }

            if (!string.IsNullOrWhiteSpace(ReturnDoc))
            {
                ItemCategory = PpeCardTypes.IncludesDsiz(ReturnDoc) && !PpeCardTypes.IncludesSiz(ReturnDoc)
                    ? "ДСИЗ"
                    : PpeCardTypes.IncludesDsiz(ReturnDoc) && PpeCardTypes.IncludesSiz(ReturnDoc)
                        ? "СИЗ+ДСИЗ"
                        : "СИЗ";
            }
        }

        ItemCategory = NormalizeItemCategory(ItemCategory);
        ApplyLegacyCategoryQuery();
        ApplyFilters();
        ApplyStandardsSearch();
    }

    public IActionResult OnPostSave()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ItemCategory = NormalizeItemCategory(Request.Form["ItemCategory"].ToString());
        ProtectionTypeFilter = Request.Form["ProtectionTypeFilter"].ToString();
        ReturnTo = Request.Form["ReturnTo"].ToString();
        ReturnDoc = Request.Form["ReturnDoc"].ToString();
        if (int.TryParse(Request.Form["ReturnStep"], out var rs))
        {
            ReturnStep = rs;
        }

        LoadLists();
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Profession))
        {
            Message = "Укажите название и профессию.";
            ApplyFilters();
            return Page();
        }

        var items = SelectedItems.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (items.Count == 0)
        {
            Message = "Выберите хотя бы одну позицию.";
            ApplyFilters();
            return Page();
        }

        var category = IssueStandard.EncodeCategory(IncludeSiz, IncludeDsiz);
        var existing = string.IsNullOrWhiteSpace(EditId) ? null : _standards.LoadAll().FirstOrDefault(s => s.Id == EditId);
        var record = new IssueStandard(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            Name.Trim(),
            Organization,
            Profession.Trim(),
            items,
            existing?.CreatedAt ?? DateTime.UtcNow,
            existing?.Workers,
            category);
        _standards.Upsert(record);

        if (string.Equals(ReturnTo, "sios", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToPage(
                "/Ppe/SiosCard/Index",
                new
                {
                    doc = string.IsNullOrWhiteSpace(ReturnDoc) ? PpeCardTypes.PersonalSiz : ReturnDoc,
                    step = ReturnStep is > 0 and <= 3 ? ReturnStep.Value : 3,
                    standardIdFromCreate = record.Id
                });
        }

        return RedirectToPage(new { editId = record.Id });
    }

    public IActionResult OnPostFillFromAppendix()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ItemCategory = NormalizeItemCategory(Request.Form["ItemCategory"].ToString());
        ProtectionTypeFilter = Request.Form["ProtectionTypeFilter"].ToString();
        Name = Request.Form["Name"].ToString() ?? Name;
        Profession = Request.Form["Profession"].ToString() ?? Profession;
        EditId = Request.Form["EditId"].ToString();

        LoadLists();
        if (string.IsNullOrWhiteSpace(Profession))
        {
            Message = "Сначала выберите профессию.";
            ApplyFilters();
            return Page();
        }

        var appendixItems = _appendix1.GetForProfession(Profession);
        if (appendixItems.Count == 0)
        {
            Message = $"В Приложении 1 нет позиций для профессии «{Profession}».";
            ApplyFilters();
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(ProtectionTypeFilter))
        {
            appendixItems = appendixItems
                .Where(e => string.Equals(e.ProtectionType, ProtectionTypeFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!IncludeSiz && !IncludeDsiz)
        {
            ItemCategory = "СИЗ";
        }

        SelectedItems = appendixItems
            .Select(e => e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => MatchesCategoryFilter(name))
            .ToList();

        if (SelectedItems.Count == 0)
        {
            Message = "По Приложению 1 нет позиций для выбранного типа (СИЗ / ДСИЗ).";
            ApplyFilters();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = $"ЕТН — {Profession}";
        }

        var kind = ItemCategory;
        InfoMessage = $"Отмечено позиций по Приложению 1 ({kind}): {SelectedItems.Count}.";
        ApplyFilters();
        return Page();
    }

    public IActionResult OnPostDelete(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            _standards.Delete(id);
        }

        return RedirectToPage();
    }

    private void LoadLists()
    {
        Standards = _standards.LoadAll()
            .Where(s => string.IsNullOrWhiteSpace(s.Organization)
                || string.Equals(s.Organization.Trim(), Organization, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        Nomenclature = _nomenclature.LoadForEditor(_standards.LoadAll());

        // Только профессии выбранного ИП/ООО (из сотрудников + уже созданных стандартов этой орг.).
        // Полный справочник Приложения 1 сюда не подмешиваем.
        var fromEmployeesRaw = _employees.GetByProfession(Organization).Keys
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        var fromEmployeesReal = fromEmployeesRaw
            .Where(p => !string.Equals(p, "Сотрудники", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var fromEmployees = fromEmployeesReal.Count > 0 ? fromEmployeesReal : fromEmployeesRaw;
        var fromStandards = Standards
            .Select(s => s.Profession)
            .Where(p => !string.IsNullOrWhiteSpace(p));
        Professions = fromEmployees
            .Concat(fromStandards)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (IncludeDsiz && !IncludeSiz && _pollutants.HasData)
        {
            ProtectionTypes = _pollutants.GetProtectionTypes();
        }
        else if (IncludeSiz && !IncludeDsiz && _etn.HasData)
        {
            ProtectionTypes = _etn.GetProtectionTypes();
        }
        else if (_etn.HasData || _pollutants.HasData)
        {
            ProtectionTypes = _etn.GetProtectionTypes()
                .Concat(_pollutants.GetProtectionTypes())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        else if (!string.IsNullOrWhiteSpace(Profession) && _appendix1.GetForProfession(Profession).Count > 0)
        {
            ProtectionTypes = _appendix1.GetProtectionTypes(Profession);
        }
        else
        {
            ProtectionTypes = _nomenclature.GetProtectionTypes(_standards.LoadAll());
        }
    }

    private void ApplyStandardsSearch()
    {
        if (string.IsNullOrWhiteSpace(Q))
        {
            return;
        }

        var query = Q.Trim();
        Standards = Standards
            .Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Profession.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.ListDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Items.Any(i => i.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void ApplyFilters()
    {
        var query = Nomenclature.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(ProtectionTypeFilter))
        {
            query = query.Where(i =>
                string.Equals(i.DisplayProtectionType, ProtectionTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!IncludeDsiz && IncludeSiz)
        {
            query = query.Where(i => IssueStandard.IncludesSiz(i.Category));
        }
        else if (IncludeDsiz && !IncludeSiz)
        {
            query = query.Where(i => IssueStandard.IncludesDsiz(i.Category));
        }
        else if (IncludeSiz && IncludeDsiz)
        {
            query = query.Where(i =>
                IssueStandard.IncludesSiz(i.Category) || IssueStandard.IncludesDsiz(i.Category));
        }

        FilteredNomenclature = query.ToList();
    }

    private bool MatchesCategoryFilter(string itemName)
    {
        var item = Nomenclature.FirstOrDefault(n =>
            n.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return IncludeSiz;
        }

        if (IncludeSiz && IncludeDsiz)
        {
            return IssueStandard.IncludesSiz(item.Category) || IssueStandard.IncludesDsiz(item.Category);
        }

        if (IncludeDsiz && !IncludeSiz)
        {
            return IssueStandard.IncludesDsiz(item.Category);
        }

        return IssueStandard.IncludesSiz(item.Category);
    }

    private static string NormalizeItemCategory(string? value)
    {
        var token = (value ?? string.Empty).Trim().Replace(" ", "");
        if (token.Equals("ДСИЗ", StringComparison.OrdinalIgnoreCase))
        {
            return "ДСИЗ";
        }

        if (token.Contains('+', StringComparison.Ordinal))
        {
            return "СИЗ+ДСИЗ";
        }

        return "СИЗ";
    }

    private void ApplyLegacyCategoryQuery()
    {
        if (!Request.Query.ContainsKey("includeSiz") && !Request.Query.ContainsKey("includeDsiz"))
        {
            return;
        }

        var siz = Request.Query["includeSiz"] == "true";
        var dsiz = Request.Query["includeDsiz"] == "true";
        ItemCategory = IssueStandard.EncodeCategory(siz, dsiz);
    }
}

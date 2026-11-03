using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Core.Ppe;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Ppe.Acceptance;

[RequireLogin]
public class EditModel : PageModel
{
    private readonly PpeAcceptanceStore _store;
    private readonly OrganizationStore _orgs;
    private readonly OrganizationEmployeesStore _employees;

    public EditModel(PpeAcceptanceStore store, OrganizationStore orgs, OrganizationEmployeesStore employees)
    {
        _store = store;
        _orgs = orgs;
        _employees = employees;
    }

    [BindProperty(SupportsGet = true)] public string? Id { get; set; }

    public bool IsNew => string.IsNullOrWhiteSpace(Id);
    public string Organization { get; private set; } = string.Empty;
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public PpeAcceptanceDocument Document { get; private set; } = new();

    [BindProperty] public string Number { get; set; } = string.Empty;
    [BindProperty] public DateTime Date { get; set; } = DateTime.Today;
    [BindProperty] public string Department { get; set; } = string.Empty;
    [BindProperty] public string Warehouse { get; set; } = "Склад спецодежды";
    [BindProperty] public List<string> LineTabNumber { get; set; } = [];
    [BindProperty] public List<string> LineEmployeeName { get; set; } = [];
    [BindProperty] public List<string> LineNomenclature { get; set; } = [];
    [BindProperty] public List<string> LineQuantity { get; set; } = [];
    [BindProperty] public List<string> LineUnit { get; set; } = [];
    [BindProperty] public List<string> LinePrice { get; set; } = [];

    public string? Message { get; private set; }
    public bool IsError { get; private set; }

    public void OnGet()
    {
        LoadPage();
        if (IsNew)
        {
            Number = _store.SuggestNextNumber(Organization);
            Date = DateTime.Today;
            EnsureLineRows();
            return;
        }

        var doc = _store.GetById(Id!);
        if (doc is null)
        {
            Id = null;
            Number = _store.SuggestNextNumber(Organization);
            Date = DateTime.Today;
            EnsureLineRows();
            return;
        }

        Document = doc;
        Number = doc.Number;
        Date = doc.Date;
        Department = doc.Department;
        Warehouse = doc.Warehouse;
        LoadLines(doc);
        EnsureLineRows();
    }

    public IActionResult OnPostSave() => Save(post: false, close: false);
    public IActionResult OnPostPost() => Save(post: true, close: false);
    public IActionResult OnPostPostAndClose() => Save(post: true, close: true);

    private IActionResult Save(bool post, bool close)
    {
        LoadPage();

        var id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id!;
        var existing = _store.GetById(id);
        var lines = BuildLines();
        if (lines.Count == 0)
        {
            Message = "Добавьте хотя бы одну строку.";
            IsError = true;
            EnsureLineRows();
            return Page();
        }

        var number = string.IsNullOrWhiteSpace(Number) || Number.Trim() == "<Авто>"
            ? _store.SuggestNextNumber(Organization)
            : Number.Trim();

        var doc = new PpeAcceptanceDocument
        {
            Id = id,
            Organization = Organization,
            Number = number,
            Date = Date == default ? DateTime.Today : Date.Date,
            Department = (Department ?? string.Empty).Trim(),
            Warehouse = (Warehouse ?? string.Empty).Trim(),
            IsPosted = post,
            Lines = lines,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        _store.Upsert(doc);

        if (close)
        {
            return RedirectToPage("/Ppe/Acceptance", new { printId = id });
        }

        Id = id;
        Document = doc;
        Message = post ? "Проведено." : "Записано.";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostDelete()
    {
        LoadPage();
        if (!string.IsNullOrWhiteSpace(Id))
        {
            _store.Delete(Id);
        }
        return RedirectToPage("/Ppe/Acceptance");
    }

    private void LoadPage()
    {
        Organization = _orgs.GetSelectedOrganization(HttpContext);
        ViewData["ActiveModule"] = "спецодежда-и-сиз";
        ViewData["IsModulePage"] = true;
        ViewData["Title"] = IsNew ? "Приемка спецодежды, СИЗ (создание)" : "Приемка спецодежды, СИЗ";
        ViewData["Subtitle"] = Organization;
        Departments = DepartmentSelect.Merge(
            DepartmentSelect.FromEmployees(_employees, Organization),
            _store.LoadAll().Select(d => d.Department),
            string.IsNullOrWhiteSpace(Department) ? null : [Department]);
    }

    private void EnsureLineRows()
    {
        if (LineNomenclature.Count == 0 &&
            LineEmployeeName.Count == 0 &&
            LineTabNumber.Count == 0)
        {
            LineTabNumber.Add(string.Empty);
            LineEmployeeName.Add(string.Empty);
            LineNomenclature.Add(string.Empty);
            LineQuantity.Add("1");
            LineUnit.Add("шт");
            LinePrice.Add("0");
        }
    }

    private void LoadLines(PpeAcceptanceDocument doc)
    {
        LineTabNumber = [];
        LineEmployeeName = [];
        LineNomenclature = [];
        LineQuantity = [];
        LineUnit = [];
        LinePrice = [];
        foreach (var line in doc.Lines)
        {
            LineTabNumber.Add(line.TabNumber);
            LineEmployeeName.Add(line.EmployeeName);
            LineNomenclature.Add(line.Nomenclature);
            LineQuantity.Add(line.Quantity.ToString("0.###"));
            LineUnit.Add(line.Unit);
            LinePrice.Add(line.Price.ToString("0.##"));
        }
    }

    private List<PpeAcceptanceLine> BuildLines()
    {
        var list = new List<PpeAcceptanceLine>();
        var count = new[]
        {
            LineTabNumber.Count, LineEmployeeName.Count, LineNomenclature.Count,
            LineQuantity.Count, LineUnit.Count, LinePrice.Count
        }.Max();

        var n = 0;
        for (var i = 0; i < count; i++)
        {
            var employee = i < LineEmployeeName.Count ? (LineEmployeeName[i] ?? string.Empty).Trim() : string.Empty;
            var nomenclature = i < LineNomenclature.Count ? (LineNomenclature[i] ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(employee) && string.IsNullOrWhiteSpace(nomenclature))
            {
                continue;
            }

            var tab = i < LineTabNumber.Count ? (LineTabNumber[i] ?? string.Empty).Trim() : string.Empty;
            var unit = i < LineUnit.Count ? (LineUnit[i] ?? string.Empty).Trim() : "шт";
            var qty = i < LineQuantity.Count && decimal.TryParse(LineQuantity[i], out var q) ? q : 1m;
            var price = i < LinePrice.Count && decimal.TryParse(LinePrice[i], out var p) ? p : 0m;
            list.Add(new PpeAcceptanceLine(++n, tab, employee, nomenclature, qty <= 0 ? 1 : qty, unit, price));
        }

        return list;
    }
}


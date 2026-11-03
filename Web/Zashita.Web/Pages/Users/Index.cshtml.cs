using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zashita.Web.Services;

namespace Zashita.Web.Pages.Users;

[RequireHeadAdmin]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class IndexModel : PageModel
{
    private readonly WokUsersService _wokUsers;
    private readonly SiteAccessStore _access;

    public IndexModel(WokUsersService wokUsers, SiteAccessStore access)
    {
        _wokUsers = wokUsers;
        _access = access;
    }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public string NewEmail { get; set; } = string.Empty;

    [BindProperty]
    public string NewName { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    public IReadOnlyList<SiteUserRow> Rows { get; private set; } = [];
    public int TotalCount { get; private set; }
    public string? Message { get; private set; }
    public bool MessageOk { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        ViewData["ActiveModule"] = "users";
        ViewData["Title"] = "Пользователи";
        ViewData["Subtitle"] = null;
        Message = TempData["UsersMessage"] as string;
        MessageOk = TempData["UsersOk"] as bool? == true;
        await BindAsync(ct);
    }

    public async Task<IActionResult> OnPostToggleCrownAsync(string email, string? displayName, CancellationToken ct)
    {
        var has = _access.HasCrown(email);
        _access.SetCrown(email, displayName, !has);
        TempData["UsersMessage"] = !has
            ? $"Доступ разрешён: {email}"
            : $"Доступ закрыт: {email}";
        TempData["UsersOk"] = true;
        return RedirectToPage(new { q = Q });
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        try
        {
            _access.AddLocalUser(NewEmail, NewName, NewPassword);
            TempData["UsersMessage"] = $"Добавлен: {NewEmail.Trim()} (с коронкой — может входить).";
            TempData["UsersOk"] = true;
        }
        catch (Exception ex)
        {
            TempData["UsersMessage"] = ex.Message;
            TempData["UsersOk"] = false;
        }

        return RedirectToPage(new { q = Q });
    }

    public IActionResult OnPostDelete(string email)
    {
        if (_access.RemoveUser(email))
        {
            TempData["UsersMessage"] = $"Удалён / доступ закрыт: {email}";
            TempData["UsersOk"] = true;
        }
        else
        {
            TempData["UsersMessage"] = "Пользователь не найден в списке доступа.";
            TempData["UsersOk"] = false;
        }

        return RedirectToPage(new { q = Q });
    }

    private async Task BindAsync(CancellationToken ct)
    {
        var loaded = await _wokUsers.LoadAsync(ct);
        var access = _access.Load();
        var crowns = new HashSet<string>(
            access.Allowed.Select(a => SiteAccessStore.Normalize(a.Email)),
            StringComparer.Ordinal);

        var map = new Dictionary<string, SiteUserRow>(StringComparer.Ordinal);
        foreach (var u in loaded.Users)
        {
            var key = SiteAccessStore.Normalize(u.Email);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            map[key] = new SiteUserRow
            {
                Email = u.Email.Trim(),
                DisplayName = u.DisplayName,
                Company = u.Company,
                Phone = u.Phone,
                JobTitle = u.JobTitle,
                RoleLabel = u.RoleLabel,
                HasCrown = crowns.Contains(key),
                IsLocal = false
            };
        }

        foreach (var local in access.LocalUsers)
        {
            var key = SiteAccessStore.Normalize(local.Email);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (map.TryGetValue(key, out var existing))
            {
                existing.IsLocal = true;
                existing.HasCrown = crowns.Contains(key);
                if (!string.IsNullOrWhiteSpace(local.DisplayName))
                {
                    existing.DisplayName = local.DisplayName;
                }
            }
            else
            {
                map[key] = new SiteUserRow
                {
                    Email = local.Email,
                    DisplayName = string.IsNullOrWhiteSpace(local.DisplayName) ? local.Email : local.DisplayName,
                    HasCrown = crowns.Contains(key),
                    IsLocal = true,
                    RoleLabel = "Локальный"
                };
            }
        }

        IEnumerable<SiteUserRow> query = map.Values
            .OrderByDescending(r => r.HasCrown)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            query = query.Where(u =>
                Contains(u.DisplayName, q) ||
                Contains(u.Email, q) ||
                Contains(u.Company, q) ||
                Contains(u.Phone, q) ||
                Contains(u.JobTitle, q));
        }

        Rows = query.ToList();
        TotalCount = Rows.Count;
    }

    private static bool Contains(string? value, string q) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(q, StringComparison.OrdinalIgnoreCase);
}

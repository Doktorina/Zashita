namespace Zashita.Web.Services.Connections;

/// <summary>
/// Выгрузка 1С ЗУП только с сервера: папка SharedPath (по умолчанию /srv/share).
/// Локальный inbox, UNC и сетевые шары не используются.
/// </summary>
public sealed class OneCOptions
{
    public const string SectionName = "OneC";

    /// <summary>false — без автосинка и без обращения к SharedPath (демо/локально).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Серверный путь к выгрузке 1С. По умолчанию /srv/share.</summary>
    public string SharedPath { get; set; } = "/srv/share";
}

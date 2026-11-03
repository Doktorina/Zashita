namespace Zashita.Core.Incidents;

public sealed record IncidentAssistantEntry(
    string Id,
    string Title,
    string Description,
    string? RouteUrl);

public static class IncidentsAssistantsCatalog
{
    public static IReadOnlyList<IncidentAssistantEntry> Entries { get; } =
    [
        new(
            "accident-register",
            "Регистрация несчастного случая на производстве",
            "Создание карточки несчастного случая и переход к расследованию.",
            "/Incidents/Workplace"),
        new(
            "investigation",
            "Помощник расследования несчастного случая",
            "Список расследований, печать актов формы Н-1 и связанных документов.",
            "/Incidents/Investigations"),
        new(
            "microtrauma",
            "Учёт и расследование микротравм",
            "Регистрация микротравм и ведение расследований.",
            "/Incidents/Microtraumas"),
        new(
            "catalogs",
            "Справочники по несчастным случаям",
            "Виды происшествий, травм, причин и другие классификаторы.",
            "/Incidents/Catalog?kind=IncidentTypes")
    ];
}

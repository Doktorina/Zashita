using Zashita.Core.Navigation;

namespace Zashita.Web.Services;

public sealed record NavModuleItem(
    string Slug,
    string Title,
    string ShortTitle,
    string Subtitle,
    ModulePageDefinition? Definition,
    bool IsHome = false);

public static class ModuleUiCatalog
{
  public static readonly ModulePageDefinition MedExams = new(
      "Медосмотры",
      [],
      [
          new ModuleSection("Все функции",
          [
              "Отчет по медосмотрам",
              "Списки контингентов",
              "Ограничения физ. лиц",
              "Планирование медосмотров",
              "Календарь медосмотров",
              "Формирование направления на медосмотр"
          ])
      ],
      [
          new ModuleSection("Справочники",
          [
              "Перечень факторов и видов работ"
          ]),
          new ModuleSection("Настройки",
          [
              "Стандарты",
              "Медицинские организации"
          ])
      ]);

  private static readonly Dictionary<string, string> ShortTitles = new(StringComparer.OrdinalIgnoreCase)
  {
      ["Система управления охраной труда"] = "Система управления",
      ["Специальная оценка"] = "Специальная оценка",
      ["Риски"] = "Риски",
      ["Медосмотры"] = "Медосмотры",
      ["Спецодежда и СИЗ"] = "Спецодежда и СИЗ",
      ["Обучение"] = "Обучение",
      ["Несчастные случаи"] = "Несчастные случаи",
      ["Бюджет"] = "Бюджет"
  };

  private static readonly Dictionary<string, string> Subtitles = new(StringComparer.OrdinalIgnoreCase)
  {
      ["Главная"] = "Сводка по задачам, срокам и состоянию охраны труда.",
      ["Система управления охраной труда"] = "Базовые процессы, политика, контроль исполнения требований.",
      ["Специальная оценка"] = "Карты СОУТ, факторы, рабочие места, результаты оценки.",
      ["Риски"] = "Идентификация опасностей, оценка и мероприятия по снижению рисков.",
      ["Медосмотры"] = "Предварительные, периодические, предсменные и профзаболевания.",
      ["Спецодежда и СИЗ"] = "Выдача, нормы, остатки и контроль сроков эксплуатации.",
      ["Обучение"] = "Планирование обучения и проверок знаний по охране труда.",
      ["Несчастные случаи"] = "Регистрация, расследование и анализ причин.",
      ["Бюджет"] = "Смета расходов и контроль затрат на охрану труда."
  };

  public static IReadOnlyList<NavModuleItem> Sidebar { get; } = BuildSidebar();

  public static string DefaultOrganization => "ИП Алешкевич А.Д.";

  public static string GetShortTitle(string moduleName) =>
      ShortTitles.TryGetValue(moduleName, out var shortTitle) ? shortTitle : moduleName;

  public static string GetSubtitle(string? moduleName) =>
      moduleName is not null && Subtitles.TryGetValue(moduleName, out var subtitle)
          ? subtitle
          : string.Empty;

  public static ModulePageDefinition? ResolveModule(string slug)
  {
      if (slug.Equals(ModuleSlug.ToSlug(MedExams.ModuleName), StringComparison.OrdinalIgnoreCase))
      {
          return MedExams;
      }

      return ModuleSlug.FindModule(slug);
  }

  public static NavModuleItem? FindNavItem(string slug) =>
      Sidebar.FirstOrDefault(item => item.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

  private static IReadOnlyList<NavModuleItem> BuildSidebar()
  {
      var items = new List<NavModuleItem>
      {
          new("home", "Главная", "Главная", Subtitles["Главная"], null, true)
      };

      var ordered = new[]
      {
          "Система управления охраной труда",
          "Специальная оценка",
          "Риски",
          "Медосмотры",
          "Спецодежда и СИЗ",
          "Обучение",
          "Несчастные случаи"
      };

      foreach (var title in ordered)
      {
          ModulePageDefinition? definition;
          if (title == "Медосмотры")
          {
              definition = MedExams;
          }
          else
          {
              definition = ModuleCatalog.All.FirstOrDefault(m =>
                  m.ModuleName.Equals(title, StringComparison.OrdinalIgnoreCase));
          }

          if (definition is null)
          {
              continue;
          }

          var slug = ModuleSlug.ToSlug(definition.ModuleName);
          items.Add(new NavModuleItem(
              slug,
              definition.ModuleName,
              GetShortTitle(definition.ModuleName),
              GetSubtitle(definition.ModuleName),
              definition));
      }

      return items;
  }
}

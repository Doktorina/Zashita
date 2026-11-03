namespace Zashita.Core.Navigation;



public static partial class ModuleMenuRegistry
{
    public static readonly ModulePageDefinition OccupationalSafetyManagement = new(
        "Система управления охраной труда",
        [],
        [
            new ModuleSection("Цели",
            [
                "Показатели",
                "Отражение факта по показателям"
            ])
        ],
        [
            new ModuleSection("См. также",
            [
                "Сотрудники"
            ])
        ]);

    public static readonly ModulePageDefinition SpecialAssessment = new(
        "Специальная оценка",
        [
            "Карты СОУТ",
            "Отчет по СОУТ",
            "Шаблоны для печати",
            "Комиссии"
        ],
        [
            new ModuleSection("Все функции",
            [
                "Карты СОУТ",
                "Отчет по СОУТ",
                "Комиссии",
                "Шаблоны для печати",
                "Загрузка результатов",
                "Годовой план по специальной оценке"
            ])
        ],
        [
            new ModuleSection("Справочники",
            [
                "Перечень рабочих мест",
                "Материалы и сырье",
                "Оборудование",
                "Классы условий труда"
            ])
        ]);

    public static readonly ModulePageDefinition ElectricalSafety = new(
        "Электробезопасность",
        [],
        [
            new ModuleSection("Реестр электробезопасность",
            [
                "Проверка знаний ЭБ",
                "Комиссия по ЭБ"
            ]),
            new ModuleSection("Предписания и проверки",
            [
                "Акт проверки",
                "План проверок",
                "Предписания"
            ])
        ],
        [
            new ModuleSection("Справочники",
            [
                "Виды электрооборудования",
                "Напряжения энергопринимающей установки",
                "Узлы оборудования",
                "Состояния объектов",
                "Виды обучения",
                "Нарушения",
                "Виды мероприятий",
                "Направления деятельности",
                "Требования",
                "Программы проверок"
            ]),
            new ModuleSection("Настройки",
            [
                "Ещё больше возможностей"
            ])
        ]);

    public static readonly ModulePageDefinition Budget = new(
        "Бюджет",
        ["Отчеты по бюджету", "Помощники по бюджету"],
        [
            new ModuleSection("Бюджет",
            [
                "Экземпляр бюджета",
                "Обороты по документам",
                "Примечания к документам"
            ]),
            new ModuleSection("Документы для отражения плана",
            [
                "Внутренние заказы",
                "Планирование обучения, инструктажей, проверок знаний, стажировок",
                "Подготовка проведения специальной оценки",
                "Предписания",
                "Расчеты стоимости медосмотров",
                "Формирование поименного списка лиц"
            ]),
            new ModuleSection("Документы для отражения факта",
            [
                "Все задачи",
                "Авансовый отчет",
                "Выдача ЛПП и компенсаций сотрудникам",
                "Мероприятие",
                "Обучение, инструктажи, проверки знаний, стажировки",
                "Поступления товаров",
                "Проведение медосмотра",
                "Реализация услуг",
                "Результаты специальной оценки"
            ])
        ],
        [
            new ModuleSection("Справочники",
            [
                "Сценарии",
                "Статьи бюджета",
                "Статьи затрат",
                "Показатели",
                "Ответственные по бюджету"
            ]),
            new ModuleSection("Настройки",
            [
                "Отображение смет в документах",
                "Ещё больше возможностей"
            ]),
            new ModuleSection("См. также",
            [
                "ЦФО",
                "Валюты"
            ])
        ]);

    public static IReadOnlyList<ModulePageDefinition> All { get; } =
    [
        OccupationalSafetyManagement,
        Risks,
        SpecialAssessment,
        Training,
        Incidents,
        Ppe,
        ElectricalSafety,
        Budget
    ];

    public static IEnumerable<string> EnumerateAllItemTitles(ModulePageDefinition definition)
    {
        foreach (var top in definition.TopLinks)
        {
            yield return top;
        }

        foreach (var section in definition.LeftSections.Concat(definition.RightSections))
        {
            foreach (var item in section.Items)
            {
                yield return item;
            }
        }
    }
}

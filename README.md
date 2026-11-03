<p align="center">
  <img src="Web/Zashita.Web/wwwroot/brand/site-icon.svg" alt="Zashita" width="72" height="72" />
</p>

<h1 align="center">Zashita</h1>

<p align="center">
  <strong>Веб-система охраны труда</strong> для строительных и производственных организаций:<br/>
  единый штат, обучение, медосмотры, СИЗ, СОУТ и электробезопасность.
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
  <img alt="ASP.NET Core" src="https://img.shields.io/badge/ASP.NET%20Core-Razor%20Pages-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
  <img alt="C#" src="https://img.shields.io/badge/C%23-13-239120?style=for-the-badge&logo=csharp&logoColor=white" />
  <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white" />
  <img alt="Demo data" src="https://img.shields.io/badge/Data-Synthetic%20demo-2ea44f?style=for-the-badge" />
  <img alt="License" src="https://img.shields.io/badge/License-Private%20demo-lightgrey?style=for-the-badge" />
</p>

---

## Зачем это

Типичная OT-система разъезжается по Excel, 1С и бумажным журналам. **Zashita** собирает рабочие процессы в одном веб-интерфейсе: штат из выгрузки 1С → обучение / МО / СИЗ / СОУТ / ЭБ с одинаковым списком людей по организации.

В этом репозитории — **портфолио-срез** с вымышленным штатом. Боевые ФИО, ИНН, пароли и скрипты выкладки **не включены**.

---

## Плюсы

| | |
|:--|:--|
| **Единый штат** | Люди берутся из карточки организации (`organization-employees`). Тулбар, журналы и отчёты считают одно и то же. |
| **Человек × ЮЛ** | Один сотрудник может работать в нескольких компаниях — отдельная карточка на каждое юрлицо, без склейки КСК / ИП-однофамильцев. |
| **Синк с 1С** | CSV-выгрузки (zup + anzhela): приём, увольнение, переводы; жёсткая перезапись штата с бэкапом. |
| **Обучение end-to-end** | Виды программ, стандарты, статус «светофор», заявки, журналы инструктажей с печатью/экспортом. |
| **Медосмотры** | Контингент, направления, график, заключения, ограничения — на том же штате. |
| **СИЗ** | Номенклатура, нормы, карты СИОС, выдача / списание. |
| **СОУТ и риски** | Рабочие места, карты, статусы, импорт документов. |
| **Электробезопасность** | Реестр проверок знаний, комиссии, I группа в журналах. |
| **Печать и шаблоны** | Word/Excel-шаблоны в `Templates/`, предпросмотр и история печати. |
| **Модульная архитектура** | UI в `Zashita.Web`, домен в `Zashita.Core` — проще сопровождать и тестировать. |

---

## Модули

```text
┌─────────────────────────────────────────────────────────────┐
│                        Zashita Web                          │
├──────────┬──────────┬──────────┬──────────┬─────────────────┤
│  Штат /  │ Обучение │   Мед.   │   СИЗ    │ СОУТ / Риски /  │
│Организации│ журналы  │осмотры   │          │      ЭБ         │
└────┬─────┴────┬─────┴────┬─────┴────┬─────┴────────┬────────┘
     │          │          │          │              │
     └──────────┴──────────┴──────────┴──────────────┘
                    Zashita.Core  ·  App_Data
```

- **Организации** — список ЮЛ, сотрудники, реквизиты, группы компаний  
- **Обучение** — планирование, статус, заявки, журналы вводных / на РМ / ПБ / ЭБ  
- **Медосмотры** — списки, направления, график, сканы заключений  
- **СИЗ** — нормы, номенклатура, карты учёта  
- **СОУТ / риски** — РМ, карты, отчёты  
- **Электробезопасность** — реестр и комиссии  

---

## Стек

<p>
  <img alt="ASP.NET" src="https://img.shields.io/badge/ASP.NET%20Core-Razor%20Pages-512BD4?logo=dotnet&logoColor=white" />
  <img alt="C#" src="https://img.shields.io/badge/Language-C%23-239120?logo=csharp&logoColor=white" />
  <img alt="JSON store" src="https://img.shields.io/badge/Storage-JSON%20App__Data-informational" />
  <img alt="Excel" src="https://img.shields.io/badge/Import-Excel%20%2F%20CSV%201C-0F9D58" />
  <img alt="Docs" src="https://img.shields.io/badge/Export-Word%20%2F%20Excel-2B579A" />
</p>

- **.NET 10** / C#  
- **ASP.NET Core** Razor Pages  
- Файловое хранилище `App_Data` (JSON) — удобно для демо и локального запуска  
- Шаблоны печати Word/Excel  
- Опциональная интеграция с внешней БД пользователей (отключена в демо)

---

## Быстрый старт

```powershell
cd Zashita-Portfolio
.\start-web.ps1
```

| | |
|:--|:--|
| URL | http://localhost:5298 |
| Логин | `admin` |
| Пароль | `123` |

Нужен [.NET SDK](https://dotnet.microsoft.com/download) под `net10.0`.

Открыть решение: `Zashita.slnx`.

---

## Структура репозитория

```text
Zashita-Portfolio/
├── README.md
├── start-web.ps1              # локальный запуск
├── Zashita.slnx               # решение
└── Web/
    ├── Zashita.Web/           # UI, Pages, wwwroot, App_Data (демо)
    └── Zashita.Core/          # домен: штат, обучение, мед, СИЗ, СОУТ…
```

### Демо-штат

| Файл | Содержание |
|------|------------|
| `organizations.json` | ООО «Демо Строй», ООО «Северные Технологии», ИП Пример Иван Иванович |
| `organization-employees.json` | Несколько вымышленных сотрудников |
| `organization-requisites.json` | Заглушки реквизитов |

---

## Что сознательно не в репозитории

- Боевые выгрузки 1С и персональные данные  
- Скрипты деплоя и доступы к серверам  
- Production-секреты (`appsettings.Production.json`)  

---

<p align="center">
  <sub>Portfolio demo · synthetic data only</sub>
</p>

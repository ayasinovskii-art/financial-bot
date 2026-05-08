# FinanceBot — Техническое задание

**Версия:** 1.0  
**Стек:** .NET 10 LTS, C# 14  
**Архитектурная парадигма:** Event-Driven Design, Actor Model (Akka.NET), DDD с богатой доменной моделью, CQRS на уровне persistence.

---

## Содержание

1. [Назначение системы](#1-назначение-системы)
2. [Технологический стек](#2-технологический-стек)
3. [Функциональные требования](#3-функциональные-требования)
4. [Нефункциональные требования](#4-нефункциональные-требования)
5. [Архитектура](#5-архитектура)
6. [Доменная модель](#6-доменная-модель)
7. [Интеграции](#7-интеграции)
8. [Конфигурация](#8-конфигурация)
9. [Структура решения](#9-структура-решения)
10. [Этапы разработки](#10-этапы-разработки)
11. [Тестирование](#11-тестирование)
12. [Deployment и эксплуатация](#12-deployment-и-эксплуатация)
13. [Конвенции и стандарты](#13-конвенции-и-стандарты)

---

## 1. Назначение системы

Telegram-бот для отслеживания личных финансов с автоматической категоризацией, бюджетированием по правилу 50/25/25, AI-консультациями через Anthropic Claude, визуализацией через графики и поддержкой нескольких пользователей под whitelist-контролем.

**Ключевые особенности:**

- Цикл бюджета привязан к датам зарплаты (по умолчанию 10 и 25 числа), а не к календарному месяцу.
- Автоматический сдвиг дней зарплаты при попадании на выходные/праздники.
- Регулярные траты (обед каждый рабочий день, подписки) и запланированные разовые траты учитываются в прогнозе бюджета.
- Категоризация через цепочку: память пользователя → локальные правила → Claude → fallback `Other`.
- При недоступности Claude — три разные стратегии fallback в зависимости от контекста использования.
- При простое бота — wakeup-уведомление с указанием пропущенных событий и командами для backfill.

---

## 2. Технологический стек

### 2.1 Основной стек

| Категория | Выбор | Версия |
|---|---|---|
| Runtime | .NET | 10 LTS |
| Язык | C# | 14 |
| Hosting | Microsoft.Extensions.Hosting | 10.x |
| Actor framework | Akka.NET | последняя стабильная |
| Akka integration | Akka.Hosting | последняя стабильная |
| Persistence | Akka.Persistence.PostgreSql | последняя стабильная |
| Cluster | Akka.Cluster + Akka.Cluster.Sharding | последняя стабильная |
| Cluster singletons | Akka.Cluster.Tools | последняя стабильная |
| Discovery | Akka.Discovery + Akka.Discovery.KubernetesApi | последняя стабильная |
| Read-model | Microsoft.EntityFrameworkCore | 10.x |
| EF Core provider | Npgsql.EntityFrameworkCore.PostgreSQL | последняя стабильная |
| Database | PostgreSQL | 17 |
| Resilience | Polly | v8 |
| Telegram | Telegram.Bot | последняя стабильная |
| AI | Anthropic.SDK (или официальный SDK) | последняя стабильная |
| Charts | ScottPlot | 5.x |
| Logging | Microsoft.Extensions.Logging (default console) | 10.x |
| Tests (unit) | xUnit | последняя стабильная |
| Tests (actors) | Akka.TestKit | последняя стабильная |
| Tests (integration) | TestContainers | последняя стабильная |
| Containerization | Docker + docker-compose | актуальная |

### 2.2 Что **не** используется

- **Serilog** — избыточно для текущего масштаба, используется default `ILogger`.
- **OpenTelemetry** — добавим точечно, если появится потребность в метриках.
- **Quartz.NET** — заменён собственным `SchedulerActor` поверх Akka Scheduler.
- **MediatR** — оркестрация через акторы, command bus не нужен.
- **AutoMapper** — мапперы пишутся руками для прозрачности.

---

## 3. Функциональные требования

### 3.1 Регистрация и контроль доступа

- Whitelist-модель. Не-whitelisted пользователи не могут пользоваться ботом.
- В конфиге `Auth:AdminUserIds` — список Telegram ID администраторов. Не может быть пустым.
- Администраторы всегда в whitelist неявно (не зависит от записей в БД).
- Не-админы попадают в whitelist только через команду `/adduser` от администратора.
- При получении любого сообщения от не-whitelisted пользователя бот отвечает:  
  «Доступ ограничен. Попроси админа добавить твой ID: `<его_telegram_id>`».
- При первом успешном `/start` пользователь регистрируется с дефолтными настройками.

### 3.2 Доходы

- Пользователь сообщает доход через команду `/income` или отвечая на вопрос бота в день зарплаты.
- В дни зарплаты (по умолчанию 10 и 25) бот вечером шлёт вопрос «Какая сумма зарплаты сегодня?».
- Если день зарплаты приходится на выходной/праздник — вопрос задаётся в **предыдущий рабочий день** вечером (правило настраиваемое: `previous` / `next` / `none`).
- Период бюджета — **salary cycle** (от поступления до поступления). По умолчанию.
- Альтернативные типы периода (настройка `period_type`): `calendar-month`.
- При получении дохода открывается новый период (если не открыт), или пополняется текущий.

### 3.3 Расходы

- Несколько способов фиксации:
  - Свободный текст в любое время: «обед 750», «такси 400 + кофе 200».
  - Через команду `/expense` с явными аргументами.
  - В ответ на вечерний опрос (с поддержкой регулярных трат).
- Каждая трата — отдельное событие `ExpenseReported`. Множественные траты в одном сообщении парсятся в N событий.
- Описание сохраняется как есть, нормализованная форма (lowercase + trim) используется для memory-категоризации.

### 3.4 Категоризация

Цепочка из четырёх шагов:

1. **Memory** — точное совпадение по `normalize(description)` в истории корректировок пользователя.
2. **Local rules** — keyword/regex match по описанию (правила в JSON-файле в проекте).
3. **Claude** — через `ClaudeConsultantActor` cluster singleton.
4. **Fallback** — категория `Other` с флагом `NeedsManualReview = true`.

Категории фиксированы:

```
Groceries, DiningOut, Transport, Utilities, Subscriptions,
Entertainment, Health, Clothing, Personal, Education,
Gifts, Travel, Other
```

Маппинг категорий на бакеты (default, переопределяется в settings):

```
Essentials (50%): Groceries, Transport, Utilities, Health, Education
Fun (25%):        DiningOut, Subscriptions, Entertainment, Personal, Clothing, Gifts, Travel
Deposit (25%):    (траты сюда не идут)
Other:            попадает в Essentials по умолчанию
```

При коррекции категории через `/correct`:
- Сохраняется в memory: `normalize(description) → newCat`.
- **Историю не трогаем** — старые расходы остаются с прежней категорией. Новые с тем же описанием будут использовать память.

При успешном ответе Claude — категория сохраняется в memory автоматически (без явного подтверждения), чтобы не дёргать API повторно.

### 3.5 Бюджет (50/25/25)

- При первом доходе в периоде — вычисляется аллокация бакетов:
  - `allocation_essentials = totalIncome * 0.50`
  - `allocation_fun = totalIncome * 0.25`
  - `allocation_deposit = totalIncome * 0.25`
- При последующих доходах в том же периоде — аллокация **пересчитывается** (увеличивается).
- Проценты настраиваются через `/settings allocation 50/25/25`.
- Если потрачено больше allocation на бакет — фиксируем перерасход, не блокируем, не переносим автоматически.
- Бакеты можно переопределить через `/settings bucket_mapping`.

### 3.6 Накопления

- **Виртуальный учёт по умолчанию.** `savings_actual = NULL` означает «считаем равным `allocation_deposit`».
- Опционально пользователь подтверждает фактический перевод через `/savings <сумма>`.
- При закрытии периода — фиксируется итоговая `savings_actual`.

### 3.7 Регулярные траты (Recurring Templates)

- Пользователь создаёт шаблон через `/template add <name> <amount> <schedule> [category]`.
- Schedule:
  - `weekdays` — каждый рабочий день (Mon–Fri за вычетом праздников по `IWorkdayCalendar`).
  - `daily` — каждый день.
  - `dow:1,3,5` — по дням недели (1=Mon).
  - `dom:1,15` — по числам месяца.
- Сумма точечная, с флагом «примерно» (используется для предсказания, не для аномалий).
- При вечернем опросе бот перечисляет ожидаемые сегодня шаблоны.
- При молчании пользователя через `silence_deadline_hours` — авто-фиксация регулярных по типичной сумме (если включено `auto_confirm_on_silence`).

### 3.8 Запланированные траты (Planned Expenses)

- Создаются через `/plan add <amount> <date> <description>`.
- Учитываются в прогнозе остатков бакетов.
- В день фактической траты — бот напоминает: «Сегодня запланировано: X. Подтвердить?».
- Подтверждение → создаётся `ExpenseReported`, `PlannedExpense` помечается как `Confirmed`.
- Корректировка суммы при подтверждении возможна.

### 3.9 Советы и консультации

#### По расписанию

- **Weekly advisor tick** — утром первого рабочего дня недели, 09:00 в TZ пользователя.
- **Monthly advisor tick** — утром первого рабочего дня месяца, 09:00 в TZ пользователя.

#### По запросу

- `/advice [week|month]` — запрашивает совет на основе текущих данных.

#### Логика

- `AdvisorActor` собирает снапшот данных пользователя.
- Передаёт через `ClaudeConsultantActor` для генерации.
- Если Claude недоступен:
  - Запланированные советы (по тику) — **park-and-refresh**: ждём восстановления, затем пересобираем снапшот заново и шлём.
  - On-demand `/advice` — **immediate local fallback**: heuristics, без Claude.

### 3.10 Графики

Минимальный набор:

1. **Pie chart по категориям** за текущий период — после вечернего опроса автоматически.
2. **Bar chart дневных трат** за последние 30 дней — `/chart daily`.
3. **Stacked bar утилизация бакетов** — `/chart buckets` и в monthly summary.
4. **Line chart прогресса накоплений** — `/chart savings`, monthly tick.

Рендер через ScottPlot 5, выход — PNG, отправка в Telegram как файл.

### 3.11 Расписание и тики

| Тик | Когда | Кому |
|---|---|---|
| `EveningTick` | Каждый день в `evening_time` (default 19:00) в TZ юзера | Per-user |
| `SilenceDeadlineTick` | Через `silence_deadline_hours` (default 4) после `EveningTick`, если юзер не ответил | Per-user |
| `SalaryDayTick` | В `salary_days` со сдвигом по `shift_rule`, в `evening_time` | Per-user |
| `WeeklyAdvisorTick` | Первый рабочий день недели, 09:00 в TZ юзера | Per-user |
| `MonthlyAdvisorTick` | Первый рабочий день месяца, 09:00 в TZ юзера | Per-user |
| `ClaudeAutoRecoveryTick` | Каждый день в 20:00 серверного времени | System |
| `SystemHeartbeat` | Каждую минуту | System |

Все per-user тики используют таймзону пользователя из `users.timezone` (default — серверная).

### 3.12 Wakeup-уведомление

- При старте `SchedulerActor` проверяет последний `SystemHeartbeat`.
- Если `(now − lastHeartbeat) > 5 минут` — простой обнаружен.
- Для каждого whitelisted пользователя вычисляются пропущенные события в окне простоя:
  - Пропущенные `EveningTick`-и (по дням).
  - Пропущенные `SalaryDayTick`-и.
  - Пропущенные `WeeklyAdvisorTick` / `MonthlyAdvisorTick`.
- Шлётся персональное сообщение с перечислением пропущенного и инструкциями по backfill.
- Если пропущен advisor-тик — отдельно упоминается возможность запустить через `/advice`.
- Уведомление шлётся **один раз на простой** (повторные рестарты в течение того же простоя не дублируют).

### 3.13 Команды Telegram

#### Общие

| Команда | Синтаксис | Описание |
|---|---|---|
| `/start` | `/start` | Регистрация / приветствие |
| `/help` | `/help` | Список команд с краткими описаниями |
| `/whoami` | `/whoami` | Показать свой `telegram_id` |
| `/cancel` | `/cancel` | Отменить текущий диалог (выйти из FSM в Idle) |

#### Доходы и расходы

| Команда | Синтаксис | Описание |
|---|---|---|
| `/income` | `/income [<date>] <amount> [<description>]` | Записать доход. Без даты — сегодня. |
| `/expense` | `/expense [<date>] <amount> <description>` | Записать конкретную трату с описанием. |
| `/expense_day` | `/expense_day [<date>] <amount>` | Итог дня без разбивки (категория `Other`). |
| `/correct` | `/correct` | Открыть список последних трат для исправления категории. |

Свободный текст без команды интерпретируется как трата сегодняшним числом, если матчится паттерн `<число>[\s+<описание>]` или серия таких через `+` / `,`.

#### Регулярные и запланированные

| Команда | Синтаксис | Описание |
|---|---|---|
| `/template add` | `/template add <name> <amount> <schedule> [<category>]` | Создать шаблон |
| `/template list` | `/template list` | Список шаблонов |
| `/template remove` | `/template remove <name>` | Удалить шаблон |
| `/plan add` | `/plan add <amount> <date> <description>` | Запланировать трату |
| `/plan list` | `/plan list` | Список запланированных |
| `/plan remove` | `/plan remove <id>` | Удалить запланированную |

#### Советы, графики, отчёты

| Команда | Синтаксис | Описание |
|---|---|---|
| `/advice` | `/advice [week\|month]` | Получить совет от Claude (или локальный fallback) |
| `/chart` | `/chart <category\|daily\|buckets\|savings>` | Запросить график |
| `/report` | `/report [period]` | Текстовый отчёт по периоду (current/previous/N) |
| `/savings` | `/savings <amount>` | Подтвердить фактический перевод на накопления |

#### Настройки

| Команда | Синтаксис | Описание |
|---|---|---|
| `/settings` | `/settings` | Показать все текущие настройки |
| `/settings <key> <value>` | например `/settings timezone Europe/Moscow` | Изменить ключ |
| `/settings reset` | `/settings reset [<key>]` | Сбросить все или один ключ |

Список ключей и значений — см. раздел 8.

#### Админ-команды (только для `Auth:AdminUserIds`)

| Команда | Синтаксис | Описание |
|---|---|---|
| `/adduser` | `/adduser <telegram_id>` | Добавить в whitelist |
| `/removeuser` | `/removeuser <telegram_id>` | Удалить из whitelist |
| `/listusers` | `/listusers` | Список whitelisted с метаданными |

---

## 4. Нефункциональные требования

### 4.1 Производительность и масштабируемость

- Готовность к multi-node deployment **с первого дня** (через Akka.Cluster + Sharding).
- На MVP — single-node кластер, переход на N-node без изменений кода (только конфиг).
- Поддержка минимум **1000 одновременно зарегистрированных пользователей** на узел при типичной нагрузке.
- Latency Telegram-ответа — < 1 сек на 95-м перцентиле для не-Claude команд, < 5 сек для Claude.

### 4.2 Надёжность

- Persistent акторы переживают рестарты процесса без потери состояния.
- Cluster singleton автоматически респавнится на другом ноде при падении (до 5 сек failover).
- Circuit Breaker для Claude предотвращает каскадные сбои.
- Wakeup-уведомление компенсирует потерянные тики.

### 4.3 Безопасность

- Whitelist на входе.
- Секреты только через переменные окружения (Telegram bot token, Anthropic API key, Postgres password).
- Никаких секретов в `appsettings.json` или коде.
- Логи не содержат полных описаний трат с PII (можно нормализованную форму или хеш).

### 4.4 Сопровождаемость

- Domain-Driven Design: чётко выделенный Domain слой без зависимостей от инфраструктуры.
- Event Sourcing — историю можно реплеить, проекции пересобираются.
- Все настройки — в конфиге, никаких magic constants в коде.

### 4.5 Наблюдаемость

- Структурированные логи через `ILogger<T>` с категориями.
- Metrics-friendly: события CB, latency Claude — логируются в стандартном формате.
- Health checks: `/health` endpoint в production-режиме (Akka cluster status, Postgres connectivity).

---

## 5. Архитектура

### 5.1 Слои и проекты

```
FinanceBot.Domain                — чистая доменная модель
FinanceBot.Application           — акторы, проекции, use-cases
FinanceBot.Infrastructure        — EF Core, внешние API, ScottPlot
FinanceBot.Host                  — composition root, Program.cs, конфиги
```

**Правила зависимостей:**

- `Domain` ни на что не ссылается (только на BCL).
- `Application` ссылается на `Domain`.
- `Infrastructure` ссылается на `Domain` (для интерфейсов сервисов).
- `Host` ссылается на всё, собирает граф зависимостей через DI.

### 5.2 Карта акторов

```
Cluster
│
├── ClusterSingletons (один экземпляр на кластер)
│   ├── ClaudeConsultantActor          — обёртка над Claude API + token-aware unavailability
│   ├── SchedulerActor                  — все тики (per-user и system)
│   ├── AccessControlActor              — whitelist глобальный
│   ├── UsersListProjection             — проекция в app.users
│   ├── PeriodProjection                — проекция в app.periods
│   ├── ExpenseProjection               — проекция в app.expenses
│   ├── IncomeProjection                — проекция в app.incomes
│   └── WhitelistProjection             — проекция в app.whitelist
│
├── ShardRegions (распределены по нодам)
│   ├── User                            → UserActor (per userId)
│   ├── UserTemplates                   → UserTemplatesActor (per userId)
│   └── UserPlannedExpenses             → UserPlannedExpensesActor (per userId)
│
└── Per-node services (на каждом ноде)
    ├── TelegramGatewayActor            — webhook receiver / polling singleton
    ├── CategorizerActor                — stateless (rules + delegate to Claude)
    ├── ChartRendererPool               — router + N workers
    └── AdvisorActor                    — stateless (heuristics + delegate to Claude)
```

### 5.3 Persistent акторы

Каждый persistent актор имеет уникальный `PersistenceId`:

| Actor | PersistenceId pattern |
|---|---|
| UserActor | `user-{userId}` |
| UserTemplatesActor | `user-templates-{userId}` |
| UserPlannedExpensesActor | `user-planned-{userId}` |
| AccessControlActor | `access-control` (синглтон) |

**Snapshot policy:** каждые **100 событий**, хранятся последние **3 снапшота**.

### 5.4 Schema БД

#### Schema `akka` (управляется Akka.Persistence.PostgreSql)

- `event_journal` — журнал доменных событий.
- `snapshot_store` — снапшоты.
- `metadata` — метаданные.

#### Schema `app` (управляется EF Core 10 migrations)

```
app.users
  user_id              uuid pk
  telegram_id          bigint unique
  timezone             varchar
  settings_json        jsonb
  registered_at        timestamptz
  last_updated         timestamptz

app.periods
  period_id            uuid pk
  user_id              uuid fk → users
  start_date           date
  end_date             date null         -- null = активный
  status               varchar           -- 'active' | 'closed'
  total_income         numeric(14,2)
  allocation_essentials numeric(14,2)
  allocation_fun       numeric(14,2)
  allocation_deposit   numeric(14,2)
  savings_actual       numeric(14,2) null

app.expenses
  expense_id           uuid pk
  user_id              uuid fk
  period_id            uuid fk
  occurred_at          timestamptz
  amount               numeric(14,2)
  description          varchar(500)
  category             varchar(32)
  bucket               varchar(32)
  source               varchar(32)       -- 'memory' | 'rules' | 'claude' | 'manual' | 'recurring-auto' | 'planned-confirmed'
  needs_review         bool
  auto_confirmed       bool
  template_id          uuid null
  planned_id           uuid null
  created_at           timestamptz

app.incomes
  income_id            uuid pk
  user_id              uuid fk
  period_id            uuid fk
  occurred_at          timestamptz
  amount               numeric(14,2)
  description          varchar(500) null
  created_at           timestamptz

app.whitelist
  telegram_id          bigint pk
  added_by             bigint
  added_at             timestamptz
  revoked_at           timestamptz null

app.projection_offsets
  projection_name      varchar(64) pk
  offset_value         bigint
  last_updated         timestamptz

app.system_heartbeat
  id                   int pk default 1   -- single row
  last_seen            timestamptz
```

**Индексы:**

- `app.expenses (user_id, period_id)`
- `app.expenses (user_id, occurred_at)`
- `app.incomes (user_id, period_id)`
- `app.periods (user_id, status)`

### 5.5 Шина событий и проекции

- Akka `EventStream` локален каждому ноду — для cross-node реакций не используется.
- Проекции читают из общего `Akka.Persistence.Query.ReadJournal` через `EventsByTag`.
- Каждая проекция хранит свой offset в `app.projection_offsets`.
- Проекции запускаются как `ClusterSingleton` — гарантия одной записи в read-модель.

### 5.6 Tagging событий

Через `IEventAdapter` Akka. Базовый адаптер навешивает теги:

- `user-{userId}` — для всех событий пользователя.
- `expense` — для `ExpenseReported`, `ExpenseCategorizedAutomatically`, `ExpenseCategoryCorrected`, `ExpenseDeleted`.
- `income` — для `IncomeReported`.
- `period` — для `BudgetPeriodStarted`, `BudgetAllocated`, `BudgetPeriodClosed`.
- `category` — для всех category-related событий.
- `whitelist` — для `UserWhitelisted`, `UserRevoked`.
- `settings` — для `UserSettingsUpdated`.

Проекции подписываются на нужные теги.

---

## 6. Доменная модель

### 6.1 Value Objects

```
Money              { Amount: decimal, Currency: string = "RUB" }
Category           — enum или sealed class с фиксированным списком (см. 3.4)
Bucket             — enum: Essentials | Fun | Deposit | None
ShiftRule          — enum: Previous | Next | None
PeriodType         — enum: SalaryCycle | CalendarMonth
ScheduleSpec       — sealed class hierarchy: Weekdays | Daily | DaysOfWeek(int[]) | DaysOfMonth(int[])
SettingsKey        — enum: Timezone | EveningTime | SalaryDays | ShiftRule | SilenceDeadlineHours | ...
TimeOfDay          { Hour: int, Minute: int }
NormalizedDescription   { Value: string }   — после lowercase + trim + collapse whitespace
```

### 6.2 Команды

Группировкой по агрегату:

#### User
- `RegisterUser(telegramId, timezone)`
- `UpdateSettings(key, value)`
- `ResetSettings(key?)`
- `ReportIncome(amount, occurredAt, description?)`
- `ReportExpense(amount, occurredAt, description, source?)`
- `CorrectExpenseCategory(expenseId, newCategory)`
- `DeleteExpense(expenseId)`
- `ConfirmSavings(amount, periodId)`
- `RequestConsultation(prompt, scope?)`
- `RequestChart(chartType, params?)`
- `RequestReport(period?)`
- `Cancel()`

#### UserTemplates
- `AddTemplate(name, amount, schedule, category?)`
- `RemoveTemplate(name)`
- `ListTemplates()`

#### UserPlannedExpenses
- `AddPlanned(amount, date, description)`
- `RemovePlanned(plannedId)`
- `ConfirmPlanned(plannedId, actualAmount?)`
- `ListPlanned()`

#### AccessControl
- `WhitelistUser(adminId, telegramId)`
- `RevokeUser(adminId, telegramId)`
- `ListWhitelisted()`

### 6.3 События

#### User lifecycle
- `UserRegistered(userId, telegramId, registeredAt)`
- `UserSettingsUpdated(userId, key, oldValue, newValue, updatedAt)`

#### Income
- `IncomeReported(userId, incomeId, periodId, amount, occurredAt, description?)`
- `IncomeReportRequested(userId, requestedAt)` — для аналитики (опционально)

#### Expense
- `ExpenseReported(userId, expenseId, periodId, amount, occurredAt, description, source)`
- `ExpenseCategorizedAutomatically(userId, expenseId, category, source, needsReview)`
- `ExpenseCategoryConfirmed(userId, expenseId, category)` — пользователь подтвердил
- `ExpenseCategoryCorrected(userId, expenseId, oldCategory, newCategory)` — обновляет memory
- `ExpenseDeleted(userId, expenseId, reason)`

#### Budget / Period
- `BudgetPeriodStarted(userId, periodId, startDate, periodType)`
- `BudgetAllocated(userId, periodId, totalIncome, allocationEssentials, allocationFun, allocationDeposit)`
- `BucketThresholdCrossed(userId, periodId, bucket, thresholdRatio)` — например, осталось <20%
- `SavingsReported(userId, periodId, amount)`
- `BudgetPeriodClosed(userId, periodId, endDate, summary)`

#### Recurring & Planned
- `RecurringTemplateAdded(userId, templateId, name, amount, schedule, category?)`
- `RecurringTemplateRemoved(userId, templateId)`
- `RecurringExpenseExpected(userId, templateId, date)` — для аналитики
- `RecurringExpenseAutoConfirmed(userId, templateId, expenseId, date)`
- `PlannedExpenseAdded(userId, plannedId, amount, date, description)`
- `PlannedExpenseConfirmed(userId, plannedId, expenseId, actualAmount)`
- `PlannedExpenseCancelled(userId, plannedId)`

#### Categorization (через Claude)
- `CategorizationRequested(userId, expenseId, correlationId)`
- `CategorizationCompleted(userId, expenseId, correlationId, category, source)`
- `CategorizationFailed(userId, expenseId, correlationId, reason)`

#### Claude / CB
- `ClaudeRequestSent(useCase, correlationId, sentAt)`
- `ClaudeResponseReceived(correlationId, latencyMs)`
- `ClaudeRequestFailed(correlationId, reason, errorType)`
- `ClaudeBecameUnavailable(reason, until)`
- `ClaudeBecameAvailable()`

#### Advisor / Consultation
- `ConsultationRequested(userId, correlationId, prompt, scope)`
- `ConsultationAnswered(userId, correlationId, response, source)` — source = `claude` | `local-heuristics`
- `AdviceParked(userId, advisorTickType)` — запланированный совет ждёт восстановления Claude
- `AdviceResumedWithFreshContext(userId, advisorTickType)`

#### Reports / Charts
- `ChartRequested(userId, chartType, params)`
- `ChartGenerated(userId, chartType, sizeBytes)`

#### Scheduling
- `EveningTickFired(userId, firedAt)`
- `SilenceDeadlineFired(userId, firedAt)`
- `SalaryDayTickFired(userId, salaryDay, firedAt)`
- `WeeklyAdvisorTickFired(userId, firedAt)`
- `MonthlyAdvisorTickFired(userId, firedAt)`

#### Wakeup
- `SystemDowntimeDetected(from, to, durationSeconds)`
- `WakeupNotificationSent(userId, missedItems)`

#### Whitelist
- `UserWhitelisted(adminId, telegramId, addedAt)`
- `UserRevoked(adminId, telegramId, revokedAt)`

### 6.4 Доменные сервисы (интерфейсы в Domain, реализации в Infrastructure)

```
IWorkdayCalendar
  - bool IsWorkday(DateOnly date)
  - DateOnly NextWorkdayOnOrAfter(DateOnly date)
  - DateOnly PreviousWorkdayOnOrBefore(DateOnly date)

ICategoryRules
  - Category? Match(NormalizedDescription description)

ICategoryBucketMap
  - Bucket Map(Category category, IBucketMappingOverrides? overrides = null)

ITimezoneRegistry
  - TimeZoneInfo Get(string ianaName)
  - TimeZoneInfo Default { get; }

IClaudeClient
  - Task<ClaudeResponse> SendAsync(ClaudeRequest req, CancellationToken ct)
```

---

## 7. Интеграции

### 7.1 Telegram

**Режимы:**

- `Polling` — long-polling через `TelegramBotClient.GetUpdatesAsync` в режиме `ClusterSingleton` (только один нод).
- `Webhook` — HTTPS endpoint, любой нод принимает, маршрутизирует в shard region. Используется в production multi-node.

Переключатель `Telegram:Mode` в конфиге.

**Парсинг сообщений:**

- Команда (`/cmd args`) — конкретный command handler в `TelegramGatewayActor`.
- Свободный текст:
  - Регулярка `(\d+(?:\.\d+)?)\s*(.*?)(?:[+,]\s*(\d+(?:\.\d+)?)\s*(.*?))*` для парсинга трат.
  - Если матчится — серия `ReportExpense` команд.
  - Если нет — ответ «не понял, см. /help».

**Inline-кнопки:**

- Используются для выбора категории при `/correct`.
- Для подтверждения регулярных трат в вечернем опросе.

### 7.2 Anthropic Claude

**Модель:** настраивается через `Claude:Model` (default `claude-sonnet-4-5` или другую актуальную).

**Use cases и их промпты:**

#### Категоризация

Промпт:
```
System: Ты помощник, который определяет категорию траты. Категории: Groceries, DiningOut, Transport, Utilities, Subscriptions, Entertainment, Health, Clothing, Personal, Education, Gifts, Travel, Other.
Отвечай ОДНИМ словом — название категории из списка.

Few-shot:
"обед в столовой 700" → DiningOut
"лекарство в аптеке" → Health
"uber до офиса" → Transport
"квартплата" → Utilities

User: <описание траты>
```

Ответ: одно слово. Если не из списка — fallback `Other` + `NeedsManualReview`.

#### Консультация / Совет

Промпт включает:
- Текущий период (даты, доход, аллокация).
- Расходы по категориям за период (агрегаты).
- Топ-5 крупных трат.
- Сравнение с предыдущим периодом (опционально).
- Запрос пользователя или контекст (weekly/monthly).

Ответ — свободный текст до 1500 символов (умещается в Telegram message).

**Resilience через Polly v8:**

- `TimeoutStrategy` — 30 сек на запрос.
- **Без CircuitBreakerStrategy** в Polly. CB реализован вручную в `ClaudeConsultantActor` как state machine с `Available` / `Unavailable(until, reason)`.
- **Без RetryStrategy.** Решено: при transient error не ретраим, при quota — ждём reset, при transient — до 20:00 серверного времени.
- `RateLimiterStrategy` — concurrency limit 3 (semaphore).

**Token tracking:**

- После каждого ответа парсятся заголовки `anthropic-ratelimit-tokens-remaining`, `anthropic-ratelimit-tokens-reset`.
- При получении 429 / quota exhaustion — `Unavailable(until = headers.reset, reason: TokensExhausted | RateLimited)`.
- При 5xx / network — `Unavailable(until = next 20:00 server time, reason: TransientError)`.

**Fallback стратегии:**

| Use case | Поведение при `Unavailable` |
|---|---|
| Categorization | Fallback на `Other` + `NeedsManualReview = true`, без очереди |
| Scheduled advice (weekly/monthly tick) | Park-and-refresh: ждём `ClaudeBecameAvailable`, пересобираем снапшот заново |
| `/advice` on-demand | Immediate local fallback (heuristics из `AdvisorActor`) |
| `/ask` (если будет) | Аналогично advice on-demand |

### 7.3 IsDayOff (workday calendar)

API: `GET https://isdayoff.ru/{YYYY-MM-DD}` → `0` (рабочий) / `1` (выходной/праздник).

**Реализация `IsDayOffWorkdayCalendar`:**

- Кеш в памяти (`Dictionary<DateOnly, bool>`) с TTL 7 дней для будущих дат, бесконечный для прошлых.
- При недоступности API — fallback на простую логику (Mon–Fri = workday, Sat/Sun = off).
- Конфиг: `WorkdayCalendar:Provider = isdayoff | static`, `WorkdayCalendar:BaseUrl`, `WorkdayCalendar:CountryCode`.

---

## 8. Конфигурация

### 8.1 Структура `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Akka": "Warning"
    }
  },
  "ConnectionStrings": {
    "Default": ""
  },
  "Telegram": {
    "BotToken": "",
    "Mode": "Polling",
    "WebhookUrl": "",
    "WebhookListenPort": 8443
  },
  "Claude": {
    "ApiKey": "",
    "Model": "claude-sonnet-4-5",
    "MaxTokensPerRequest": 1024,
    "Resilience": {
      "TimeoutPerAttemptSeconds": 30,
      "ConcurrencyLimit": 3,
      "TransientUnavailableUntilHour": 20
    }
  },
  "WorkdayCalendar": {
    "Provider": "isdayoff",
    "BaseUrl": "https://isdayoff.ru",
    "CountryCode": "ru"
  },
  "Auth": {
    "AdminUserIds": []
  },
  "Akka": {
    "ClusterName": "financebot",
    "Discovery": {
      "Method": "config"
    },
    "Cluster": {
      "SeedNodes": [],
      "MinimumMembers": 1,
      "ShardCount": 100,
      "ShardCoordinatorMode": "ddata"
    }
  },
  "Defaults": {
    "EveningTime": "19:00",
    "SalaryDays": [10, 25],
    "ShiftRule": "Previous",
    "SilenceDeadlineHours": 4,
    "AutoConfirmRecurring": true,
    "AutoConfirmOnSilence": true,
    "PeriodType": "SalaryCycle",
    "Allocation": [50, 25, 25]
  },
  "Limits": {
    "MaxUsers": 10000,
    "MaxClaudeRequestsPerUserPerDay": 50
  }
}
```

### 8.2 Переменные окружения (override через ENV)

| Variable | Назначение |
|---|---|
| `ConnectionStrings__Default` | Postgres connection string |
| `Telegram__BotToken` | Telegram bot token |
| `Claude__ApiKey` | Anthropic API key |
| `Auth__AdminUserIds__0` | Telegram ID первого админа |
| `Telegram__Mode` | `Polling` / `Webhook` |
| `Akka__Discovery__Method` | `config` / `dns` / `kubernetes-api` |

### 8.3 Per-user settings (хранятся в `app.users.settings_json`)

| Key | Type | Default | Validation |
|---|---|---|---|
| `timezone` | string (IANA) | server TZ | `TimeZoneInfo.FindSystemTimeZoneById` |
| `evening_time` | `HH:mm` | `19:00` | regex |
| `salary_days` | `int[]` | `[10, 25]` | каждое число 1–28 |
| `shift_rule` | enum | `previous` | `previous` / `next` / `none` |
| `silence_deadline_hours` | int | 4 | 1–24 |
| `auto_confirm_recurring` | bool | true | — |
| `auto_confirm_on_silence` | bool | true | — |
| `period_type` | enum | `salary-cycle` | `salary-cycle` / `calendar-month` |
| `allocation` | `[int, int, int]` | `[50, 25, 25]` | сумма = 100 |
| `bucket_mapping` | `Dictionary<Category, Bucket>` | default | категория должна существовать |

---

## 9. Структура решения

```
FinanceBot/
├── src/
│   ├── FinanceBot.Domain/
│   │   ├── Events/
│   │   │   ├── User/                 (UserRegistered, UserSettingsUpdated)
│   │   │   ├── Income/
│   │   │   ├── Expense/
│   │   │   ├── Budget/
│   │   │   ├── Recurring/
│   │   │   ├── Planned/
│   │   │   ├── Categorization/
│   │   │   ├── Claude/
│   │   │   ├── Advisor/
│   │   │   ├── Scheduling/
│   │   │   ├── Wakeup/
│   │   │   └── Whitelist/
│   │   ├── Commands/
│   │   │   └── (по агрегатам)
│   │   ├── ValueObjects/
│   │   │   ├── Money.cs
│   │   │   ├── Category.cs
│   │   │   ├── Bucket.cs
│   │   │   ├── ShiftRule.cs
│   │   │   ├── PeriodType.cs
│   │   │   ├── ScheduleSpec.cs
│   │   │   ├── TimeOfDay.cs
│   │   │   └── NormalizedDescription.cs
│   │   ├── Services/
│   │   │   ├── IWorkdayCalendar.cs
│   │   │   ├── ICategoryRules.cs
│   │   │   ├── ICategoryBucketMap.cs
│   │   │   ├── ITimezoneRegistry.cs
│   │   │   └── IClaudeClient.cs
│   │   └── FinanceBot.Domain.csproj
│   │
│   ├── FinanceBot.Application/
│   │   ├── Actors/
│   │   │   ├── User/
│   │   │   │   ├── UserActor.cs
│   │   │   │   ├── UserActorState.cs
│   │   │   │   ├── UserDialogFsm.cs
│   │   │   │   └── Messages/
│   │   │   ├── UserTemplates/
│   │   │   ├── UserPlannedExpenses/
│   │   │   ├── Scheduler/
│   │   │   ├── Categorizer/
│   │   │   ├── Claude/
│   │   │   ├── Charts/
│   │   │   ├── Advisor/
│   │   │   ├── AccessControl/
│   │   │   └── Telegram/
│   │   ├── Projections/
│   │   │   ├── ProjectionBase.cs
│   │   │   ├── UsersListProjection.cs
│   │   │   ├── PeriodProjection.cs
│   │   │   ├── ExpenseProjection.cs
│   │   │   ├── IncomeProjection.cs
│   │   │   └── WhitelistProjection.cs
│   │   ├── Configuration/
│   │   │   ├── AkkaConfiguration.cs
│   │   │   ├── ShardingConfiguration.cs
│   │   │   ├── EventTagger.cs
│   │   │   └── ClusterBootstrapper.cs
│   │   └── FinanceBot.Application.csproj
│   │
│   ├── FinanceBot.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Entities/
│   │   │   │   ├── UserEntity.cs
│   │   │   │   ├── PeriodEntity.cs
│   │   │   │   ├── ExpenseEntity.cs
│   │   │   │   ├── IncomeEntity.cs
│   │   │   │   ├── WhitelistEntity.cs
│   │   │   │   ├── ProjectionOffsetEntity.cs
│   │   │   │   └── SystemHeartbeatEntity.cs
│   │   │   ├── Configurations/    (IEntityTypeConfiguration<T>)
│   │   │   └── Migrations/
│   │   ├── WorkdayCalendar/
│   │   │   ├── IsDayOffWorkdayCalendar.cs
│   │   │   └── StaticWorkdayCalendar.cs
│   │   ├── Claude/
│   │   │   ├── ClaudeClient.cs
│   │   │   ├── ClaudePrompts.cs
│   │   │   └── ClaudeRateLimitParser.cs
│   │   ├── Charts/
│   │   │   ├── ChartRenderer.cs
│   │   │   ├── CategoryPieRenderer.cs
│   │   │   ├── DailyBarRenderer.cs
│   │   │   ├── BucketUtilizationRenderer.cs
│   │   │   └── SavingsLineRenderer.cs
│   │   ├── CategoryRules/
│   │   │   ├── JsonCategoryRules.cs
│   │   │   └── rules.json           (embedded resource)
│   │   └── FinanceBot.Infrastructure.csproj
│   │
│   └── FinanceBot.Host/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── appsettings.Production.json
│       ├── HostExtensions.cs
│       └── FinanceBot.Host.csproj
│
├── tests/
│   ├── FinanceBot.Domain.Tests/
│   ├── FinanceBot.Application.Tests/
│   └── FinanceBot.Integration.Tests/
│
├── docker/
│   ├── Dockerfile
│   └── docker-compose.yml
│
├── .env.example
├── .gitignore
├── .editorconfig
├── Directory.Build.props
├── Directory.Packages.props
├── FinanceBot.sln
└── README.md
```

**Соглашения:**

- Centralized package management через `Directory.Packages.props`.
- `Nullable = enable`, `TreatWarningsAsErrors = true` через `Directory.Build.props`.
- Все проекты на `<TargetFramework>net10.0</TargetFramework>`.

---

## 10. Этапы разработки

Этапы упорядочены так, чтобы базовые слои писались раз и не переделывались. После Stage 4 (Actor Foundation) можно работать командой параллельно — каждый разработчик берёт по этапу из 5–22.

### Stage 1 — Solution Foundation

**Цель:** поднять структуру решения, инфраструктуру, базовые конфиги.

**Deliverables:**

- `FinanceBot.sln` с четырьмя проектами.
- `Directory.Build.props`, `Directory.Packages.props`.
- `Dockerfile` (multi-stage), `docker-compose.yml`, `.env.example`, `.gitignore`.
- `Program.cs` в `FinanceBot.Host` с `Host.CreateApplicationBuilder` и `AddAkka()`.
- Akka.NET + Akka.Persistence.PostgreSql + Akka.Cluster + Akka.Cluster.Sharding в зависимостях.
- Postgres контейнер в compose, healthcheck, volume для persistence.
- `appsettings.json` с полной структурой (см. 8.1), все секреты пустые.
- README с инструкциями по запуску.

**Acceptance:**

- `docker-compose up` поднимает Postgres и пустой bot, который успешно стартует и не крэшится.
- В логах виден факт инициализации Akka кластера single-node.

**Не делается:**

- Никаких акторов с логикой.
- Никаких миграций EF Core.

---

### Stage 2 — Domain Foundation

**Цель:** определить **все** доменные события, команды, value objects заранее. Чтобы добавление новых фич не требовало переписывать существующие.

**Deliverables:**

- В `FinanceBot.Domain`:
  - `ValueObjects/` — все типы из раздела 6.1.
  - `Events/` — **все** события из раздела 6.3, разбитые по подпапкам.
  - `Commands/` — все команды из раздела 6.2.
  - `Services/` — все интерфейсы из раздела 6.4.
- Все события — `record` с readonly properties, иммутабельны, имеют `EventVersion: int = 1`.
- Все команды — `record`, иммутабельны.
- Категории и бакеты — как `enum` или `sealed class with static readonly` (выбор архитектора, обоснован).

**Acceptance:**

- Domain проект собирается без зависимостей кроме BCL.
- Все типы покрыты xUnit тестами на сериализацию (если выбран JSON для journal).
- Документация (XML comments) на каждом публичном типе.

---

### Stage 3 — Persistence Foundation

**Цель:** настроить полную схему БД, чтобы дальше только читать/писать, не менять структуру.

**Deliverables:**

- В `FinanceBot.Infrastructure`:
  - `AppDbContext` с `DbSet<>` для всех entity из 5.4.
  - Все entity и `IEntityTypeConfiguration<T>`.
  - Initial migration `M001_InitialSchema` создаёт **все** таблицы из 5.4.
- Конфигурация Akka.Persistence.PostgreSql на схему `akka` — авто-создание таблиц journal/snapshot.
- В `FinanceBot.Host`:
  - На старте: `dbContext.Database.MigrateAsync()` применяет миграции `app`-схемы.
  - Health check Postgres connectivity.

**Acceptance:**

- При старте свежей БД создаются обе схемы (`akka` и `app`) со всеми таблицами.
- Миграции идемпотентны.

---

### Stage 4 — Actor Foundation

**Цель:** настроить кластерное окружение акторов без бизнес-логики.

**Deliverables:**

- В `FinanceBot.Application/Configuration/`:
  - `AkkaConfiguration` — HOCON для cluster, sharding, persistence.
  - `ShardingConfiguration` — конфиг для трёх shard regions (User, UserTemplates, UserPlannedExpenses) с `MessageExtractor`.
  - `EventTagger` — `IEventAdapter` навешивающий теги по правилам из 5.6.
- Skeleton акторы (без логики, только receive boilerplate, persist пустого события на старте):
  - `UserActor : ReceivePersistentActor`
  - `UserTemplatesActor : ReceivePersistentActor`
  - `UserPlannedExpensesActor : ReceivePersistentActor`
  - `TelegramGatewayActor` — long polling stub, логирует входящие.
  - `AccessControlActor : ReceivePersistentActor` — singleton.
  - `SchedulerActor` — singleton, пустой.
- Базовый класс `ProjectionBase` с инфраструктурой offset-tracking и подписки на `EventsByTag`.
- Регистрация всех ShardRegions, ClusterSingletons и per-node services через `Akka.Hosting`.

**Acceptance:**

- При старте бота поднимаются ShardRegions, ClusterSingletons.
- Можно отправить любое сообщение в shard region — UserActor создаётся, persist пустого события проходит.
- В логах видны cluster events (`MemberUp`, `ShardingStarted`).

**Не делается:**

- Никакой обработки команд.

---

### Stage 5 — `/start`, `/help`, `/whoami` и регистрация

**Цель:** первый рабочий end-to-end сценарий — пользователь регистрируется.

**Deliverables:**

- `TelegramGatewayActor`:
  - Парсинг `/start`, `/help`, `/whoami`, `/cancel`.
  - Проверка whitelist через AccessControlActor.
  - Маршрутизация в shard region User.
- `UserActor`:
  - Обработка `RegisterUser` → persist `UserRegistered`.
  - Обработка `Cancel` (всегда возвращает в Idle).
- `AccessControlActor`:
  - Команда `IsAllowed(telegramId)` → `Allowed | Denied(reason)`.
  - Логика: AdminUserIds + проекция whitelist.
- `UsersListProjection`:
  - Подписка на тег `user-`.
  - Применение `UserRegistered` → INSERT в `app.users` с дефолтными settings.
  - Применение `UserSettingsUpdated` → UPDATE.
- Welcome-сообщение и `/help` контент.

**Acceptance:**

- Пользователь из `AdminUserIds` пишет `/start` → получает welcome → запись в `app.users`.
- Не-whitelisted пользователь пишет `/start` → получает «Доступ ограничен. Твой ID: ...».
- `/whoami` возвращает корректный telegram_id.
- `/help` возвращает шаблонный список команд.

---

### Stage 6 — Админ-команды (`/adduser`, `/removeuser`, `/listusers`)

**Deliverables:**

- `TelegramGatewayActor` — парсинг админ-команд, проверка `AdminUserIds`.
- `AccessControlActor`:
  - `WhitelistUser` → `UserWhitelisted` event.
  - `RevokeUser` → `UserRevoked` event.
  - `ListWhitelisted` → ответ списком.
- `WhitelistProjection` — обновление `app.whitelist`.
- Reply-сообщения для админа.

**Acceptance:**

- Админ добавляет нового пользователя → запись в `app.whitelist`.
- Добавленный пользователь успешно регистрируется через `/start`.
- Не-админ не может выполнить `/adduser` (получает «команда недоступна»).

---

### Stage 7 — `/settings`

**Deliverables:**

- Парсинг `/settings`, `/settings <key> <value>`, `/settings reset [<key>]`.
- `UserActor` обработка `UpdateSettings`, `ResetSettings`:
  - Валидация ключа и значения по правилам из 8.3.
  - Persist `UserSettingsUpdated`.
- `UsersListProjection` обновляет `settings_json`.
- Форматирование текущих настроек в ответе на `/settings` без аргументов.
- При изменении timezone / evening_time / salary_days — пересчёт тиков делается в Stage 16.

**Acceptance:**

- Все настройки из 8.3 принимаются и валидируются.
- Невалидное значение не меняет состояние, возвращает понятную ошибку.
- `/settings` без аргумента красиво форматирует все настройки.

---

### Stage 8 — `/income`

**Deliverables:**

- Парсинг `/income [<date>] <amount> [<description>]`.
- `UserActor`:
  - Обработка `ReportIncome`.
  - Если активного периода нет (или это первый доход после `BudgetPeriodClosed`) — генерируется `BudgetPeriodStarted`.
  - Persist `IncomeReported`.
  - Расчёт текущего total_income в периоде, генерация `BudgetAllocated` с обновлёнными аллокациями.
- `IncomeProjection` — INSERT в `app.incomes`.
- `PeriodProjection` — UPSERT в `app.periods` (создание / обновление total_income и allocations).
- Reply: «Принято. Период с X по Y. Аллокация: Essentials Z, Fun W, Deposit V».

**Acceptance:**

- `/income 50000` создаёт период (если нет активного), записывает доход, рассчитывает 50/25/25.
- Повторный `/income 30000` в том же периоде увеличивает total_income, аллокации пересчитываются.
- В `app.periods` корректные значения.

---

### Stage 9 — `/expense`, `/expense_day`, свободный текст

**Deliverables:**

- Парсинг команд и свободного текста (regexp на множественные траты через `+` / `,`).
- `UserActor`:
  - Обработка `ReportExpense`.
  - Persist `ExpenseReported`.
  - Пока без категоризации — категория = `Other`, `NeedsManualReview = false` (или `true`, выбор архитектора).
- `ExpenseProjection` — INSERT в `app.expenses`.
- Reply с подтверждением и текущим остатком в бакете.

**Acceptance:**

- Свободный текст «обед 750 + такси 400» создаёт две записи `ExpenseReported`.
- `/expense_day 1500` создаёт одну запись с описанием = `(итог дня)` или пустым.
- Reply показывает остатки бакетов.

---

### Stage 10 — Категоризатор: локальные правила

**Deliverables:**

- `JsonCategoryRules` — реализация `ICategoryRules`:
  - Загрузка `rules.json` из embedded resource.
  - Match по keyword (с приоритетами, longest match wins).
- Расширение конфига: rules.json содержит все базовые правила для 12 категорий.
- `CategorizerActor` (per-node, stateless):
  - Receive `CategorizeRequest(description, correlationId)`.
  - Применение `ICategoryRules.Match`.
  - Если match — reply `CategorizationCompleted(category, source: Rules)`.
  - Если miss — reply `CategorizationCompleted(category: Other, source: Fallback)` (на этом этапе, без Claude).
- `UserActor` теперь после `ReportExpense`:
  - Шлёт async запрос в `CategorizerActor`.
  - Stash других сообщений до получения ответа.
  - На ответ — persist `ExpenseCategorizedAutomatically`.

**Acceptance:**

- «обед 750» → DiningOut.
- «такси домой 500» → Transport.
- «лекарство 800» → Health.
- Незнакомое описание → Other.
- В `app.expenses` корректные категории и source.

---

### Stage 11 — Memory категоризации + `/correct`

**Deliverables:**

- В `UserActor`:
  - Поле в state: `Dictionary<NormalizedDescription, Category> CategoryMemory`.
  - Восстанавливается из `ExpenseCategoryCorrected` событий и из автоматических `ExpenseCategorizedAutomatically` (где source = Claude).
  - Перед делегированием в CategorizerActor — lookup в memory.
- Команда `/correct`:
  - Reply со списком последних 10 трат (особо помеченных `NeedsManualReview`) с inline-кнопками выбора категории.
  - На callback — persist `ExpenseCategoryCorrected`, обновление memory.
- `ExpenseProjection` — UPDATE при `ExpenseCategoryCorrected`.

**Acceptance:**

- После `/correct` правильная категория сохраняется в memory.
- Следующая трата с тем же описанием категоризуется через memory мгновенно (source = Memory).
- Историю прошлых трат `/correct` не меняет.

---

### Stage 12 — Claude integration с unavailability state

**Deliverables:**

- `IClaudeClient` реализация `ClaudeClient` в `Infrastructure`:
  - HttpClient-based вызов API.
  - Парсинг rate limit заголовков (`anthropic-ratelimit-tokens-*`).
  - Polly TimeoutStrategy на 30 сек.
- `ClaudeConsultantActor` (cluster singleton):
  - State machine: `Available` / `Unavailable(until, reason)`.
  - Receive `ClaudeRequest(useCase, prompt, correlationId)`.
  - Если `Unavailable` и `now < until` — reply `ClaudeUnavailable`.
  - Иначе — вызов `IClaudeClient`, reply `ClaudeResponse` или `ClaudeUnavailable`.
  - Публикация событий `ClaudeBecameUnavailable`, `ClaudeBecameAvailable` для observability.
- `CategorizerActor` дополняется:
  - Если rules miss — шлёт `ClaudeRequest(useCase: Categorization)`.
  - На `ClaudeResponse` — парсит категорию из текста, reply `CategorizationCompleted(source: Claude)`.
  - На `ClaudeUnavailable` — reply `CategorizationCompleted(category: Other, source: Fallback, NeedsReview: true)`.
- `SchedulerActor` (опережая Stage 16) добавляет `ClaudeAutoRecoveryTick` каждый день в 20:00 server time → шлёт `ResetUnavailable` синглтону Claude. Только для `TransientError`.

**Acceptance:**

- Незнакомая трата уходит в Claude, возвращается с категорией.
- Claude добавляет результат в memory автоматически.
- Симуляция 429 (моком) → `Unavailable` → последующие запросы возвращают fallback мгновенно.
- В 20:00 server time `Unavailable` (TransientError) сбрасывается.

---

### Stage 13 — Recurring Templates (`/template`)

**Deliverables:**

- `UserTemplatesActor`:
  - State: `Dictionary<TemplateId, RecurringTemplate>`.
  - Команды `AddTemplate`, `RemoveTemplate`, `ListTemplates`.
  - Persist `RecurringTemplateAdded`, `RecurringTemplateRemoved`.
  - Query: `GetRelevantForDate(DateOnly date)` → шаблоны, активные в эту дату по schedule.
- Парсинг `/template add`, `/template list`, `/template remove`.
- Schedule parser:
  - `weekdays` → `Weekdays`
  - `daily` → `Daily`
  - `dow:1,3,5` → `DaysOfWeek`
  - `dom:1,15` → `DaysOfMonth`
- Использование `IWorkdayCalendar` для `weekdays` (праздники = не активен).

**Acceptance:**

- `/template add lunch 700 weekdays DiningOut` создаёт шаблон.
- `/template list` показывает.
- `GetRelevantForDate(пятница)` возвращает lunch, `GetRelevantForDate(праздник)` — нет.

---

### Stage 14 — Planned Expenses (`/plan`)

**Deliverables:**

- `UserPlannedExpensesActor`:
  - State: `Dictionary<PlannedId, PlannedExpense>` со статусами Active / Confirmed / Cancelled.
  - Команды `AddPlanned`, `RemovePlanned`, `ConfirmPlanned`, `ListPlanned`.
  - Persist соответствующие события.
- Парсинг `/plan add`, `/plan list`, `/plan remove`.
- При `ConfirmPlanned` — генерируется `PlannedExpenseConfirmed` + соответствующий `ExpenseReported` в UserActor.
- Прогноз бюджета с учётом планов (используется в advisor и при показе остатков).

**Acceptance:**

- `/plan add 30000 2025-05-15 rent` создаёт план.
- При вечернем опросе 15-го числа бот напоминает (Stage 17).
- `/plan list` показывает активные.

---

### Stage 15 — `/savings` и закрытие периода

**Deliverables:**

- Парсинг `/savings <amount>`.
- `UserActor`:
  - `ConfirmSavings` → persist `SavingsReported`.
  - При появлении `IncomeReported` в новом периоде (после старого) — persist `BudgetPeriodClosed` для предыдущего.
- `PeriodProjection`:
  - При `SavingsReported` — UPDATE `savings_actual`.
  - При `BudgetPeriodClosed` — UPDATE `status = closed`, `end_date`.

**Acceptance:**

- `/savings 12000` фиксирует факт перевода.
- При начале нового периода старый автоматически закрывается.

---

### Stage 16 — SchedulerActor и тики

**Deliverables:**

- `SchedulerActor`:
  - State (in-memory): map `userId → ScheduledTicks { evening, salary, weekly, monthly, silenceDeadline }`.
  - На старте: загрузка списка пользователей из `app.users`, расчёт ближайших тиков для каждого.
  - Использование `Context.System.Scheduler.ScheduleTellOnceCancelable` для каждого тика.
  - После fire — пересчёт следующего тика того же типа.
  - Подписка на тег `settings` — при `UserSettingsUpdated` для timezone/evening_time/salary_days/shift_rule пересчёт тиков.
- `IWorkdayCalendar.IsDayOffWorkdayCalendar` — реализация с кешем.
- `SystemHeartbeat` — запись каждую минуту в `app.system_heartbeat`.
- `ClaudeAutoRecoveryTick` — system tick на 20:00 server.

**Acceptance:**

- При старте бота `SchedulerActor` запоминает всех юзеров и планирует тики.
- В `evening_time` юзера приходит `EveningTickFired` (UserActor пока ничего не делает с ним).
- В `salary_days` со сдвигом по shift_rule приходит `SalaryDayTickFired`.
- Изменение `evening_time` через `/settings` пересчитывает следующий тик.

---

### Stage 17 — Вечерний опрос с FSM

**Deliverables:**

- `UserActor` FSM:
  - State `Idle` → на `EveningTickFired`:
    - Запрос шаблонов на сегодня у `UserTemplatesActor`.
    - Запрос планов на сегодня у `UserPlannedExpensesActor`.
    - Формирование текста вопроса.
    - Переход в `AwaitingDailyExpenses`.
    - Stash других входящих сообщений до выхода из state (с таймаутом).
  - State `AwaitingDailyExpenses`:
    - На свободный текст — парсинг трат, обработка регулярных (подтверждение / коррекция / отмена).
    - На `SilenceDeadlineFired` — авто-фиксация регулярных (если `auto_confirm_on_silence`).
    - На `/cancel` — возврат в Idle, unstash.
    - На `EveningResponseReceived` — обработка, возврат в Idle, unstash.
- Шаблоны, не упомянутые в ответе, считаются пропущенными (если `auto_confirm_recurring` = false) или подтверждаются (если true).
- В конце — reply со сводкой дня и графиком категорий (Stage 20).

**Acceptance:**

- В `evening_time` приходит вопрос с упоминанием релевантных шаблонов.
- Ответ типа «обед 750 + такси 400» парсится, регулярный обед обновляется, такси добавляется.
- При молчании 4 часа — регулярные авто-фиксируются.
- При `/cancel` — выход без записи.

---

### Stage 18 — Wakeup-уведомления

**Deliverables:**

- `SchedulerActor` на старте:
  - Чтение last `SystemHeartbeat`.
  - Если gap > 5 минут — для каждого whitelisted user вычисляются пропущенные тики.
  - Шлёт `WakeupCheck(userId, downtimeFrom, downtimeTo, missedItems)` в shard region User.
- `UserActor`:
  - Обработка `WakeupCheck`.
  - Формирование персонального сообщения с пропущенным.
  - Reply через TelegramGateway.
  - Persist `WakeupNotificationSent`.

**Acceptance:**

- Бот лежал > 5 минут → при старте каждый юзер получает текстовое уведомление с указанием пропущенного.
- Если пропущен advisor-тик — есть упоминание `/advice`.
- Повторный рестарт в течение того же простоя не дублирует уведомления.

---

### Stage 19 — Advisor (`/advice`, weekly/monthly tick)

**Deliverables:**

- `AdvisorActor` (per-node):
  - `BuildSnapshot(userId)` — собирает данные пользователя из проекций (текущий период, расходы по категориям, топ трат, сравнение с предыдущим).
  - `BuildLocalAdvice(snapshot)` — heuristics: «категория Х на N% выше среднего», «осталось дней до конца периода — Y, на Essentials осталось Z, риск перерасхода» и т.п.
- `UserActor`:
  - На `WeeklyAdvisorTickFired` / `MonthlyAdvisorTickFired` / команду `/advice`:
    - Запрос snapshot у Advisor.
    - Формирование промпта.
    - Запрос в `ClaudeConsultantActor`.
    - На ответ — reply пользователю.
    - На `Unavailable`:
      - Если по тику — persist `AdviceParked`, ждём `ClaudeBecameAvailable` event на EventStream → пересобираем snapshot, шлём заново.
      - Если по `/advice` — fallback на `BuildLocalAdvice`, reply с пометкой «локальный совет, Claude недоступен».

**Acceptance:**

- `/advice week` возвращает адекватный совет от Claude.
- При `Unavailable` `/advice` возвращает локальный совет.
- При `WeeklyAdvisorTickFired` и `Unavailable` — совет приходит позже, после восстановления Claude.

---

### Stage 20 — Графики

**Deliverables:**

- `ChartRenderer`:
  - Реализации для 4 типов графиков (см. 3.10).
  - Каждый рендерер запрашивает данные из проекций (через `AppDbContext` read-only).
  - ScottPlot 5 — рендер в `byte[]` PNG.
- `ChartRendererPool` — Akka router (RoundRobin, 4 воркера на нод).
- Парсинг `/chart category|daily|buckets|savings`.
- `UserActor`:
  - На `RequestChart` — шлёт в pool, на ответ — отправляет PNG в Telegram.
  - В вечернем summary автоматически прикладывается `category` график.

**Acceptance:**

- `/chart category` возвращает PNG pie-chart.
- В вечерней сводке автоматически приходит график.
- Все 4 типа графиков работают.

---

### Stage 21 — `/report`

**Deliverables:**

- Парсинг `/report [period]`:
  - `current` (default), `previous`, `1`, `2`, ... (N периодов назад).
- `UserActor` — формирует текстовый отчёт из проекций:
  - Total income, expenses, savings.
  - Разбивка по бакетам и категориям.
  - Топ-5 крупных трат.
  - Сравнение с предыдущим периодом.

**Acceptance:**

- `/report` возвращает читаемый текст за текущий период.
- `/report previous` — за предыдущий.
- При отсутствии данных — корректное сообщение.

---

### Stage 22 — Multi-node hardening

**Deliverables:**

- `docker-compose.multi-node.yml` — два инстанса бота.
- Тесты:
  - Поднять 2 нода, отправить команды в оба, убедиться что shard regions работают.
  - Убить один нод, проверить что singleton переехал, шарды ребалансировались.
- Webhook mode для Telegram:
  - HTTP endpoint в `FinanceBot.Host` (через `Microsoft.AspNetCore.Hosting`).
  - Маршрутизация update в shard region User напрямую.
- Конфиг переключения `Telegram:Mode = Webhook` + `WebhookUrl`.
- Akka.Discovery.KubernetesApi — добавлено в зависимости.

**Acceptance:**

- Multi-node compose запускается, оба нода присоединяются к кластеру.
- Failover работает.
- Webhook mode принимает обновления через HTTPS.

---

### Stage 23 — Production polish

**Deliverables:**

- Health checks endpoint `/health`:
  - Postgres connectivity.
  - Akka cluster status (member count, no unreachable).
- Graceful shutdown:
  - При SIGTERM — leave cluster, дожидаемся завершения in-flight requests.
- Логи:
  - Ревизия структурированности (event ID, correlation ID).
  - Sensitive data redaction в логах (telegram_id ок, описания трат — частично).
- README с инструкциями для production deployment.
- Опционально: K8s manifests (Deployment, Service, ConfigMap, Secrets).

**Acceptance:**

- Health endpoint возвращает 200 при здоровом состоянии, 503 иначе.
- SIGTERM приводит к чистому завершению.
- Логи содержат достаточно информации для дебага.

---

## 11. Тестирование

### 11.1 Unit-тесты (Domain)

- xUnit.
- Все value objects, доменные функции (валидация settings, вычисление salary day со сдвигом, парсинг description, нормализация).
- Цель покрытия: 80%+.

### 11.2 Actor-тесты (Application)

- `Akka.TestKit.Xunit2`.
- Каждый актор тестируется изолированно с TestProbe для зависимостей.
- Тестируются:
  - Корректные команды → корректные события.
  - Невалидные команды → корректные ошибки.
  - FSM переходы.
  - Persistence: рестарт актора в тесте, проверка восстановления состояния.
- Snapshot тестов — да (на корректность сериализации).

### 11.3 Integration-тесты

- TestContainers для Postgres.
- Сценарии:
  - Полный flow `/start` → `/income` → `/expense` → `/report`.
  - Wakeup notification после имитации простоя.
  - Корректность проекций под нагрузкой.
- Запускаются в CI отдельным джобом.

### 11.4 Load / Stress

- Опционально на M22+.
- Имитация 1000 одновременных пользователей.
- Проверка latency и memory footprint.

---

## 12. Deployment и эксплуатация

### 12.1 Локальный dev

```
git clone ...
cp .env.example .env
# отредактировать .env
docker compose up
```

Альтернатива (для дебага из IDE):

```
docker compose up postgres
# запустить FinanceBot.Host из IDE
```

### 12.2 Production single-node

```
docker compose -f docker-compose.prod.yml up -d
```

`docker-compose.prod.yml` отличается:
- `Telegram:Mode = Webhook`.
- TLS termination через reverse proxy (nginx/traefik) или встроенный HTTPS.
- `restart: always`.
- Resource limits.

### 12.3 Production multi-node (k8s)

- Deployment `financebot-bot` с replicas: 3.
- Headless Service для cluster discovery.
- Akka.Discovery.KubernetesApi.
- Postgres внешний (managed) или StatefulSet.
- Secrets через `Secret` объекты.
- Ingress с TLS для webhook.

### 12.4 Мониторинг

- Prometheus / Grafana — позже, опционально.
- На MVP — `docker logs` + ручные SQL-запросы к БД.

### 12.5 Backup

- Postgres `pg_dump` ежедневно (вне scope текущего проекта, инфраструктурная задача).
- Schema `akka` критична — всё восстанавливается из неё.

---

## 13. Конвенции и стандарты

### 13.1 Стиль кода

- `.editorconfig` с правилами C#:
  - 4 пробела отступ.
  - LF line endings.
  - UTF-8.
  - Trim trailing whitespace.
- Roslyn analyzers: дефолтные + StyleCop (опционально).
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` в `Directory.Build.props`.
- `<Nullable>enable</Nullable>` везде.

### 13.2 Naming

- Классы / типы: `PascalCase`.
- Методы / public properties: `PascalCase`.
- Поля private: `_camelCase`.
- Локальные переменные / параметры: `camelCase`.
- Async методы: суффикс `Async`.
- Интерфейсы: префикс `I`.
- Акторы: суффикс `Actor`.
- События доменные: глагол в прошедшем времени (`UserRegistered`).
- Команды: глагол в инфинитиве (`RegisterUser`).

### 13.3 Архитектурные правила

- Domain не зависит ни от чего, кроме BCL.
- Application не зависит от Infrastructure напрямую — только через интерфейсы из Domain.
- Composition root — `FinanceBot.Host/Program.cs`. Только там разрешена прямая регистрация конкретных классов в DI.
- Все настройки строго через `IOptions<T>` или `IConfiguration`, никаких `Environment.GetEnvironmentVariable` в логике.
- Все async методы принимают `CancellationToken`.
- Никаких `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` — везде `await`.
- Каждый актор имеет `static Props CreateProps(...)` и приватный конструктор (или внутренний).

### 13.4 Версионирование событий

- Каждое доменное событие имеет `EventVersion: int = 1`.
- При изменении схемы события — увеличивается версия + создаётся `IEventAdapter` для апгрейда старых версий.
- Полезные load-нагрузки сериализуются через System.Text.Json с настройками по умолчанию + `JsonStringEnumConverter`.

### 13.5 Документация

- XML comments на всех публичных типах в Domain.
- README в корне проекта.
- ADR (Architecture Decision Records) в `/docs/adr/` для значимых решений (не обязательно на MVP).

### 13.6 Git workflow

- Trunk-based development или git-flow — по выбору команды.
- Conventional commits (feat:, fix:, refactor:, chore:).
- PR обязателен для main.
- CI проверяет: build, unit tests, actor tests, lint.

---

## Приложения

### A. Список всех команд Telegram

См. раздел 3.13.

### B. Полный список доменных событий

См. раздел 6.3.

### C. Полная схема БД

См. раздел 5.4.

### D. Карта зависимостей этапов

```
Stage 1 (Foundation)
      │
Stage 2 (Domain)
      │
Stage 3 (Persistence)
      │
Stage 4 (Actor Foundation)
      │
      ├───→ Stage 5 (/start, /help, /whoami)
      │           │
      │           ├───→ Stage 6 (Admin commands)
      │           │
      │           ├───→ Stage 7 (/settings)
      │           │
      │           ├───→ Stage 8 (/income)
      │           │           │
      │           │           ├───→ Stage 9 (/expense)
      │           │           │           │
      │           │           │           ├───→ Stage 10 (Categorizer rules)
      │           │           │           │           │
      │           │           │           │           ├───→ Stage 11 (Memory + /correct)
      │           │           │           │           │
      │           │           │           │           └───→ Stage 12 (Claude integration)
      │           │           │           │                       │
      │           │           │           │                       ├───→ Stage 19 (Advisor)
      │           │           │           │                       │
      │           │           │           │                       └───→ Stage 17 (FSM evening)
      │           │           │           │
      │           │           │           ├───→ Stage 13 (Templates)
      │           │           │           │           │
      │           │           │           │           └───→ Stage 17 (FSM evening)
      │           │           │           │
      │           │           │           ├───→ Stage 14 (Plans)
      │           │           │           │
      │           │           │           └───→ Stage 15 (/savings)
      │           │           │
      │           │           ├───→ Stage 21 (/report)
      │           │           │
      │           │           └───→ Stage 20 (Charts) → Stage 17
      │           │
      │           └───→ Stage 16 (Scheduler)
      │                       │
      │                       ├───→ Stage 17 (FSM evening)
      │                       │
      │                       └───→ Stage 18 (Wakeup)
      │
      └───→ Stage 22 (Multi-node) → Stage 23 (Production)
```

После Stage 4 параллельная работа возможна по веткам Stage 5–21. Stage 17 объединяет несколько веток (Categorizer + Templates + Plans + Charts).

---

**Конец документа.**

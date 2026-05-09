# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

FinanceBot — Telegram бот для отслеживания личных финансов (бюджет 50/25/25, AI-категоризация трат через Anthropic Claude, графики). Полное ТЗ в `task.md`. Этапы 1–5 реализованы; Stage 6+ ещё впереди.

## Build / run / test

```bash
# build the solution
dotnet build FinanceBot.sln

# release build
dotnet build FinanceBot.sln -c Release

# run host locally (requires Postgres reachable per ConnectionStrings:Default)
dotnet run --project src/FinanceBot.Host

# postgres-only via compose, then run host from IDE
docker compose -f docker/docker-compose.yml up -d postgres

# full compose (postgres + bot)
docker compose -f docker/docker-compose.yml up

# tests (project not yet created; uses xUnit + Akka.TestKit + Testcontainers when added)
dotnet test
```

Никогда не запускать `dotnet ef migrations add` без `--startup-project src/FinanceBot.Infrastructure` — design-time factory живёт в Infrastructure (`AppDbContextFactory`), не в Host:

```bash
dotnet ef migrations add <Name> \
  --project src/FinanceBot.Infrastructure \
  --startup-project src/FinanceBot.Infrastructure \
  --context AppDbContext \
  --output-dir Persistence/Migrations
```

## Architecture (big picture)

Четыре проекта под `src/`, строгая иерархия зависимостей (см. §13.3 ТЗ):
- `FinanceBot.Domain` — только BCL. Events / Commands / ValueObjects / service interfaces (`IWorkdayCalendar`, `ICategoryRules`, `IClaudeClient`, …). Никаких ссылок на Akka, EF Core, Telegram.Bot.
- `FinanceBot.Application` — Akka actors, projections, конфигурация Akka. Зависит только от Domain. Знает Akka, но НЕ знает EF Core / Telegram.Bot / HTTP — все интеграции абстрагированы интерфейсами (`ITelegramBot`, `IProjectionOffsetStore`, `IUsersReadModelWriter`).
- `FinanceBot.Infrastructure` — реализации этих интерфейсов поверх EF Core / Telegram.Bot / HttpClient. Зависит от Domain + Application.
- `FinanceBot.Host` — composition root, Program.cs, hosted services, Web host (для health endpoint и будущего webhook).

**Архитектурный стиль:** Event-sourced DDD + Akka.NET Actor Model + CQRS на уровне persistence.
- Доменные события — единственный источник правды; персистятся в схему `akka` (Akka.Persistence.PostgreSql, `auto-initialize=true`).
- Read-model — отдельная схема `app` (EF Core 10, миграции применяются `DatabaseMigrationService` на старте).
- Между ними — projections (`ProjectionBase`), читающие `EventsByTag` через `Akka.Persistence.Query.IEventsByTagQuery` и обновляющие read-model. Каждая projection хранит offset в `app.projection_offsets` и работает как cluster singleton.

**Карта акторов** (полная — §5.2 ТЗ):
- Cluster singletons: `AccessControlActor` (whitelist), `SchedulerActor` (все тики), `ClaudeConsultantActor` (TBD), projections (`UsersListProjection`, …).
- Shard regions: `UserActor` (per userId), `UserTemplatesActor`, `UserPlannedExpensesActor`. Все три используют один и тот же `UserShardMessageExtractor` поверх `ShardEnvelope { EntityId, Message }`.
- Per-node services: `TelegramGatewayActor` (singleton-like, ловит `IncomingTelegramUpdate`), `CategorizerActor` (TBD), `AdvisorActor` (TBD), `ChartRendererPool` (TBD).

**Tagging событий** — через `EventTagger : IWriteEventAdapter` (`Configuration/EventTagger.cs`), навешивает теги по правилам §5.6: `user-{userId}`, `expense`, `income`, `period`, `category`, `whitelist`, `settings`, плюс наш собственный `user-lifecycle` для UsersListProjection.

**Telegram flow (Stage 5):** `TelegramPollingHostedService` (Host) → `TelegramBotAdapter.PollAsync` (Infrastructure) → `IncomingTelegramUpdate` → `TelegramGatewayActor` (Application). Парсинг команды → `AccessControlActor.Ask<AccessDecision>` → если allowed, маршрут в shard region User через `ShardEnvelope`. Ответы пользователю — `OutgoingTelegramReply` публикуется в `EventStream`, `TelegramReplyDispatcher` (Host) подхватывает и зовёт `ITelegramBot.SendTextAsync`.

**Маппинг telegramId → userId**: детерминистический UUIDv5 (`UserIdFromTelegramId.Resolve`). Один и тот же telegramId всегда даёт один и тот же Guid — нет нужды в централизованном реестре «id → guid», что важно для shard sharding.

## Conventions specific to this codebase

- C# 14 / .NET 10 / `<Nullable>enable</Nullable>` / `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (см. `Directory.Build.props`).
- Centralized package management в `Directory.Packages.props` — `PackageReference` без Version, версии только тут.
- Все доменные события — `record` с `int EventVersion = 1` и `DateTimeOffset OccurredAt`. При изменении схемы — увеличивается версия + `IEventAdapter` для апгрейда.
- Команды — глагол в инфинитиве (`RegisterUser`), события — глагол в прошедшем времени (`UserRegistered`).
- Каждый persistent actor: уникальный `PersistenceId` (`user-{userId:N}`, `user-templates-{userId:N}`, `user-planned-{userId:N}`, `access-control`).
- Snapshot policy: каждые 100 событий, хранятся последние 3.
- Stage-инкрементальное расширение акторов делается через `partial class` (см. `TelegramGatewayActor.cs` + `TelegramGatewayActor.Stage5.cs`). Wire-метод объявляется как `partial void WireStageN()` и реализуется в отдельном файле — это позволяет добавлять stage'ы без правки уже зафиксированной части.
- Никаких `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` — везде `await`.
- Все async методы принимают `CancellationToken`.
- Секреты только через ENV (`Telegram__BotToken`, `Claude__ApiKey`, `ConnectionStrings__Default`, `Auth__AdminUserIds__0`). Никогда не коммитить заполненный `.env`.

## Что отключено в build-конфиге (и почему)

`Directory.Build.props` подавляет:
- `NU1903`, `NU1904` — vulnerable transitive deps от сторонних пакетов (Akka 1.5.x ссылается на старый `System.Security.Cryptography.Xml`, `Akka.Cluster` имеет известный CVE — оба не имеют прямого фикса в pinned версиях).
- `NU1608` — `Akka.Persistence.PostgreSql` ограничивает Npgsql `<= 9.0.0`, но мы используем 10.0.0; работает корректно.
- `IDE0008` (var → explicit) и подобные style-only диагностики через `.editorconfig` (TreatWarningsAsErrors превращало бы их в build errors).

Не снимать эти подавления без явного апгрейда соответствующего пакета.

## Akka package version pin

Все Akka.* зависимости пин-нуты в `Directory.Packages.props` на `1.5.37`, потому что `Akka.Persistence.PostgreSql.Hosting` ещё не выпущен на 1.5.49+. Когда обновлять — двигать **все** Akka.* версии разом.

## Where things live

- HOCON для journal event-adapters: `Application/Configuration/AkkaHoconBuilder.cs`.
- Per-user / system настройки и их валидация: `Domain/ValueObjects/SettingsKey.cs` + §8.3 ТЗ.
- Маппинг категория → бакет (default + override): интерфейсы `ICategoryBucketMap`, `IBucketMappingOverrides` в Domain; реализации появятся на Stage 9–10.
- Whitelist логика: `AccessControlActor` + проекция `app.whitelist` (на Stage 6).
- Locale: тексты ответов бота — русский (`TelegramReplies.cs`); комментарии — русский; identifiers и log messages — английский.

# FinanceBot

Telegram-бот для отслеживания личных финансов (50/25/25, Akka.NET cluster, Claude).

Стек: .NET 10, C# 14, Akka.NET 1.5, Akka.Persistence.PostgreSql, EF Core 10, PostgreSQL 17, Polly, ScottPlot, Telegram.Bot, Anthropic.SDK.

## Структура решения

```
src/
  FinanceBot.Domain          — доменная модель (events / commands / VOs / interfaces)
  FinanceBot.Application     — акторы, проекции, конфигурация Akka
  FinanceBot.Infrastructure  — EF Core, ClaudeClient, ChartRenderer, IsDayOff, health-checks
  FinanceBot.Host            — composition root, Program.cs, appsettings.json
docker/
  Dockerfile
  docker-compose.yml             — single-node dev
  docker-compose.multi-node.yml  — два бота + Postgres (для тестирования cluster)
```

## Локальный запуск

### Через docker-compose (рекомендуется)

```bash
cp .env.example .env
# отредактировать .env — заполнить Telegram__BotToken, Auth__AdminUserIds__0, и т.д.
docker compose -f docker/docker-compose.yml up -d
```

### Через IDE с локальным Postgres

```bash
docker compose -f docker/docker-compose.yml up -d postgres
dotnet run --project src/FinanceBot.Host
```

Секреты (`Telegram__BotToken`, `Claude__ApiKey`, `Auth__AdminUserIds__0`) берутся из переменных окружения / user-secrets.

## Multi-node / production

### Multi-node compose (локально)

```bash
docker compose -f docker/docker-compose.multi-node.yml up -d
# bot1 — :8081  (polling)
# bot2 — :8082  (webhook, должен иметь публичный Telegram__WebhookUrl)
```

Оба ноды поднимают `akka.tcp://financebot@bot1:4053` как seed; bot2 присоединяется. Singletons (AccessControl, Scheduler, Claude, projections) живут на одном из нодов, при падении — переезжают.

### Webhook mode

```
Telegram__Mode=Webhook
Telegram__WebhookUrl=https://your.domain/telegram/webhook
```

Endpoint `/telegram/webhook` доступен в Host'е при `Telegram:Mode=Webhook`; Telegram должен иметь возможность сделать HTTPS POST на этот URL. Webhook регистрируется автоматически при старте через `TelegramWebhookSetupService`.

### Health checks

- `GET /health` → 200 если Postgres достижим и Akka cluster здоров; 503 при сбоях.
  - `postgres` — `AppDbContext.CanConnectAsync()`.
  - `akka-cluster` — member count > 0, нет unreachable нод.

### Graceful shutdown

`GracefulClusterShutdownService` ловит SIGTERM (через `IHostApplicationLifetime`), запускает `CoordinatedShutdown` с reason=ClusterLeaving и ждёт до 30 секунд завершения. Шарды и singletons перебалансируются на оставшиеся ноды.

### Логи / observability

- Структурированные логи через `Microsoft.Extensions.Logging` + SimpleConsole.
- Каждый Telegram update получает `CorrelationId` (Guid), логируется в `TelegramGatewayActor`.
- Sensitive data: telegram_id ок в логах; описания трат — лучше избегать (PII).

### Kubernetes

Akka.Discovery.KubernetesApi прикручен (1.5.37). Для cluster bootstrap в k8s — настроить:
- `Akka:Discovery:Method=kubernetes-api`
- `kubernetes-api.pod-namespace=<namespace>`
- `kubernetes-api.pod-label-selector="app=financebot"`

Манифесты Deployment / Service / ConfigMap / Secret не входят в репозиторий — добавьте по своим стандартам.

## Конфигурация

Все настройки в `src/FinanceBot.Host/appsettings.json`. Секреты — только через ENV (см. `.env.example`).

Per-user настройки (timezone, evening_time, allocation, и т.д.) задаются через `/settings` в Telegram, хранятся в `app.users.settings_json`.

## Миграции БД

Миграции `app`-схемы применяются автоматически на старте бота (`dbContext.Database.MigrateAsync()`). Схема `akka` создаётся `Akka.Persistence.PostgreSql` через `auto-initialize=true`.

Создать новую миграцию:

```bash
dotnet ef migrations add <Name> \
  --project src/FinanceBot.Infrastructure \
  --startup-project src/FinanceBot.Infrastructure \
  --context AppDbContext \
  --output-dir Persistence/Migrations
```

## Тесты

```bash
dotnet test
```

## Этапы разработки

См. `task.md` — полное ТЗ. Реализованы все этапы 1–23:

| Stage | Что реализовано |
|---|---|
| 1–4 | Foundation: solution / domain / persistence / actor skeleton |
| 5 | /start, /help, /whoami, /cancel + регистрация |
| 6 | Admin: /adduser, /removeuser, /listusers |
| 7 | /settings |
| 8 | /income + period + аллокация 50/25/25 |
| 9 | /expense, /expense_day, free-text парсинг |
| 10 | Категоризатор: локальные правила |
| 11 | Memory категоризации + /correct |
| 12 | Claude integration + state machine Available/Unavailable |
| 13 | /template — recurring |
| 14 | /plan — planned expenses |
| 15 | /savings + auto-close period |
| 16 | SchedulerActor + heartbeat + IsDayOff + ClaudeAutoRecoveryTick |
| 17 | Вечерний FSM, EveningTick, SilenceDeadline |
| 18 | Wakeup при детекте простоя |
| 19 | Advisor: /advice, park-and-refresh при недоступности Claude |
| 20 | Графики: ScottPlot 5 + RoundRobinPool, /chart |
| 21 | /report [current\|previous\|N] — текстовые отчёты |
| 22 | Multi-node compose + webhook + Akka.Discovery.KubernetesApi |
| 23 | /health (Akka cluster + Postgres), graceful shutdown, correlation IDs |

# FinanceBot

Telegram-бот для отслеживания личных финансов (50/25/25, Akka.NET cluster, Claude).

Стек: .NET 10, C# 14, Akka.NET, Akka.Persistence.PostgreSql, EF Core 10, PostgreSQL 17, Polly, ScottPlot, Telegram.Bot, Anthropic.SDK.

## Структура решения

```
src/
  FinanceBot.Domain          — доменная модель (events / commands / VOs / interfaces)
  FinanceBot.Application     — акторы, проекции, конфигурация Akka
  FinanceBot.Infrastructure  — EF Core, ClaudeClient, ChartRenderer, IsDayOff
  FinanceBot.Host            — composition root, Program.cs, appsettings.json
docker/
  Dockerfile, docker-compose.yml
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

## Конфигурация

Все настройки в `src/FinanceBot.Host/appsettings.json`. Секреты — только через ENV (см. `.env.example`).

## Миграции БД

Миграции `app`-схемы применяются автоматически на старте бота (`dbContext.Database.MigrateAsync()`). Схема `akka` создаётся `Akka.Persistence.PostgreSql` через `auto-initialize=true`.

Создать новую миграцию:

```bash
dotnet ef migrations add <Name> \
  --project src/FinanceBot.Infrastructure \
  --startup-project src/FinanceBot.Host \
  --context AppDbContext \
  --output-dir Persistence/Migrations
```

## Тесты

```bash
dotnet test
```

## Этапы разработки

См. `task.md` — полное ТЗ. Реализованы Stage 1–5 (foundation + регистрация).

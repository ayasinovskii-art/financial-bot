# Design: Bank-statement import (screenshot + CSV)

**Date:** 2026-06-16
**Author:** Claude (Opus 4.8) + ayasinovskii-art
**Status:** Approved (owner waived the review gate — "agent mode")
**Issue:** #17

## Context

A user sends the bot a bank-statement **screenshot** (photo) or **CSV** export. The bot recognises
the transactions, shows a summary with **inline buttons** to confirm, and on confirmation bulk-imports
them as expenses/incomes — reusing the existing per-expense categorisation.

Decisions (brainstorming 2026-06-16):
- **Full #17 scope**, delivered in 2 phases — **P1 screenshot** (builds the shared core), **P2 CSV**.
- **Confirm via inline buttons.** The inline-button infra already exists end-to-end
  (`ITelegramBot.SendInlineKeyboardAsync`/`AnswerCallbackAsync`, `TelegramReplyDispatcher`,
  gateway `IncomingCallbackQuery` → `ITelegramCallbackHandler`, `CorrectHandler` precedent). The
  `Depends-on: #18` marker on the issue is stale — no blocking dependency.
- **Reuse existing categorisation** — each recognised line goes through the normal `ReportExpense`
  path so the rules/Claude category logic and `ExpenseAccepted` flow are unchanged.
- **Expenses + incomes** — debit lines → expense, credit lines → income.
- **Dedup** — a line whose (date, amount, description) already exists is skipped and reported.
- **Partial-failure reporting** — the summary reports imported / skipped-duplicate / failed counts.

## What already exists (reused, not rebuilt)
- Inline keyboards + callbacks (send + receive + route).
- `ITelegramBot.SendPhoto/SendDocument` (outgoing); `ExpenseShared.Dispatch` → `ReportExpense`
  (single add + categorise + `ExpenseAccepted`); `FreeTextHandler` already fans one text message
  into multiple `ReportExpense` dispatches (the bulk pattern).
- `IClaudeClient`/`ClaudeClient` (HTTP + Polly + rate-limit parsing + `ClaudeResponse` failure model);
  `ExtractText` already reads a content-block array response.

## Gaps to fill
1. **Receive** photos/documents — `TelegramUpdateConverter` only reads `message.Text` today.
2. **Download** file bytes — `ITelegramBot` has no `GetFile`/download.
3. **Vision** — `ClaudeRequest` is text-only; needs an optional image content block.
4. **Bulk import + confirm state** — new commands/flow + a per-user pending proposal.

## Architecture

### Domain (BCL only)
- `ImportedTransaction(DateOnly Date, decimal Amount, string Description, TransactionKind Kind)`;
  `enum TransactionKind { Expense, Income }`.
- `ExpenseSource.Import = 8`; `ClaudeUseCase.StatementExtraction = 3`.
- `ClaudeRequest` gains optional `ClaudeImage? Image` (`string MediaType`, `string Base64Data`);
  text-only callers unaffected.
- `IStatementExtractor` — `Task<StatementExtractionResult> ExtractAsync(ReadOnlyMemory<byte> image, string mediaType, CancellationToken)` (P1 = vision).
- `IStatementCsvParser` (P2) — bytes → transactions; per-bank implementations + format detection.
- Commands: `ProposeStatementImport(UserId, Guid ProposalId, IReadOnlyList<ImportedTransaction>)`,
  `ConfirmStatementImport(UserId, Guid ProposalId)`, `CancelStatementImport(UserId)`.
- Event: `StatementImported(UserId, int Imported, int SkippedDuplicates, int Failed, decimal ExpenseTotal, decimal IncomeTotal, DateTimeOffset OccurredAt)`.
  Individual lines still emit the existing `ExpenseReported`/income events.

### Application (Akka)
- `IncomingTelegramFile(UpdateId, ChatId, TelegramId, Username, FirstName, LastName, string FileId, FileKind Kind, string? MimeType, string? Caption, SentAt)`; `enum FileKind { Photo, Document }`.
- **Gateway** routes `IncomingTelegramFile` (post access-check) to a per-node `StatementImportActor`.
- **`StatementImportActor`** (per-node service, mirrors Categorizer/Advisor pattern): downloads bytes
  via `ITelegramBot.DownloadFileAsync`, picks the extractor (vision for `Photo`; CSV parser for
  `Document` in P2), then `Ask`s the UserActor shard `ProposeStatementImport`; on the proposal reply
  publishes an `OutgoingInlineKeyboard` (`Импортировать N` / `Список` / `Отмена`, callback data
  `import:confirm:<proposalId>` etc.).
- **UserActor** holds **transient** `PendingImport(ProposalId, transactions, createdAt)` FSM state
  (not journaled — a confirmation dialog; if the node restarts mid-confirm the user re-sends, cheap
  vs. journaling every proposal). On `ConfirmStatementImport`: validate proposalId, for each line
  dedup against recent expenses/incomes then categorise+emit via the existing path, emit a
  `StatementImported` summary, clear pending, reply aggregate. `CancelStatementImport`/a newer
  proposal clears it.
- **`StatementImportCallbackHandler : ITelegramCallbackHandler`** (`DataPrefix "import:"`) →
  confirm/cancel/list → `Ask` UserActor → publish reply + `OutgoingCallbackAck`.

### Infrastructure
- `TelegramUpdateConverter` captures `message.Photo` (largest `PhotoSize`) and `message.Document`
  → `IncomingTelegramFile` (caption preserved); `TelegramPollResult` carries files too.
- `TelegramBotAdapter.DownloadFileAsync(fileId)` → `(byte[] Bytes, string MimeType)` via
  `GetFile` + `DownloadFile`.
- `ClaudeClient.BuildHttpRequest`: when `request.Image` is set, emit
  `content: [{type:image, source:{type:base64, media_type, data}}, {type:text, text:UserPrompt}]`.
- `ClaudeStatementExtractor : IStatementExtractor` — vision request with a strict JSON-list system
  prompt; parses `[{date, amount, description, kind}]`, tolerant of fenced/garbled output.
- CSV parsers (P2): Tinkoff first, then Sber/Alfa; header-based format detection.

### Host
- Polling/webhook routes `IncomingTelegramFile` to the gateway (alongside updates/callbacks).

## Data flow (screenshot, P1)
photo → adapter → `IncomingTelegramFile` → gateway (access) → `StatementImportActor` → download bytes
→ `IStatementExtractor` (Claude vision) → `ProposeStatementImport` → UserActor stores `PendingImport`
+ handler sends inline keyboard → user taps **Импортировать** → `IncomingCallbackQuery` →
`StatementImportCallbackHandler` → `ConfirmStatementImport` → UserActor dedup+categorise+emit per line
+ `StatementImported` → reply "Импортировано 10, пропущено 2 дубля, ошибок 0".

## Error handling
- Claude unavailable / timeout / 429 → reuse `ClaudeUnavailabilityReason` → "Не смог распознать выписку, попробуй позже".
- No transactions found / unparseable → "Не нашёл транзакций на изображении".
- Stale proposal (old keyboard tapped, proposalId mismatch / no pending) → "Этот импорт уже неактуален, пришли скрин заново".
- Partial failures → reported in the `StatementImported` summary (imported/skipped/failed).
- Empty-token / stub adapter (no BotToken) → NOOP downloads, logged (dev/CI safe), как и сейчас.

## Phasing
- **P1 (first PR):** ingestion (photo+document plumbing), vision extractor, propose/confirm/cancel,
  dedup, categorise-reuse, expenses+incomes, inline confirm, aggregate reply. Document ingestion lands
  in P1 (cheap) so P2 only adds parsers.
- **P2 (second PR):** `IStatementCsvParser` + Tinkoff/Sber/Alfa + format detection, plugged into the
  same propose/confirm/import core.

## Testing
- **Domain:** `ImportedTransaction`, dedup predicate (pure); CSV parsers with sample fixtures (P2).
- **Application (Akka.TestKit):** UserActor propose→confirm→aggregate, dedup, cancel, stale-proposal;
  `StatementImportActor` orchestration with a fake `IStatementExtractor`/`ITelegramBot`; callback handler.
- **Infrastructure:** `TelegramUpdateConverter` photo/document; `ClaudeClient.BuildHttpRequest` image JSON
  shape; `ClaudeStatementExtractor` response parsing (happy + garbled + empty).
- Coverage stays ≥ 0.45 ratchet; Release build is 0-warning (`TreatWarningsAsErrors`).

## Out of scope (v1)
- Image downscaling / multi-page stitching; per-line editing before import; post-import undo
  (use existing `DeleteExpense`); non-RU bank layouts beyond Sber/Tinkoff/Alfa.

## Risks & mitigations
| Risk | Mitigation |
|------|------------|
| Vision misreads amounts/dates | Show full list on **Список** before confirm; dedup guard; user can `DeleteExpense` after. |
| Large bulk categorisation cost | Rules-first categorisation (existing) keeps most lines off Claude; only ambiguous lines call it. |
| Transient proposal lost on restart | Acceptable — re-send photo; documented; avoids journaling every proposal. |
| Claude image payload size | Telegram compresses photos; reject oversized; downscale deferred. |

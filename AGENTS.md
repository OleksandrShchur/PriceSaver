# PriceSaver — agent context

PriceSaver is a Telegram bot + ASP.NET Core Web API (`.NET 9`) with a React/TypeScript Vite client. Users paste a product URL; the bot tracks the price and notifies them when it changes (drops by default).

Repo: https://github.com/OleksandrShchur/PriceSaver

## Layout

| Path | Role |
| --- | --- |
| `PriceSaver.Server/` | API, Telegram bot, parsers, EF Core, jobs |
| `PriceSaver.Server.Tests/` | xUnit + FluentAssertions |
| `pricesaver.client/` | React + TypeScript + Vite SPA |
| `PriceSaver.sln` | Solution |
| `.cursor/skills/add-store/SKILL.md` | How to add a new retailer |

## Stack (server)

- ASP.NET Core, EF Core + SQL Server (`ApplicationDbContext`)
- Telegram webhook: `POST /api/telegram` → `ITelegramUpdateHandler` → `ISubscriptionHandler`
- Price checks: `PriceCheckerService.CheckAllAsync()` or `POST /api/jobs/check-prices` (`X-Api-Key`)
- Models: `User`, `Subscription`, `PriceHistory`, `StoreType`
- Config: `TelegramOptions`, `JobsOptions` (validate on start). Secrets stay in config / user secrets — never hardcode.

## Stores and parsers

Supported today: **ATB**, **Silpo**, **Maudau**, **METRO**. Parsers live in `PriceSaver.Server/Parsers/` and implement `IPriceParser` (`StoreKey`, `StoreType`, `CanParse`, `ParseAsync`). They are registered in `Program.cs` via `AddPriceParserHttpClient<TParser>` (typed client named after the concrete type, then exposed as `IPriceParser`) and resolved by URL via `CanParse`. Do not use `AddHttpClient<IPriceParser, T>` — all parsers would share one HttpClient name and single-value headers such as `Referer` collide.

**Adding a store is a multi-file change.** Follow [`.cursor/skills/add-store/SKILL.md`](.cursor/skills/add-store/SKILL.md) — enum, parser, DI, `InferStoreType`, Telegram instructions/welcome copy, docs, and tests. Skipping `SubscriptionService.InferStoreType` leaves new subscriptions as `Unknown`.

## Conventions

- Propagate `CancellationToken`; keep existing DI lifetimes.
- User-facing Telegram copy is Ukrainian; parser/log messages may be English.
- Tests must be offline (`StubHttpMessageHandler`). Do not call live shop sites.
- After model/schema changes, add an EF Core migration and keep `PriceSaver.Server/docs/sql/schema.sql` aligned. A new `StoreType` enum member does **not** need a migration.
- Do not expand scope (refactors, frontend) unless the task requires it.

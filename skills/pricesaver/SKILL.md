---
name: pricesaver
description: Build, extend, and operate PriceSaver — a Telegram bot + ASP.NET Core Web API and React/TypeScript app that tracks product prices across online stores and notifies users when prices drop.
---

# PriceSaver

PriceSaver is a price-tracking system that lets users subscribe to products through a **Telegram bot**, periodically re-checks prices on supported stores using pluggable parsers, and notifies subscribers when a price changes (drops by default).

Repository: https://github.com/OleksandrShchur/PriceSaver

## When to use this skill

Use this skill whenever the task involves the PriceSaver codebase or any of its core domains:

- Adding or fixing a **store price parser** (e.g. ATB, Silpo, Maudau, Metro, or a new retailer). For a full add-store checklist, also follow [`.cursor/skills/add-store/SKILL.md`](../../.cursor/skills/add-store/SKILL.md).
- Working on the **Telegram bot** — webhook/long-polling, commands, message handlers, subscription flows.
- Managing **subscriptions** (create, list, deactivate, notify-on-increase) and **users**.
- Running or scheduling **price checks** (background jobs, manual triggers, notifications).
- Modifying **EF Core** models, `ApplicationDbContext`, migrations, or the SQL schema.
- Building or wiring **Web API endpoints** for jobs, Telegram updates, or subscription management.
- Frontend work on the **React + TypeScript + Vite** client (`pricesaver.client`).

Do **not** use this skill for unrelated .NET/React projects.

## Capabilities

This skill can:

- **Create new price parsers** implementing `IPriceParser` (`StoreKey`, `StoreType`, `CanParse(url)`, `ParseAsync(url, ct)`) and register them via `AddPriceParserHttpClient<TParser>`.
- **Extend store support** by adding a `StoreType` enum value, `InferStoreType` mapping, Telegram copy, and docs (see add-store skill).
- **Implement Telegram bot behavior** — handle `Update` payloads, parse commands, drive subscription conversations, and send notifications via `ITelegramService`.
- **Manage subscriptions** through `ISubscriptionService` / `SubscriptionHandler` (add a product URL, list active subscriptions, toggle `IsActive` / `NotifyOnIncrease`).
- **Run price checks** via `PriceCheckerService.CheckAllAsync()`, triggered by the scheduled job or the `POST /api/jobs/check-prices` endpoint.
- **Persist data** with EF Core (`Subscription`, `User`, `PriceHistory`) and keep `docs/sql/schema.sql` aligned with model changes.
- **Expose and secure endpoints**, including the API-key–protected jobs endpoint.
- **Generate price reports / histories** from `PriceHistory` records.
- **Work on the React client** for displaying subscriptions and price data.

## Triggers

Activate this skill on phrases or files such as:

- "add a parser for `<store>`", "the Silpo/ATB/Metro/Maudau price is wrong", "scraper returns wrong price".
- "track this product", "subscribe to a price", "notify me when the price drops".
- "the Telegram bot doesn't respond", "handle a new bot command", "set up the webhook".
- "run the price check job", "schedule price checks", "trigger a manual check".
- "add a migration", "update the DB schema", "change the Subscription model".
- File patterns:
  - `PriceSaver.Server/Parsers/*PriceParser.cs`, `IPriceParser.cs`
  - `PriceSaver.Server/Handlers/*Handler.cs`
  - `PriceSaver.Server/Services/*Service.cs`
  - `PriceSaver.Server/Controllers/{Telegram,Jobs}Controller.cs`
  - `PriceSaver.Server/Models/{Subscription,User,PriceHistory,StoreType}.cs`
  - `PriceSaver.Server/Data/ApplicationDbContext.cs`, `docs/sql/schema.sql`
  - `pricesaver.client/src/**/*.tsx`

## Usage examples

- "Add a price parser for Auchan and make products from their site trackable."
- "The ATB parser returns the wrong price when the card-price label is missing — fix the regex fallback."
- "Add a `/list` Telegram command that shows all of a user's active subscriptions with current prices."
- "Trigger a manual price check for all subscriptions and notify users whose prices dropped."
- "Add a `NotifyOnIncrease` toggle to the subscription flow and persist it."
- "Create an EF Core migration after adding a `TargetPrice` field to `Subscription`."
- "Build a React page that lists subscriptions and their latest price history."

## Implementation notes

**Architecture**
- Backend: `PriceSaver.Server` (ASP.NET Core, .NET 9). Frontend: `pricesaver.client` (React + TypeScript + Vite). Solution: `PriceSaver.sln`.
- Telegram updates arrive at `POST /api/telegram` (webhook) and flow into `ITelegramUpdateHandler` → `ISubscriptionHandler`. Long polling is driven by the `TelegramService` hosted singleton.

**Parsers (`PriceSaver.Server/Parsers`)**
- All parsers implement `IPriceParser`:
  - `string StoreKey` — stable store identifier (e.g. `"atb"`).
  - `StoreType StoreType` — enum value (persisted only via `InferStoreType(StoreKey)`).
  - `bool CanParse(string url)` — host-based URL matching.
  - `Task<(string Name, decimal Price)> ParseAsync(string url, CancellationToken ct)`.
- Register new parsers with `builder.Services.AddPriceParserHttpClient<TParser>(...)`. Do **not** use `AddHttpClient<IPriceParser, T>` (shared client name / header collisions) or bare `AddSingleton<IPriceParser, T>` when a typed `HttpClient` is required.
- Some parsers (e.g. ATB) fetch page text via the Jina Reader proxy (`https://r.jina.ai/`); note that Jina returns HTTP 200 even for upstream 404s, so check the body for error markers.
- Currently registered parsers: `AtbPriceParser`, `SilpoPriceParser`, `MaudauPriceParser`, `MetroPriceParser`.

**Store types**
- Supported today: **ATB**, **Silpo**, **Maudau**, **METRO**.
- `StoreType` enum (`Models/StoreType.cs`) uses `[Description]` for localized display names. Add a new value when introducing a new retailer and keep it in sync with the matching parser, `InferStoreType`, Telegram instructions, and docs.

**Data model (EF Core)**
- `Subscription`: `Id (Guid)`, `UserId (long)`, `ProductUrl`, `StoreType`, `ProductName?`, `CurrentPrice (decimal)`, `LastCheckedDate?`, `IsActive`, `NotifyOnIncrease`, `CreatedAt`.
- Persistence via `ApplicationDbContext` over SQL Server (`DefaultConnection`). Keep `docs/sql/schema.sql` consistent with model/migration changes.

**Jobs & notifications**
- `PriceCheckerService.CheckAllAsync()` iterates active subscriptions, resolves the matching parser, re-parses, updates `CurrentPrice`/`LastCheckedDate`, records history, and notifies on price change.
- Manual trigger: `POST /api/jobs/check-prices` guarded by the `X-Api-Key` header compared against `JobsOptions.SecretKey`.

**Configuration**
- `TelegramOptions` and `JobsOptions` are bound from configuration with data-annotation validation and `ValidateOnStart`. Telegram bot token and the jobs secret key live in `appsettings.json` / environment / user secrets — never hardcode secrets.

**Conventions**
- Follow existing DI registration patterns and async/`CancellationToken` propagation.
- User-facing Telegram copy is Ukrainian; validate inputs at boundaries; avoid leaking parser/network errors to users.
- After model changes, add an EF Core migration and update `docs/sql/schema.sql`.

PriceSaver.Server
=================

Backend service for PriceSaver: ASP.NET Core Web API + Telegram bot.

Quick setup

- Configure `ConnectionStrings:DefaultConnection`, `Telegram`, `Jobs`, and `Nominatim` in `appsettings.json`.
- Run EF Core migrations or create DB from schema in `docs/sql/schema.sql` (apply incremental scripts under `docs/sql/` if upgrading an existing DB).
- Restore packages and run the project.

Endpoints

- POST `/api/telegram/callback` with Telegram update JSON - receives Telegram webhook callbacks.

- POST `/api/jobs/check-prices` with header `X-Api-Key: <secret>` — triggers price checks.
- GET `/api/subscriptions/user/{telegramId}` — list user subscriptions.
- DELETE `/api/subscriptions/{id}` — deactivate subscription.

Design notes

- Parsers are pluggable: implement `IPriceParser` and register with `AddPriceParserHttpClient<TParser>` in `Program.cs`.
- Supported retailers today: ATB, Silpo, Maudau, METRO. To add another, follow [`.cursor/skills/add-store/SKILL.md`](../.cursor/skills/add-store/SKILL.md).
- Telegram updates are processed by `ITelegramUpdateHandler`; webhook mode is the default, and long polling can be enabled with `Telegram:EnablePolling`.
- New users must set a location after `/start` (typed place name via OSM Nominatim, or Telegram location share) before subscriptions/URLs work; instructions remain available.
- `PriceCheckerService` performs daily checks and notifies users.

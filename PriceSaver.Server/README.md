PriceSaver.Server
=================

Backend service for PriceSaver: ASP.NET Core Web API + Telegram bot.

Quick setup

- Configure `ConnectionStrings:DefaultConnection`, `Telegram`, and `Jobs` in `appsettings.json`.
- Run EF Core migrations or create DB from schema in `docs/sql/schema.sql`.
- Restore packages and run the project.

Endpoints

- POST `/api/telegram/callback` with Telegram update JSON - receives Telegram webhook callbacks.

- POST `/api/jobs/check-prices` with header `X-Api-Key: <secret>` — triggers price checks.
- GET `/api/subscriptions/user/{telegramId}` — list user subscriptions.
- DELETE `/api/subscriptions/{id}` — deactivate subscription.

Design notes

- Parsers are pluggable: implement `IPriceParser` and register in DI.
- Telegram updates are processed by `ITelegramUpdateHandler`; webhook mode is the default, and long polling can be enabled with `Telegram:EnablePolling`.
- `PriceCheckerService` performs daily checks and notifies users.

PriceSaver.Server
=================

Backend service for PriceSaver: ASP.NET Core Web API + Telegram bot.

Quick setup

- Add connection string `DefaultConnection` to `appsettings.json`.
- Add `Telegram:BotToken` and `Jobs:SecretKey` to `appsettings.json`.
- Run EF Core migrations or create DB from schema in `docs/sql/schema.sql`.
- Restore packages and run the project.

Endpoints

- POST `/api/jobs/check-prices` with header `X-Api-Key: <secret>` — triggers price checks.
- GET `/api/subscriptions/user/{telegramId}` — list user subscriptions.
- DELETE `/api/subscriptions/{id}` — deactivate subscription.

Design notes

- Parsers are pluggable: implement `IPriceParser` and register in DI.
- `TelegramBotHostedService` runs a long-polling bot that handles `/start`, `/my_subscriptions`, and links.
- `PriceCheckerService` performs daily checks and notifies users.

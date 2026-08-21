# PriceSaver

[![CI](https://github.com/OleksandrShchur/PriceSaver/actions/workflows/ci.yml/badge.svg)](https://github.com/OleksandrShchur/PriceSaver/actions/workflows/ci.yml)

Telegram price-tracking bot: paste a product URL, track the price, and get notified when it changes. Built with ASP.NET Core (.NET 9) and a React/TypeScript client.

**Supported stores:** ATB, Silpo, Maudau, METRO.

| Path | Role |
| --- | --- |
| [`PriceSaver.Server/`](PriceSaver.Server/) | API, Telegram bot, parsers, EF Core, jobs |
| [`PriceSaver.Server.Tests/`](PriceSaver.Server.Tests/) | xUnit tests |
| [`pricesaver.client/`](pricesaver.client/) | React + Vite SPA |
| [`.cursor/skills/add-store/SKILL.md`](.cursor/skills/add-store/SKILL.md) | How to add a new retailer |

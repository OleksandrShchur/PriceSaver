---
name: add-store
description: >-
  Adds a new retailer to PriceSaver by creating a StoreType value, IPriceParser,
  DI registration, StoreKey mapping, and tests. Use when adding a store, shop,
  retailer, price parser, scraper, or when the user says "add a parser for",
  "support this site", or names a new market (ATB, Silpo, Maudau, or another).
---

# Add a store

A "store" in PriceSaver is a retailer whose product URLs can be subscribed to. Support is **not** just a parser class: enum, `StoreKey` mapping, DI, and tests must stay in sync.

Do not add frontend or database-migration work for a new store. `StoreType` is stored as an int; a new enum member needs no schema change.

## Current stores

| Store | `StoreKey` | `StoreType` | Hosts | Fetch style |
| --- | --- | --- | --- | --- |
| ATB | `atb` | `ATB` | `atbmarket.com`, `atbmarket.ua`, `atb.ua` | Jina Reader HTML + regex |
| Silpo | `silpo` | `Silpo` | `silpo.ua` | Product JSON API |
| Maudau | `maudau` | `Maudau` | `maudau.com.ua` | Product JSON API |

`StoreType.Unknown` has no parser. Insert new enum members **before** `Unknown`.

## Checklist

Copy and track:

```
- [ ] StoreType enum + [Description] (Ukrainian display name)
- [ ] IPriceParser implementation
- [ ] Program.cs AddHttpClient<IPriceParser, TParser>
- [ ] SubscriptionService.InferStoreType switch (required; parser.StoreType is not enough)
- [ ] Parser unit tests (CanParse + ParseAsync success/failure)
- [ ] StoreTypeEnumExtensionsTests InlineData
- [ ] SubscriptionServiceTests InferStoreType InlineData
```

## Workflow

**Ask first** if missing: store name, product URL example(s), www + non-www hosts, and whether a public product API exists.

### 1. Enum

In `PriceSaver.Server/Models/StoreType.cs` add a PascalCase member with `[Description("…")]`. That string is what Telegram shows via `GetDescription()`.

### 2. Parser

Create `PriceSaver.Server/Parsers/{Name}PriceParser.cs` implementing `IPriceParser`:

- `StoreKey` — lowercase, no spaces (`"silpo"`). Must match the `InferStoreType` case exactly.
- `StoreType` — the new enum value.
- `CanParse(url)` — `Uri.TryCreate` + case-insensitive host match, including `www`. Return false for garbage URLs; never throw.
- `ParseAsync` — return `(Name, Price)`. Price is UAH `decimal`. Prefer the price the shop displays (card / `priceToShow` / discount) over the strikethrough regular price.

**Fetch style (pick one):**

- Prefer a product JSON API (copy `SilpoPriceParser` / `MaudauPriceParser`).
- If there is no API, scrape HTML. ATB uses Jina Reader (`https://r.jina.ai/{url}`). Jina returns HTTP 200 for upstream 404s — detect error markers in the body.

**HTTP:** inject `HttpClient` and `ILogger<TParser>` (ATB/Silpo pattern). Register with `AddHttpClient<IPriceParser, TParser>`. If the API needs headers or gzip, configure the client like Silpo/Maudau (`Timeout` 15s, User-Agent, `AutomaticDecompression`).

**Errors:** throw the public `PriceParseException` for expected failures (bad slug, 404, missing price). Do not nest a private `PriceParseException`. Parser exceptions are logged and mapped to `CreateSubscriptionStatus.ParseFailed`; they are not shown to Telegram users.

**Do not** change `ProductUrlNormalizer` unless this store's identity lives in the query string (normalizer already lowercases host, strips fragment and trailing slash).

### 3. DI

In `PriceSaver.Server/Program.cs`, next to the other parsers:

```csharp
builder.Services.AddHttpClient<IPriceParser, {Name}PriceParser>(...);
```

Must be `AddHttpClient<IPriceParser, T>` so `IEnumerable<IPriceParser>` in `SubscriptionService` and `PriceCheckerService` picks it up. Resolution is `parsers.FirstOrDefault(p => p.CanParse(url))`.

### 4. StoreKey mapping (easy to miss)

`IPriceParser.StoreType` is **not** what gets persisted. `SubscriptionService.CreateSubscriptionAsync` calls `InferStoreType(parser.StoreKey)`. Add a case or new subscriptions land as `Unknown` and price-check grouping breaks.

```csharp
private static StoreType InferStoreType(string key) => key.ToLowerInvariant() switch
{
    "atb" => StoreType.ATB,
    "silpo" => StoreType.Silpo,
    "maudau" => StoreType.Maudau,
    "newstore" => StoreType.NewStore, // add here
    _ => StoreType.Unknown
};
```

### 5. Tests

Mirror an existing parser test (`AtbPriceParserTests`, `SilpoPriceParserTests`, or `MaudauPriceParserTests`):

- Build the parser with `StubHttpMessageHandler` + `HttpClient`. Use `NullLogger<T>.Instance` when the parser takes `ILogger<T>`.
- `CanParse`: true for real hosts (with/without `www`), false for other stores and invalid URLs. Assert `StoreKey` and `StoreType`.
- `ParseAsync`: fixture JSON or HTML → expected name and price (comma decimals if the shop uses them).
- Failure: 404 / empty body / missing price throws.

Also add:

- `[InlineData(StoreType.NewStore, "Display name")]` in `StoreTypeEnumExtensionsTests`.
- `[InlineData("newstore", StoreType.NewStore)]` in `CreateSubscriptionAsync_InfersStoreType_FromParserStoreKey`.

Do not hit live shop sites in tests.

## Done when

- A product URL from the new host creates a subscription with the correct `StoreType` (not `Unknown`).
- `dotnet test` passes, including the new parser tests.
- No unrelated refactors (do not "fix" `InferStoreType` to use `parser.StoreType` in the same change unless the user asked).

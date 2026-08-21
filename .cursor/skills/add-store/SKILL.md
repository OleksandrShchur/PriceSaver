---
name: add-store
description: >-
  Adds a new retailer to PriceSaver by creating a StoreType value, IPriceParser,
  DI registration, StoreKey mapping, user-facing Telegram copy, docs, and tests.
  Use when adding a store, shop, retailer, price parser, scraper, or when the user
  says "add a parser for", "support this site", or names a new market (ATB, Silpo,
  Maudau, Metro, or another).
---

# Add a store

A "store" in PriceSaver is a retailer whose product URLs can be subscribed to. Support is **not** just a parser class: enum, `StoreKey` mapping, DI, Telegram copy, docs, and tests must stay in sync.

Do not add frontend or database-migration work for a new store. `StoreType` is stored as an int; a new enum member needs no schema change.

## Current stores

| Store | `StoreKey` | `StoreType` | Hosts | Fetch style |
| --- | --- | --- | --- | --- |
| ATB | `atb` | `ATB` | `atbmarket.com`, `atbmarket.ua`, `atb.ua` | Jina Reader HTML + regex |
| Silpo | `silpo` | `Silpo` | `silpo.ua` | Product JSON API |
| Maudau | `maudau` | `Maudau` | `maudau.com.ua` | Product JSON API |
| Metro | `metro` | `Metro` | `shop.metro.ua` | Product JSON API (`betty-articles`, `details=true`) |

`StoreType.Unknown` has no parser. Insert new enum members **before** `Unknown`.

Registered parsers today: `AtbPriceParser`, `SilpoPriceParser`, `MaudauPriceParser`, `MetroPriceParser`.

## Checklist

Copy and track:

```
Code
- [ ] StoreType enum + [Description] (Ukrainian / display name shown in Telegram)
- [ ] IPriceParser implementation
- [ ] Program.cs AddPriceParserHttpClient<TParser>
- [ ] SubscriptionService.InferStoreType switch (required; parser.StoreType is not enough)
- [ ] Parser unit tests (CanParse + ParseAsync success/failure)
- [ ] StoreTypeEnumExtensionsTests InlineData
- [ ] SubscriptionServiceTests InferStoreType InlineData

User-facing copy (Ukrainian)
- [ ] TelegramUpdateHandler.SendInstructionsAsync — store list + link
- [ ] TelegramUpdateHandler.SendWelcomeMessageAsync — store names in step 1
- [ ] TelegramUpdateHandler non-URL hint — "Надішліть пряме посилання…"

Docs / agent context
- [ ] README.md supported-stores line
- [ ] PriceSaver.Server/README.md design notes (if it lists stores)
- [ ] AGENTS.md "Supported today" list
- [ ] .cursor/skills/add-store/SKILL.md "Current stores" table (this file)
- [ ] .cursor/rules/pricesaver.mdc if it names example stores
- [ ] skills/pricesaver/SKILL.md if present and out of date
```

## Workflow

**Ask first** if missing: store name, product URL example(s), www + non-www hosts, display name for Telegram, and whether a public product API exists.

### 1. Enum

In `PriceSaver.Server/Models/StoreType.cs` add a PascalCase member with `[Description("…")]`. That string is what Telegram shows via `GetDescription()`.

### 2. Parser

Create `PriceSaver.Server/Parsers/{Name}PriceParser.cs` implementing `IPriceParser`:

- `StoreKey` — lowercase, no spaces (`"silpo"`). Must match the `InferStoreType` case exactly.
- `StoreType` — the new enum value.
- `CanParse(url)` — `Uri.TryCreate` + case-insensitive host match, including `www`. Return false for garbage URLs; never throw.
- `ParseAsync` — return `(Name, Price)`. Price is UAH `decimal`. Prefer the price the shop displays (card / `priceToShow` / discount) over the strikethrough regular price.

**Fetch style (pick one):**

- Prefer a product JSON API (copy `SilpoPriceParser` / `MaudauPriceParser` / `MetroPriceParser`).
- If there is no API, scrape HTML. ATB uses Jina Reader (`https://r.jina.ai/{url}`). Jina returns HTTP 200 for upstream 404s — detect error markers in the body.

**HTTP:** inject `HttpClient` and `ILogger<TParser>` (ATB/Silpo pattern). Register with `AddPriceParserHttpClient<TParser>` (see DI below). If the API needs headers or gzip, configure the client like Silpo/Maudau/Metro (`Timeout` 15s, User-Agent, `AutomaticDecompression`).

**Errors:** throw the public `PriceParseException` for expected failures (bad slug, 404, missing price). Do not nest a private `PriceParseException`. Parser exceptions are logged and mapped to `CreateSubscriptionStatus.ParseFailed`; they are not shown to Telegram users.

**Do not** change `ProductUrlNormalizer` unless this store's identity lives in the query string (normalizer already lowercases host, strips fragment and trailing slash).

**Metro pitfalls (reference):** price may live under `stores/{storeId}/possibleDeliveryModes/.../sellingPriceInfo.finalPrice` rather than on the bundle root; call the API with `details=true`; tolerate corrupted URL suffixes by falling back to a prefix-matching bundle key when needed.

### 3. DI

In `PriceSaver.Server/Program.cs`, next to the other parsers:

```csharp
builder.Services.AddPriceParserHttpClient<{Name}PriceParser>(client =>
{
    // optional: Timeout, User-Agent, Origin, Referer
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.All
});
```

Must use `AddPriceParserHttpClient<TParser>` so each parser gets its own named HttpClient **and** `IEnumerable<IPriceParser>` in `SubscriptionService` / `PriceCheckerService` still picks it up. Resolution is `parsers.FirstOrDefault(p => p.CanParse(url))`.

Do **not** use `AddHttpClient<IPriceParser, TParser>`. That names every client `"IPriceParser"`, so configure callbacks share one `HttpClient` and single-value headers such as `Referer` throw `FormatException` (this broke `POST /api/telegram` after Metro was added).

Do **not** register with `AddSingleton<IPriceParser, TParser>` alone when the parser needs a typed `HttpClient`.

### 4. StoreKey mapping (easy to miss)

`IPriceParser.StoreType` is **not** what gets persisted. `SubscriptionService.CreateSubscriptionAsync` calls `InferStoreType(parser.StoreKey)`. Add a case or new subscriptions land as `Unknown` and price-check grouping breaks.

```csharp
private static StoreType InferStoreType(string key) => key.ToLowerInvariant() switch
{
    "atb" => StoreType.ATB,
    "silpo" => StoreType.Silpo,
    "maudau" => StoreType.Maudau,
    "metro" => StoreType.Metro,
    "newstore" => StoreType.NewStore, // add here
    _ => StoreType.Unknown
};
```

### 5. Telegram user-facing copy

Ukrainian strings live in `PriceSaver.Server/Handlers/TelegramUpdateHandler.cs`:

| Method / place | What to update |
| --- | --- |
| `SendInstructionsAsync` | Add `🏪 <a href="…">DisplayName</a>` under **Підтримувані магазини** |
| `SendWelcomeMessageAsync` | Include the store in step 1️⃣ (“Надішліть посилання на продукт з …”) |
| Non-URL message (~line that says “Надішліть пряме посилання”) | Include the store in the supported list |

Keep link text aligned with `[Description]` on `StoreType` when practical (e.g. METRO → `METRO`).

### 6. Docs

Update the supported-store lists in:

- Root [`README.md`](../../../README.md)
- [`PriceSaver.Server/README.md`](../../../PriceSaver.Server/README.md) (if it mentions stores)
- [`AGENTS.md`](../../../AGENTS.md)
- This skill’s **Current stores** table
- [`skills/pricesaver/SKILL.md`](../../../skills/pricesaver/SKILL.md) if it still lists wrong parsers / DI

### 7. Tests

Mirror an existing parser test (`AtbPriceParserTests`, `SilpoPriceParserTests`, `MaudauPriceParserTests`, or `MetroPriceParserTests`):

- Build the parser with `StubHttpMessageHandler` + `HttpClient`. Use `NullLogger<T>.Instance` when the parser takes `ILogger<T>`.
- `CanParse`: true for real hosts (with/without `www`), false for other stores and invalid URLs. Assert `StoreKey` and `StoreType`.
- `ParseAsync`: fixture JSON or HTML → expected name and price (comma decimals if the shop uses them).
- Failure: 404 / empty body / missing price throws.
- If the store uses distinct HttpClient headers (e.g. `Referer`), extend `ParserHttpClientRegistrationTests` so clients do not collide.

Also add:

- `[InlineData(StoreType.NewStore, "Display name")]` in `StoreTypeEnumExtensionsTests`.
- `[InlineData("newstore", StoreType.NewStore)]` in `CreateSubscriptionAsync_InfersStoreType_FromParserStoreKey`.

Do not hit live shop sites in tests.

## Done when

- A product URL from the new host creates a subscription with the correct `StoreType` (not `Unknown`).
- Telegram **Інструкції** / welcome / non-URL hint mention the new store.
- README / AGENTS / this skill list the new store.
- `dotnet test` passes, including the new parser tests.
- No unrelated refactors (do not "fix" `InferStoreType` to use `parser.StoreType` in the same change unless the user asked).

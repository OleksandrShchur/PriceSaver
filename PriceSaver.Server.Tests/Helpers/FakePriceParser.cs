using PriceSaver.Server.Models;
using PriceSaver.Server.Parsers;

namespace PriceSaver.Server.Tests.Helpers
{
    /// <summary>
    /// Configurable in-memory <see cref="IPriceParser"/> used to drive
    /// subscription and price-check logic without touching the network.
    /// </summary>
    public sealed class FakePriceParser : IPriceParser
    {
        public FakePriceParser(
            string storeKey = "atb",
            StoreType storeType = StoreType.ATB,
            Func<string, bool>? canParse = null,
            Func<string, CancellationToken, Task<(string Name, decimal Price)>>? parse = null)
        {
            StoreKey = storeKey;
            StoreType = storeType;
            CanParseFunc = canParse ?? (_ => true);
            ParseFunc = parse ?? ((_, _) => Task.FromResult(("Product", 100m)));
        }

        public string StoreKey { get; }

        public StoreType StoreType { get; }

        /// <summary>Mutable predicate controlling <see cref="CanParse"/>.</summary>
        public Func<string, bool> CanParseFunc { get; set; }

        /// <summary>Mutable factory controlling <see cref="ParseAsync"/> results.</summary>
        public Func<string, CancellationToken, Task<(string Name, decimal Price)>> ParseFunc { get; set; }

        public int ParseCallCount { get; private set; }

        public bool CanParse(string url) => CanParseFunc(url);

        public Task<(string Name, decimal Price)> ParseAsync(string url, CancellationToken ct = default)
        {
            ParseCallCount++;
            return ParseFunc(url, ct);
        }
    }
}

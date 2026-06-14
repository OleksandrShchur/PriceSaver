using PriceSaver.Server.Models;

namespace PriceSaver.Server.Parsers
{
    public interface IPriceParser
    {
        string StoreKey { get; }
        StoreType StoreType { get; }
        bool CanParse(string url);
        Task<(string Name, decimal Price)> ParseAsync(string url, CancellationToken ct = default);
    }
}

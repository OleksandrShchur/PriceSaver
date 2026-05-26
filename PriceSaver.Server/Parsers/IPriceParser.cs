using System.Threading;
using System.Threading.Tasks;

namespace PriceSaver.Server.Parsers
{
    public interface IPriceParser
    {
        string StoreKey { get; }
        bool CanParse(string url);
        Task<(string Name, decimal Price)> ParseAsync(string url, CancellationToken ct = default);
    }
}

using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using PriceSaver.Server.Models;

namespace PriceSaver.Server.Parsers
{
    public class SilpoPriceParser : IPriceParser
    {
        // The "00000…0" branch ID returns the default/online price for any product.
        private const string SilpoApiBase =
            "https://sf-ecom-api.silpo.ua/v1/uk/branches/00000000-0000-0000-0000-000000000000/products/";

        // Matches the product slug at the end of a silpo.ua product URL:
        // https://silpo.ua/product/yogurt-activia-300g-123456
        //                                  ^^^^^^^^^^^^^^^^^^^^
        private static readonly Regex SlugRegex = new(@"/product/(?<slug>[^/?#]+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly HttpClient _http;

        public SilpoPriceParser(HttpClient http)
        {
            _http = http;
        }

        public string StoreKey => "silpo";

        public StoreType StoreType => StoreType.Silpo;

        public bool CanParse(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Host.Equals("silpo.ua", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.silpo.ua", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<(string Name, decimal Price)> ParseAsync(
            string url,
            CancellationToken ct = default)
        {
            var slug = ExtractSlug(url);

            using var json = await FetchProductJsonAsync(slug, url, ct);

            return ParseProduct(json, url);
        }

        private sealed class PriceParseException(string message)
            : Exception(message);

        private string ExtractSlug(string url)
        {
            var match = SlugRegex.Match(url);

            if (!match.Success)
            {
                throw new PriceParseException($"😔 Не вдалося розпізнати посилання на товар Сільпо.\n\n🔗 {url}");
            }

            return match.Groups["slug"].Value;
        }

        private async Task<JsonDocument> FetchProductJsonAsync(
            string slug,
            string originalUrl,
            CancellationToken ct)
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{SilpoApiBase}{slug}");

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new PriceParseException(
                    $"😔 Товар не знайдено — можливо, він більше не продається.\n\n🔗 {originalUrl}");
            }

            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync(ct);

            return await JsonDocument.ParseAsync(
                stream,
                cancellationToken: ct);
        }

        private static (string Name, decimal Price) ParseProduct(
            JsonDocument doc,
            string url)
        {
            var root = doc.RootElement;

            // The API returns the product name in "title" or "name"
            // depending on the endpoint.
            var name =
                root.TryGetProperty("title", out var titleProp)
                    ? titleProp.GetString()
                    : root.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString()
                        : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(
                    "Silpo API: product name not found in response.");
            }

            // Price fields observed in the API:
            //
            // "price"       - regular price
            // "discount"    - discounted price
            // "priceToShow" - actual displayed price (preferred)

            decimal price = 0;

            if (root.TryGetProperty("priceToShow", out var pts)
                && pts.TryGetDecimal(out var p1)
                && p1 > 0)
            {
                price = p1;
            }
            else if (root.TryGetProperty("discount", out var disc)
                     && disc.TryGetDecimal(out var p2)
                     && p2 > 0)
            {
                price = p2;
            }
            else if (root.TryGetProperty("price", out var priceProp)
                     && priceProp.TryGetDecimal(out var p3)
                     && p3 > 0)
            {
                price = p3;
            }

            if (price <= 0)
            {
                throw new InvalidOperationException(
                    $"Silpo API: could not extract a valid price from response for {url}.");
            }

            return (name!, price);
        }
    }
}

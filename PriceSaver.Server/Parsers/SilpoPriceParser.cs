using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceSaver.Server.Parsers
{
    public class SilpoPriceParser : IPriceParser
    {
        // The "00000…0" branch ID returns the default/online price for any product.
        private const string SilpoApiBase =
            "https://sf-ecom-api.silpo.ua/v1/uk/branches/00000000-0000-0000-0000-000000000000/products/";

        // Matches the product slug at the end of a silpo.ua product URL:
        //   https://silpo.ua/product/yogurt-activia-300g-123456
        //                                    ^^^^^^^^^^^^^^^^^^^^
        private static readonly Regex SlugRegex =
            new(@"/product/(?<slug>[^/?#]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly HttpClient _http;

        public SilpoPriceParser()
        {
            _http = CreateHttpClient();
        }

        public string StoreKey => "silpo";

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
            var json = await FetchProductJsonAsync(slug, url, ct);
            return ParseProduct(json, url);
        }

        private sealed class PriceParseException(string message) : Exception(message);

        private string ExtractSlug(string url)
        {
            var match = SlugRegex.Match(url);
            if (!match.Success)
                throw new PriceParseException(
                    $"😔 Не вдалося розпізнати посилання на товар Сільпо.\n\n🔗 {url}");

            return match.Groups["slug"].Value;
        }

        private async Task<JsonDocument> FetchProductJsonAsync(
            string slug, string originalUrl, CancellationToken ct)
        {
            const int MaxAttempts = 5;

            static TimeSpan Delay(int attempt) =>
                TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 10));

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                HttpResponseMessage? response = null;
                try
                {
                    using var request =
                        new HttpRequestMessage(HttpMethod.Get, $"{SilpoApiBase}{slug}");

                    response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new PriceParseException(
                            $"😔 Товар не знайдено — можливо, він більше не продається.\n\n🔗 {originalUrl}");

                    if (IsTransient(response))
                    {
                        if (attempt == MaxAttempts) break;
                        await Task.Delay(Delay(attempt), ct);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    var stream = await response.Content.ReadAsStreamAsync(ct);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                }
                catch (PriceParseException)
                {
                    throw; // don't swallow user-facing errors
                }
                catch (HttpRequestException) when (attempt < MaxAttempts)
                {
                    await Task.Delay(Delay(attempt), ct);
                }
                finally
                {
                    response?.Dispose();
                }
            }

            throw new PriceParseException(
                $"😔 На жаль, не вдалося отримати ціну для товару.\n" +
                $"Спробуй надіслати посилання ще раз — можливо, сайт тимчасово недоступний.\n\n" +
                $"🔗 {originalUrl}");
        }

        private static (string Name, decimal Price) ParseProduct(JsonDocument doc, string url)
        {
            var root = doc.RootElement;

            // The API returns the product name in "title" or "name" depending on the endpoint.
            var name = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString()
                     : root.TryGetProperty("name", out var nameProp) ? nameProp.GetString()
                     : null;

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Silpo API: product name not found in response.");

            // Price fields observed in the API:
            //   "price"          – regular price
            //   "discount"       – discounted price (present & non-zero when on sale)
            //   "priceToShow"    – whichever the customer actually pays (preferred)
            decimal price = 0;

            if (root.TryGetProperty("priceToShow", out var pts) && pts.TryGetDecimal(out var p1) && p1 > 0)
                price = p1;
            else if (root.TryGetProperty("discount", out var disc) && disc.TryGetDecimal(out var p2) && p2 > 0)
                price = p2;
            else if (root.TryGetProperty("price", out var priceProp) && priceProp.TryGetDecimal(out var p3) && p3 > 0)
                price = p3;

            if (price <= 0)
                throw new InvalidOperationException(
                    $"Silpo API: could not extract a valid price from response for {url}.");

            return (name!, price);
        }

        private static bool IsTransient(HttpResponseMessage r) =>
            r.StatusCode is HttpStatusCode.Forbidden
                         or HttpStatusCode.TooManyRequests
                         or HttpStatusCode.ServiceUnavailable
            || (int)r.StatusCode >= 500;

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            };

            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

            // Mimic the browser headers that silpo.ua itself sends to the API.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/125.0.0.0 Safari/537.36");

            http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
            http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk-UA,uk;q=0.9");

            // The silpo.ua SPA sends this; the API may check it.
            http.DefaultRequestHeaders.Add("Origin", "https://silpo.ua");
            http.DefaultRequestHeaders.Add("Referer", "https://silpo.ua/");

            return http;
        }
    }
}

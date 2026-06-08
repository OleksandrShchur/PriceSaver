using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceSaver.Server.Parsers
{
    public class MaudauPriceParser : IPriceParser
    {
        private const string MaudauApiBase =
            "https://backend.prod.maudau.click/v1/user/products/searches";

        // Matches the product slug at the end of a maudau.com.ua product URL:
        // https://maudau.com.ua/product/dzhyn-finsbury-wild-strawberry-375-07-l-878772
        //                               ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
        private static readonly Regex SlugRegex = new(@"/product/(?<slug>[^/?#]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Extracts the trailing numeric product ID from the slug:
        // dzhyn-finsbury-wild-strawberry-375-07-l-878772  →  878772
        private static readonly Regex ProductIdRegex = new(@"-(?<id>\d+)$",
            RegexOptions.Compiled);

        private readonly HttpClient _http;

        public MaudauPriceParser(HttpClient http)
        {
            _http = http;
        }

        public string StoreKey => "maudau";

        public bool CanParse(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Host.Equals("maudau.com.ua", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.maudau.com.ua", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<(string Name, decimal Price)> ParseAsync(
            string url,
            CancellationToken ct = default)
        {
            var slug = ExtractSlug(url);
            var productId = ExtractProductId(slug, url);

            using var json = await FetchProductJsonAsync(productId, url, ct);

            return ParseProduct(json, url);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private sealed class PriceParseException(string message)
            : Exception(message);

        private string ExtractSlug(string url)
        {
            var match = SlugRegex.Match(url);

            if (!match.Success)
                throw new PriceParseException(
                    $"😔 Не вдалося розпізнати посилання на товар Maudau.\n\n🔗 {url}");

            return match.Groups["slug"].Value;
        }

        private string ExtractProductId(string slug, string url)
        {
            var match = ProductIdRegex.Match(slug);

            if (!match.Success)
                throw new PriceParseException(
                    $"😔 Не вдалося визначити ID товару Maudau зі slug '{slug}'.\n\n🔗 {url}");

            return match.Groups["id"].Value;
        }

        private async Task<JsonDocument> FetchProductJsonAsync(
            string productId,
            string originalUrl,
            CancellationToken ct)
        {
            const int MaxAttempts = 5;

            static TimeSpan Delay(int attempt) =>
                TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 10));

            var requestUrl = $"{MaudauApiBase}?product_ids={productId}&per_page=1";

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                HttpResponseMessage? response = null;

                try
                {
                    using var request =
                        new HttpRequestMessage(HttpMethod.Get, requestUrl);

                    response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new PriceParseException(
                            $"😔 Товар не знайдено — можливо, він більше не продається.\n\n🔗 {originalUrl}");

                    if (IsTransient(response))
                    {
                        if (attempt == MaxAttempts)
                            break;

                        await Task.Delay(Delay(attempt), ct);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    await using var stream =
                        await response.Content.ReadAsStreamAsync(ct);

                    return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                }
                catch (PriceParseException)
                {
                    throw;
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

        private static (string Name, decimal Price) ParseProduct(
            JsonDocument doc,
            string url)
        {
            var root = doc.RootElement;

            // The endpoint returns a JSON array; an empty array means the product
            // ID wasn't recognised (deleted listing, wrong ID, etc.).
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                throw new PriceParseException(
                    $"😔 Товар не знайдено — можливо, він більше не продається.\n\n🔗 {url}");

            var product = root[0];

            var name = product.TryGetProperty("title", out var titleProp)
                ? titleProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException(
                    "Maudau API: product name not found in response.");

            // offer.price is stored in kopecks (e.g. 39900 → 399.00 UAH).
            if (!product.TryGetProperty("offer", out var offerProp))
                throw new InvalidOperationException(
                    $"Maudau API: 'offer' object not found in response for {url}.");

            if (!offerProp.TryGetProperty("price", out var priceProp)
                || !priceProp.TryGetDecimal(out var rawPrice)
                || rawPrice <= 0)
                throw new InvalidOperationException(
                    $"Maudau API: could not extract a valid price from response for {url}.");

            return (name!, rawPrice / 100m);
        }

        private static bool IsTransient(HttpResponseMessage response) =>
            response.StatusCode is
                HttpStatusCode.Forbidden or
                HttpStatusCode.TooManyRequests or
                HttpStatusCode.ServiceUnavailable
            || (int)response.StatusCode >= 500;
    }
}

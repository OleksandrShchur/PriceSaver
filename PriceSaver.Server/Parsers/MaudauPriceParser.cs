using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceSaver.Server.Parsers
{
    public class MaudauPriceParser : IPriceParser
    {
        private const string MaudauApiBase = "https://backend.prod.maudau.click/v1/user/products/";

        private static readonly Regex PriceRegex = new(
            @"(?<price>\d+(?:[\s\u00A0]\d{3})*(?:[.,]\d{1,2})?)\s*(?:₴|грн|uah)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MetaPriceRegex = new(
            @"(?:product:price:amount|og:price:amount|price)\s*[:=]\s*(?<price>\d+(?:[.,]\d{1,2})?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SlugRegex = new(@"/product/(?<slug>[^/?#]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        private string ExtractSlug(string url)
        {
            var match = SlugRegex.Match(url);

            if (!match.Success)
                throw new PriceParseException(
                    $"😔 Не вдалося розпізнати посилання на товар Maudau.\n\n🔗 {url}");

            return match.Groups["slug"].Value;
        }

        public async Task<(string Name, decimal Price)> ParseAsync(
            string url,
            CancellationToken ct = default)
        {
            var slug = ExtractSlug(url);
            return await FetchProductInfoFromApiAsync(slug, url, ct);
        }

        private sealed class PriceParseException(string message) : Exception(message);

        private async Task<(string Name, decimal Price)> FetchProductInfoFromApiAsync(
            string slug,
            string originalUrl,
            CancellationToken ct)
        {
            const int MaxAttempts = 5;

            static TimeSpan Delay(int attempt) =>
                TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 10));

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                HttpResponseMessage? response = null;

                try
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        $"{MaudauApiBase}{Uri.EscapeDataString(slug)}");

                    request.Headers.TryAddWithoutValidation(
                        "Accept",
                        "application/json, text/plain, */*");
                    request.Headers.TryAddWithoutValidation(
                        "User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                        "AppleWebKit/537.36 (KHTML, like Gecko) " +
                        "Chrome/124.0.0.0 Safari/537.36");

                    response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new PriceParseException(
                            $"😔 Товар не знайдено.\n\n🔗 {originalUrl}");

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

                    using var document =
                        await JsonDocument.ParseAsync(
                            stream,
                            cancellationToken: ct);

                    return ParseMaudauProductSearchResponse(document, originalUrl);
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
                $"😔 На жаль, не вдалося отримати оновлені дані про товар.\n" +
                $"Спробуй ще раз — можливо, сервіс Maudau тимчасово недоступний.\n\n" +
                $"🔗 {originalUrl}");
        }

        private static (string Name, decimal Price) ParseMaudauProductSearchResponse(
            JsonDocument document,
            string url)
        {
            var product = FindProductElement(document.RootElement);

            if (product is null)
                throw new PriceParseException(
                    $"😔 Не вдалося знайти дані про товар у відповіді сервісу.\n\n🔗 {url}");

            var name =
                GetJsonString(product.Value, "title") ??
                GetJsonString(product.Value, "name") ??
                GetJsonString(product.Value, "productName") ??
                GetJsonString(product.Value, "product_name");

            var price =
                GetJsonDecimal(product.Value, "priceToShow") ??
                GetJsonDecimal(product.Value, "currentPrice") ??
                GetJsonDecimal(product.Value, "current_price") ??
                // Some responses include price under `offer` object
                (product.Value.TryGetProperty("offer", out var offer)
                    ? (GetJsonDecimal(offer, "price") ?? GetJsonDecimal(offer, "old_price") ?? GetJsonDecimal(offer, "oldPrice"))
                    : null) ??
                GetJsonDecimal(product.Value, "price") ??
                GetJsonDecimal(product.Value, "discount") ??
                GetJsonDecimal(product.Value, "salePrice") ??
                GetJsonDecimal(product.Value, "finalPrice") ??
                0m;

            // Some API responses return price in cents (e.g. 9900 -> 99.00).
            // Normalize by dividing by 100 when the value looks like cents
            // (large integer and divisible by 100).
            if (price >= 1000m && price % 100m == 0m)
            {
                price /= 100m;
            }

            if (price <= 0)
                throw new PriceParseException(
                    $"😔 Не вдалося знайти ціну товару у відповіді сервісу.\n\n🔗 {url}");

            return (name ?? string.Empty, price);
        }

        private static JsonElement? FindProductElement(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
                return element[0];

            if (element.ValueKind != JsonValueKind.Object)
                return null;

            if (element.TryGetProperty("items", out var items))
            {
                var nested = FindProductElement(items);
                if (nested.HasValue)
                    return nested.Value;
            }

            if (element.TryGetProperty("products", out var products))
            {
                var nested = FindProductElement(products);
                if (nested.HasValue)
                    return nested.Value;
            }

            if (element.TryGetProperty("data", out var data))
            {
                var nested = FindProductElement(data);
                if (nested.HasValue)
                    return nested.Value;
            }

            if (element.TryGetProperty("product", out var product) &&
                product.ValueKind == JsonValueKind.Object)
            {
                return product;
            }

            return element;
        }

        private static string? GetJsonString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
                return null;

            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.GetRawText(),
                _ => null,
            };
        }

        private static decimal? GetJsonDecimal(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
                return null;

            if (prop.ValueKind == JsonValueKind.Number &&
                prop.TryGetDecimal(out var value))
            {
                return value;
            }

            if (prop.ValueKind == JsonValueKind.String &&
                decimal.TryParse(
                    prop.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static bool IsTransient(HttpResponseMessage response) =>
            response.StatusCode is
                HttpStatusCode.Forbidden or
                HttpStatusCode.TooManyRequests or
                HttpStatusCode.ServiceUnavailable
            || (int)response.StatusCode >= 500;
    }
}

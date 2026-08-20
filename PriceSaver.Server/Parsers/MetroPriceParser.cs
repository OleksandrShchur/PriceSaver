using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using PriceSaver.Server.Models;

namespace PriceSaver.Server.Parsers
{
    public class MetroPriceParser : IPriceParser
    {
        private const string Source = "Metro";
        private const string StoreId = "00027";
        private const string ApiBase =
            "https://shop.metro.ua/evaluate.article.v1/betty-articles";

        // Matches /shop/pv/{article}/{variant}/{bundle} and ignores an optional trailing slug:
        // https://shop.metro.ua/shop/pv/BTY-X9528/0032/0021/Ятрань-Шинка-...
        private static readonly Regex ProductPathRegex = new(
            @"/shop/pv/(?<article>[^/?#]+)/(?<variant>[^/?#]+)/(?<bundle>[^/?#]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly HttpClient _http;
        private readonly ILogger<MetroPriceParser> _logger;

        public MetroPriceParser(HttpClient http, ILogger<MetroPriceParser> logger)
        {
            _http = http;
            _logger = logger;
        }

        public string StoreKey => "metro";

        public StoreType StoreType => StoreType.Metro;

        public bool CanParse(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Host.Equals("shop.metro.ua", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.shop.metro.ua", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<(string Name, decimal Price)> ParseAsync(
            string url,
            CancellationToken ct = default)
        {
            _logger.LogDebug("Starting price parse for product URL: {Url}", url);

            try
            {
                var (articleId, variant, bundle) = ExtractProductPath(url);
                using var json = await FetchArticleJsonAsync(articleId, url, ct);
                var (name, price) = ParseArticle(json, articleId, variant, bundle, url);

                _logger.LogInformation(
                    "Successfully parsed price for '{ProductName}' from {Source}: {Price} UAH",
                    name,
                    Source,
                    price);

                return (name, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Price parse failed for URL: {Url} from {Source}", url, Source);
                throw;
            }
        }

        private static (string ArticleId, string Variant, string Bundle) ExtractProductPath(string url)
        {
            var match = ProductPathRegex.Match(url);

            if (!match.Success)
            {
                throw new PriceParseException(
                    $"Could not extract Metro product path from URL: {url}");
            }

            return (
                match.Groups["article"].Value,
                match.Groups["variant"].Value,
                match.Groups["bundle"].Value);
        }

        private async Task<JsonDocument> FetchArticleJsonAsync(
            string articleId,
            string originalUrl,
            CancellationToken ct)
        {
            var apiUrl =
                $"{ApiBase}?ids={Uri.EscapeDataString(articleId)}&country=UA&locale=uk-UA&storeIds={StoreId}&details=true";

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            _logger.LogDebug(
                "Received response from {Source} parser. StatusCode: {StatusCode}, ContentLength: {Length}",
                Source,
                response.StatusCode,
                response.Content.Headers.ContentLength);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new PriceParseException($"Metro product not found for URL: {originalUrl}");
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);

            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }

        private (string Name, decimal Price) ParseArticle(
            JsonDocument doc,
            string articleId,
            string variant,
            string bundle,
            string url)
        {
            if (!doc.RootElement.TryGetProperty("result", out var result)
                || !TryGetPropertyIgnoreCase(result, articleId, out var article))
            {
                throw new PriceParseException(
                    $"Metro API: article '{articleId}' not found in response for {url}.");
            }

            if (!article.TryGetProperty("variants", out var variants)
                || !TryGetPropertyIgnoreCase(variants, variant, out var variantEl))
            {
                throw new PriceParseException(
                    $"Metro API: variant '{variant}' not found in response for {url}.");
            }

            var name = variantEl.TryGetProperty("description", out var descriptionProp)
                ? descriptionProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogWarning(
                    "Product name not found in response from {Source} for URL: {Url}.",
                    Source,
                    url);

                throw new PriceParseException(
                    $"Metro API: product name not found in response for {url}.");
            }

            if (!variantEl.TryGetProperty("bundles", out var bundles)
                || !TryResolveBundle(bundles, bundle, out var bundleEl, out var resolvedBundle)
                || !TryGetFinalPrice(bundleEl, out var price))
            {
                _logger.LogWarning(
                    "Price element not found in response from {Source} for URL: {Url}.",
                    Source,
                    url);

                throw new PriceParseException(
                    $"Metro API: could not extract a valid finalPrice from response for {url}.");
            }

            if (!string.Equals(bundle, resolvedBundle, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Metro bundle '{Requested}' not found; using prefix match '{Resolved}' for {Url}.",
                    bundle,
                    resolvedBundle,
                    url);
            }

            return (name, price);
        }

        private static bool TryResolveBundle(
            JsonElement bundles,
            string requestedBundle,
            out JsonElement bundleEl,
            out string resolvedBundle)
        {
            if (TryGetPropertyIgnoreCase(bundles, requestedBundle, out bundleEl))
            {
                resolvedBundle = requestedBundle;
                return true;
            }

            if (bundles.ValueKind != JsonValueKind.Object)
            {
                bundleEl = default;
                resolvedBundle = requestedBundle;
                return false;
            }

            // Metro URLs sometimes arrive with a corrupted trailing suffix (e.g. 002121 or 002121:49).
            // Fall back to the longest available bundle key that is a prefix of the requested id.
            var fallbackKey = bundles.EnumerateObject()
                .Select(p => p.Name)
                .Where(k => requestedBundle.StartsWith(k, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();

            if (fallbackKey is not null
                && TryGetPropertyIgnoreCase(bundles, fallbackKey, out bundleEl))
            {
                resolvedBundle = fallbackKey;
                return true;
            }

            bundleEl = default;
            resolvedBundle = requestedBundle;
            return false;
        }

        private static bool TryGetFinalPrice(JsonElement bundleEl, out decimal price)
        {
            // Legacy / alternate shape: price directly on the bundle.
            if (TryGetFinalPriceFromSellingPriceInfo(bundleEl, out price))
                return true;

            // Current Metro API: price under stores/{storeId}/possibleDeliveryModes/...
            if (!TryGetPropertyIgnoreCase(bundleEl, "stores", out var stores)
                || !TryGetPropertyIgnoreCase(stores, StoreId, out var store)
                || !TryGetPropertyIgnoreCase(store, "possibleDeliveryModes", out var deliveryModes)
                || deliveryModes.ValueKind != JsonValueKind.Object)
            {
                price = default;
                return false;
            }

            if (TryGetPropertyIgnoreCase(deliveryModes, "STORE", out var storeMode)
                && TryGetFinalPriceFromDeliveryMode(storeMode, out price))
            {
                return true;
            }

            foreach (var mode in deliveryModes.EnumerateObject())
            {
                if (TryGetFinalPriceFromDeliveryMode(mode.Value, out price))
                    return true;
            }

            price = default;
            return false;
        }

        private static bool TryGetFinalPriceFromDeliveryMode(
            JsonElement deliveryMode,
            out decimal price)
        {
            if (!TryGetPropertyIgnoreCase(deliveryMode, "possibleFulfillmentTypes", out var fulfillmentTypes)
                || fulfillmentTypes.ValueKind != JsonValueKind.Object)
            {
                price = default;
                return false;
            }

            if (TryGetPropertyIgnoreCase(fulfillmentTypes, "STORE", out var storeFulfillment)
                && TryGetFinalPriceFromSellingPriceInfo(storeFulfillment, out price))
            {
                return true;
            }

            foreach (var fulfillment in fulfillmentTypes.EnumerateObject())
            {
                if (TryGetFinalPriceFromSellingPriceInfo(fulfillment.Value, out price))
                    return true;
            }

            price = default;
            return false;
        }

        private static bool TryGetFinalPriceFromSellingPriceInfo(
            JsonElement parent,
            out decimal price)
        {
            if (parent.TryGetProperty("sellingPriceInfo", out var priceInfo)
                && priceInfo.TryGetProperty("finalPrice", out var finalPriceProp)
                && finalPriceProp.TryGetDecimal(out price)
                && price > 0)
            {
                return true;
            }

            price = default;
            return false;
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement element,
            string name,
            out JsonElement value)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            if (element.TryGetProperty(name, out value))
                return true;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}

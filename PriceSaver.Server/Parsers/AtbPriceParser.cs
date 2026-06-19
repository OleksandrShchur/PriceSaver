using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using PriceSaver.Server.Models;

namespace PriceSaver.Server.Parsers
{
    public class AtbPriceParser : IPriceParser
    {
        private const string Source = "ATB";
        private const string JinaReaderBaseUrl = "https://r.jina.ai/";
        private const string JinaNotFoundMarker = "error 404";

        private static readonly Regex MarkdownHeadingRegex =
            new(@"^#+\s+(?<title>.+)$", RegexOptions.Compiled);

        // Matches: "250.06 з карткою ATБ" (price comes BEFORE the card label)
        // Handles mixed Latin/Cyrillic: A/А, T/Т are often Latin on the ATB site
        private static readonly Regex AtbCardPriceRegex = new(
            @"(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)\s+з\s+карткою\s+[AА][TТ][БB]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Fallback: "263.50 грн /"  or  "263.50 грн/шт"
        private static readonly Regex PriceWithCurrencyRegex = new(
            @"(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)\s*\u0433\u0440\u043d\s*/",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly HttpClient _http;
        private readonly ILogger<AtbPriceParser> _logger;

        public AtbPriceParser(HttpClient http, ILogger<AtbPriceParser> logger)
        {
            _http = http;
            _logger = logger;
        }

        public string StoreKey => "atb";

        public StoreType StoreType => StoreType.ATB;

        public bool CanParse(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Host.Equals("atbmarket.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.atbmarket.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("atbmarket.ua", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.atbmarket.ua", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("atb.ua", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.atb.ua", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<(string Name, decimal Price)> ParseAsync(
            string url,
            CancellationToken ct = default)
        {
            _logger.LogDebug("Starting price parse for product URL: {Url}", url);

            try
            {
                var text = await DownloadProductTextWithReaderAsync(url, ct);
                var (name, price, loyaltyPrice) = ParseProductText(text, url);

                _logger.LogInformation(
                    "Successfully parsed price for '{ProductName}' from {Source}: {Price} UAH (loyalty: {LoyaltyPrice})",
                    name,
                    Source,
                    price,
                    loyaltyPrice);

                return (name, price);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Price parse failed for URL: {Url} from {Source}", url, Source);
                throw;
            }
        }

        private async Task<string> DownloadProductTextWithReaderAsync(
            string url,
            CancellationToken ct)
        {
            using var request =
                new HttpRequestMessage(HttpMethod.Get, $"{JinaReaderBaseUrl}{url}");

            request.Headers.Accept.ParseAdd("text/plain");

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            var content = await response.Content.ReadAsStringAsync(ct);

            _logger.LogDebug(
                "Received response from {Source} parser. StatusCode: {StatusCode}, ContentLength: {Length}",
                Source,
                response.StatusCode,
                content?.Length);

            response.EnsureSuccessStatusCode();

            return content ?? string.Empty;
        }

        private (string Name, decimal Price, decimal LoyaltyPrice) ParseProductText(string text, string url)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("ATB product page text was empty.");

            // Jina returns HTTP 200 even when the target URL is 404;
            // the error surfaces only in the body as "Warning: Target URL returned error 404".
            if (text.Contains(JinaNotFoundMarker, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ATB product page not found (404).");

            // ── Title ──────────────────────────────────────────────────────────
            var title = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => MarkdownHeadingRegex.Match(line))
                .Where(m => m.Success)
                .Select(m => NormalizeMarkdownTitle(m.Groups["title"].Value))
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException("ATB product title was not found.");

            // ── Price: prefer ATB-card price, fall back to regular price ───────
            var cleanedText = CleanText(text);

            var cardPriceText = TryExtractCardPrice(cleanedText);
            var regularPriceText = TryExtractRegularPrice(cleanedText);
            var priceText = cardPriceText ?? regularPriceText;

            if (string.IsNullOrWhiteSpace(priceText))
            {
                _logger.LogWarning(
                    "Price element not found in response from {Source} for URL: {Url}. Raw content length: {Length}",
                    Source,
                    url,
                    text?.Length);

                throw new InvalidOperationException("ATB product price was not found.");
            }

            if (!decimal.TryParse(
                    NormalizePrice(priceText),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var price))
            {
                throw new InvalidOperationException(
                    $"ATB product price '{priceText}' could not be parsed.");
            }

            var loyaltyPrice = cardPriceText is not null
                ? price
                : regularPriceText is not null &&
                  decimal.TryParse(NormalizePrice(regularPriceText), NumberStyles.Number, CultureInfo.InvariantCulture, out var regular)
                    ? regular
                    : price;

            return (title, price, loyaltyPrice);
        }

        /// <summary>
        /// Tries to find the discounted ATB-card price, e.g. "250.06 з карткою ATБ".
        /// Returns null when no such price is present on the page.
        /// </summary>
        private static string? TryExtractCardPrice(string cleanedText)
        {
            var match = AtbCardPriceRegex.Match(cleanedText);
            return match.Success ? match.Groups["price"].Value : null;
        }

        /// <summary>
        /// Fallback: returns the first price followed by "грн /", e.g. "263.50 грн/шт".
        /// </summary>
        private static string? TryExtractRegularPrice(string cleanedText)
        {
            return PriceWithCurrencyRegex
                .Matches(cleanedText)
                .Select(m => m.Groups["price"].Value)
                .FirstOrDefault();
        }

        private static string CleanText(string? text) =>
            Regex.Replace(
                WebUtility.HtmlDecode(text ?? string.Empty),
                @"\s+",
                " ")
            .Trim();

        private static string NormalizePrice(string priceText) =>
            Regex.Replace(priceText, @"\s", string.Empty)
                .Replace(",", ".");

        private static string NormalizeMarkdownTitle(string title)
        {
            var cleanTitle = CleanText(
                Regex.Replace(
                    title,
                    @"!\[[^\]]*\]\([^)]+\)|\[[^\]]*\]\([^)]+\)",
                    string.Empty));

            cleanTitle = cleanTitle.TrimStart('\u27a4').Trim();

            var buyIndex = cleanTitle.IndexOf(
                " купити ",
                StringComparison.OrdinalIgnoreCase);

            if (buyIndex > 0)
                cleanTitle = cleanTitle[..buyIndex].Trim();

            var starIndex = cleanTitle.IndexOf(
                " ★ ",
                StringComparison.OrdinalIgnoreCase);

            if (starIndex > 0)
                cleanTitle = cleanTitle[..starIndex].Trim();

            return cleanTitle;
        }
    }
}

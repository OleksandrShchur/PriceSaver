using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace PriceSaver.Server.Parsers
{
    public class AtbPriceParser : IPriceParser
    {
        private const string JinaReaderBaseUrl = "https://r.jina.ai/";

        private static readonly Regex MarkdownHeadingRegex =
            new(@"^#+\s+(?<title>.+)$", RegexOptions.Compiled);

        private static readonly Regex AtbCardPriceRegex = new(
            @"\u043a\u0430\u0440\u0442\u043a\u043e\u044e\s+\u0410\u0422\u0411[^\d]*(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PriceWithCurrencyRegex = new(
            @"(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)\s*\u0433\u0440\u043d\s*/",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HttpClient JinaHttp = CreateJinaHttpClient();

        public string StoreKey => "atb";

        public bool CanParse(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

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
            var text = await DownloadProductTextWithReaderAsync(url, ct);

            return ParseProductText(text);
        }

        private static async Task<string> DownloadProductTextWithReaderAsync(
            string url,
            CancellationToken ct)
        {
            using var request =
                new HttpRequestMessage(HttpMethod.Get, $"{JinaReaderBaseUrl}{url}");

            request.Headers.Accept.ParseAdd("text/plain");

            using var response =
                await JinaHttp.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(ct);
        }

        private static (string Name, decimal Price) ParseProductText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(
                    "ATB product page text was empty.");
            }

            var title = text
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Select(line => MarkdownHeadingRegex.Match(line))
                .Where(match => match.Success)
                .Select(match =>
                    NormalizeMarkdownTitle(match.Groups["title"].Value))
                .FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate));

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException(
                    "ATB product title was not found.");
            }

            var priceText = ExtractAtbCardPriceText(text)
                ?? PriceWithCurrencyRegex
                    .Matches(text)
                    .Select(match => match.Groups["price"].Value)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(priceText))
            {
                throw new InvalidOperationException(
                    "ATB product price was not found.");
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

            return (title, price);
        }

        private static string? ExtractAtbCardPriceText(string text)
        {
            var match = AtbCardPriceRegex.Match(CleanText(text));

            return match.Success
                ? match.Groups["price"].Value
                : null;
        }

        private static string CleanText(string? text) =>
            Regex.Replace(
                HtmlEntity.DeEntitize(text ?? string.Empty),
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
            {
                cleanTitle = cleanTitle[..buyIndex].Trim();
            }

            var starIndex = cleanTitle.IndexOf(
                " ★ ",
                StringComparison.OrdinalIgnoreCase);

            if (starIndex > 0)
            {
                cleanTitle = cleanTitle[..starIndex].Trim();
            }

            return cleanTitle;
        }

        private static HttpClient CreateJinaHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            };

            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/125.0.0.0 Safari/537.36");

            return http;
        }
    }
}

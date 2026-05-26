using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace PriceSaver.Server.Parsers
{
    public class AtbPriceParser : IPriceParser
    {
        private const string HryvniaPerUnitText = "\u0433\u0440\u043d/";
        private const string AtbCardLabelText = "\u043a\u0430\u0440\u0442\u043a\u043e\u044e \u0410\u0422\u0411";
        private const string JinaReaderBaseUrl = "https://r.jina.ai/";

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly Regex MarkdownHeadingRegex = new(@"^#+\s+(?<title>.+)$", RegexOptions.Compiled);

        // FIX: allow optional whitespace around the decimal separator (\s*[.,]\s*)
        // so that prices split across multiple spans ("250 . 06", "250,06") are captured correctly.
        private static readonly Regex AtbCardPriceRegex = new(
            @"\u043a\u0430\u0440\u0442\u043a\u043e\u044e\s+\u0410\u0422\u0411[^\d]*(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PriceWithCurrencyRegex = new(
            @"(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)\s*\u0433\u0440\u043d\s*/",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PriceRegex = new(
            @"(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)",
            RegexOptions.Compiled);

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

        public async Task<(string Name, decimal Price)> ParseAsync(string url, CancellationToken ct = default)
        {
            var (content, isHtml) = await DownloadProductContentAsync(url, ct);

            return isHtml
                ? ParseProductHtml(content)
                : ParseProductText(content);
        }

        private static async Task<(string Content, bool IsHtml)> DownloadProductContentAsync(string url, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://www.atbmarket.com/");
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true
            };

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return (await DownloadProductTextWithReaderAsync(url, ct), false);
            }

            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadAsStringAsync(ct), true);
        }

        private static async Task<string> DownloadProductTextWithReaderAsync(string url, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{JinaReaderBaseUrl}{url}");
            request.Headers.Accept.ParseAdd("text/plain");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }

        internal static (string Name, decimal Price) ParseProductHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                throw new InvalidOperationException("ATB product page was empty.");
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var title = CleanText(
                doc.DocumentNode.SelectSingleNode($"//*[{HasClass("product-page__title")}]")?.InnerText
                ?? doc.DocumentNode.SelectSingleNode("//h1")?.InnerText);
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException("ATB product title was not found.");
            }

            var priceText = ExtractAtbCardPriceText(doc)
                ?? ExtractRegularPriceText(doc);

            if (string.IsNullOrWhiteSpace(priceText))
            {
                throw new InvalidOperationException("ATB product price was not found.");
            }

            if (!decimal.TryParse(NormalizePrice(priceText), NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                throw new InvalidOperationException($"ATB product price '{priceText}' could not be parsed.");
            }

            return (title, price);
        }

        private static string? ExtractAtbCardPriceText(HtmlDocument doc)
        {
            // Strategy 1: look inside .product-about__buy-row for the full card-price pattern.
            var buyRowNodes = doc.DocumentNode.SelectNodes($"//*[{HasClass("product-about__buy-row")}]");
            var cardPriceText = buyRowNodes
                ?.Select(node => AtbCardPriceRegex.Match(CleanText(node.InnerText)))
                .FirstOrDefault(match => match.Success)
                ?.Groups["price"].Value;

            if (!string.IsNullOrWhiteSpace(cardPriceText))
            {
                return cardPriceText;
            }

            // Strategy 2: find any element whose aggregated text contains "карткою атб",
            // then apply the card-price regex (handles most DOM layouts).
            cardPriceText = doc.DocumentNode
                .SelectNodes($"//*[contains(translate(normalize-space(.), '\u043a\u0410\u0422\u0411', '\u043a\u0430\u0442\u0431'), '{AtbCardLabelText.ToLowerInvariant()}')]")
                ?.Select(node => AtbCardPriceRegex.Match(CleanText(node.InnerText)))
                .FirstOrDefault(match => match.Success)
                ?.Groups["price"].Value;

            if (!string.IsNullOrWhiteSpace(cardPriceText))
            {
                return cardPriceText;
            }

            // Strategy 3 (fallback for split-span layouts):
            // Find raw text nodes that literally contain "карткою" and walk UP the DOM
            // up to 3 ancestor levels, retrying the regex at each level.
            // This handles cases where the label ("з карткою АТБ") and the price ("250.06")
            // live in separate sibling elements — no single element below their common
            // ancestor contains both, but that ancestor does.
            var labelTextNodes = doc.DocumentNode.SelectNodes(
                "//text()[contains(translate(., '\u0410\u0422\u0411', '\u0430\u0442\u0431'), '\u043a\u0430\u0440\u0442\u043a\u043e\u044e \u0430\u0442\u0431')]");

            if (labelTextNodes != null)
            {
                foreach (var textNode in labelTextNodes)
                {
                    var ancestor = textNode.ParentNode;
                    for (int level = 0; level < 3 && ancestor != null; level++, ancestor = ancestor.ParentNode)
                    {
                        var match = AtbCardPriceRegex.Match(CleanText(ancestor.InnerText));
                        if (match.Success)
                        {
                            return match.Groups["price"].Value;
                        }
                    }
                }
            }

            return null;
        }

        private static string? ExtractRegularPriceText(HtmlDocument doc)
        {
            var priceText = doc.DocumentNode
                .SelectNodes(
                    $"//*[{HasClass("product-about__buy-row")}]//*[{HasClass("product-price__top")}]"
                    + $" | //*[{HasClass("product-price--weight")}]//*[{HasClass("product-price__top")}]")
                ?.Select(node => PriceRegex.Match(CleanText(node.InnerText)))
                .FirstOrDefault(match => match.Success)
                ?.Groups["price"].Value;

            priceText ??= doc.DocumentNode
                .SelectNodes($"//text()[contains(., '{HryvniaPerUnitText}')]")
                ?.Select(node => CleanText(node.InnerText))
                .Select(text => PriceWithCurrencyRegex.Match(text))
                .FirstOrDefault(match => match.Success)
                ?.Groups["price"].Value;

            return priceText;
        }

        private static (string Name, decimal Price) ParseProductText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("ATB product page text was empty.");
            }

            var title = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => MarkdownHeadingRegex.Match(line))
                .Where(match => match.Success)
                .Select(match => NormalizeMarkdownTitle(match.Groups["title"].Value))
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException("ATB product title was not found.");
            }

            var priceText = ExtractAtbCardPriceText(text)
                ?? PriceWithCurrencyRegex
                .Matches(text)
                .Select(match => match.Groups["price"].Value)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(priceText))
            {
                throw new InvalidOperationException("ATB product price was not found.");
            }

            if (!decimal.TryParse(NormalizePrice(priceText), NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                throw new InvalidOperationException($"ATB product price '{priceText}' could not be parsed.");
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
            Regex.Replace(HtmlEntity.DeEntitize(text ?? string.Empty), @"\s+", " ").Trim();

        // FIX: use Regex.Replace to strip ALL Unicode whitespace variants
        // (including \u00a0 non-breaking space) before parsing.
        private static string NormalizePrice(string priceText) =>
            Regex.Replace(priceText, @"\s", string.Empty).Replace(",", ".");

        private static string NormalizeMarkdownTitle(string title)
        {
            var cleanTitle = CleanText(Regex.Replace(title, @"!\[[^\]]*\]\([^)]+\)|\[[^\]]*\]\([^)]+\)", string.Empty));
            cleanTitle = cleanTitle.TrimStart('\u27a4').Trim();

            var buyIndex = cleanTitle.IndexOf(" купити ", StringComparison.OrdinalIgnoreCase);
            if (buyIndex > 0)
            {
                cleanTitle = cleanTitle[..buyIndex].Trim();
            }

            var starIndex = cleanTitle.IndexOf(" ★ ", StringComparison.OrdinalIgnoreCase);
            if (starIndex > 0)
            {
                cleanTitle = cleanTitle[..starIndex].Trim();
            }

            return cleanTitle;
        }

        private static string HasClass(string className) =>
            $"contains(concat(' ', normalize-space(@class), ' '), ' {className} ')";

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            };

            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk-UA,uk;q=0.9,en-US;q=0.8,en;q=0.7");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            return http;
        }
    }
}

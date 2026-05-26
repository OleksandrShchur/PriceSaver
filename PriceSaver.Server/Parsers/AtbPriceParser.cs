using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace PriceSaver.Server.Parsers
{
    public class AtbPriceParser : IPriceParser
    {
        private const string JinaReaderBaseUrl = "https://r.jina.ai/";

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly Regex MarkdownHeadingRegex = new(@"^#+\s+(?<title>.+)$", RegexOptions.Compiled);

        private static readonly Regex AtbCardPriceRegex = new(
            @"\u043a\u0430\u0440\u0442\u043a\u043e\u044e\s+\u0410\u0422\u0411[^\d]*(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PriceWithCurrencyRegex = new(
            @"(?<price>\d+(?:\s*[.,]\s*\d{1,2})?)\s*\u0433\u0440\u043d\s*/",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        /// <summary>
        /// Reads the ATB-card discounted price from:
        /// <code>
        /// &lt;data value="250.06" class="atbcard-sale__price-top"&gt;...&lt;/data&gt;
        /// </code>
        /// Returns null when the element or its value attribute is absent.
        /// </summary>
        private static string? ExtractAtbCardPriceText(HtmlDocument doc)
        {
            var dataNode = doc.DocumentNode.SelectSingleNode($"//data[{HasClass("atbcard-sale__price-top")}]");
            var value = dataNode?.GetAttributeValue("value", null);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Reads the regular shelf price from:
        /// <code>
        /// &lt;data value="" class="product-price__top"&gt;
        ///     &lt;span&gt;263.&lt;span class="product-price__coin"&gt;50&lt;/span&gt;&lt;/span&gt;
        /// &lt;/data&gt;
        /// </code>
        /// First tries the value attribute; if empty reconstructs the number from the
        /// integer text node and the product-price__coin span ("263." + "50" → "263.50").
        /// </summary>
        private static string? ExtractRegularPriceText(HtmlDocument doc)
        {
            var dataNode = doc.DocumentNode.SelectSingleNode($"//data[{HasClass("product-price__top")}]");

            // Prefer the machine-readable value attribute when ATB fills it.
            var valueAttr = dataNode?.GetAttributeValue("value", null);
            if (!string.IsNullOrWhiteSpace(valueAttr))
            {
                return valueAttr;
            }

            // Reconstruct from the visible span structure:
            // <span>263.<span class="product-price__coin">50</span></span>
            var coinNode = dataNode?.SelectSingleNode($".//*[{HasClass("product-price__coin")}]");
            if (coinNode == null)
            {
                return null;
            }

            // The direct text child of the coin span's parent is the integer+dot part ("263.").
            var integerPart = coinNode.ParentNode.ChildNodes
                .Where(n => n.NodeType == HtmlNodeType.Text)
                .Select(n => n.InnerText.Trim())
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

            var decimalPart = coinNode.InnerText.Trim();

            return string.IsNullOrWhiteSpace(integerPart) || string.IsNullOrWhiteSpace(decimalPart)
                ? null
                : integerPart + decimalPart;  // "263." + "50" → "263.50"
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

using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace PriceSaver.Server.Parsers
{
    public class MetroPriceParser : IPriceParser
    {
        public string StoreKey => "metro";

        public bool CanParse(string url) => url.Contains("metro.ua", StringComparison.OrdinalIgnoreCase) || url.Contains("metro", StringComparison.OrdinalIgnoreCase);

        public async Task<(string Name, decimal Price)> ParseAsync(string url, CancellationToken ct = default)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PriceSaverBot/1.0 (+https://example.com)");
            var html = await http.GetStringAsync(url, ct);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nameNode = doc.DocumentNode.SelectSingleNode("//h1") ?? doc.DocumentNode.SelectSingleNode("//*[@class='product-header__title']");
            var title = nameNode?.InnerText?.Trim() ?? "Product";

            var priceMatch = Regex.Match(html, @"(\d+[\s,]?\d*\.?\d*)\s*₴");
            var priceText = priceMatch.Success ? priceMatch.Groups[1].Value : Regex.Match(html, "\\d+[\\.,]?\\d+").Value;

            if (!decimal.TryParse(priceText.Replace(" ", "").Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                price = 0m;
            }

            return (title, price);
        }
    }
}

using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace PriceSaver.Server.Parsers
{
    public class AtbPriceParser : IPriceParser
    {
        public string StoreKey => "atb";

        public bool CanParse(string url) => url.Contains("atb.ua", StringComparison.OrdinalIgnoreCase) || url.Contains("atbmarket.ua", StringComparison.OrdinalIgnoreCase);

        public async Task<(string Name, decimal Price)> ParseAsync(string url, CancellationToken ct = default)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PriceSaverBot/1.0 (+https://example.com)");
            var html = await http.GetStringAsync(url, ct);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Attempt to extract product name and price conservatively
            var nameNode = doc.DocumentNode.SelectSingleNode("//h1") ?? doc.DocumentNode.SelectSingleNode("//*[@class='product__title']");
            var title = nameNode?.InnerText?.Trim() ?? "Product";

            // Find something that looks like a price
            var priceText = Regex.Match(html, @"(\d+[\s,]?\d*\.?\d*)\s*₴").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(priceText))
            {
                // fallback digits
                priceText = Regex.Match(html, @"\d+[\.,]?\d+").Value;
            }

            if (!decimal.TryParse(priceText.Replace(" ", "").Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                price = 0m;
            }

            return (title, price);
        }
    }
}

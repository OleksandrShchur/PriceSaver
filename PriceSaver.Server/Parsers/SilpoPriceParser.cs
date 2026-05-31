using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace PriceSaver.Server.Parsers
{
    public class SilpoPriceParser : IPriceParser
    {
        private const string JinaReaderBaseUrl = "https://r.jina.ai/";

        private static readonly Regex MarkdownHeadingRegex =
            new(@"^#+\s+(?<title>.+)$", RegexOptions.Compiled);

        // Silpo renders prices as "54.99 грн" or "49 грн".
        // On discounted products the discounted price appears first:
        //   "55.99 грн ~~ 69.99 грн ~~ -20 %"
        // so the first match is always what the customer actually pays.
        private static readonly Regex SilpoPriceRegex = new(
            @"(?<price>\d+(?:[.,]\d{1,2})?)\s*грн",
            RegexOptions.Compiled);

        private readonly HttpClient _jinaHttp;

        public SilpoPriceParser(IConfiguration configuration)
        {
            _jinaHttp = CreateJinaHttpClient(configuration["JinaApiKey"]);
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
            // PriceParseException already carries a ready-to-send Ukrainian message,
            // so let it bubble up to the bot handler unchanged.
            var text = await FetchWithJinaAsync(url, ct);

            return ParseProductText(text);
        }

        private sealed class PriceParseException(string message) : Exception(message);

        private static bool IsTransientFailure(HttpResponseMessage response) =>
            response.StatusCode is HttpStatusCode.Forbidden          // 403
                                or HttpStatusCode.TooManyRequests    // 429
                                or HttpStatusCode.ServiceUnavailable // 503
            || (int)response.StatusCode >= 500;

        private static bool IsCloudflareChallenge(string text) =>
            text.Contains("Performing security verification", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);

        private async Task<string> FetchWithJinaAsync(string url, CancellationToken ct)
        {
            const int MaxAttempts = 5;

            // Delays between attempts: 1s → 2s → 4s → 8s (capped at 10s)
            static TimeSpan Delay(int attempt) =>
                TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 10));

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                using var request =
                    new HttpRequestMessage(HttpMethod.Get, $"{JinaReaderBaseUrl}{url}");
                request.Headers.Accept.ParseAdd("text/plain");

                HttpResponseMessage? response = null;
                try
                {
                    response = await _jinaHttp.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct);

                    if (IsTransientFailure(response))
                    {
                        if (attempt == MaxAttempts) break;
                        await Task.Delay(Delay(attempt), ct);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();

                    var text = await response.Content.ReadAsStringAsync(ct);

                    if (IsCloudflareChallenge(text))
                    {
                        if (attempt == MaxAttempts) break;
                        await Task.Delay(Delay(attempt), ct);
                        continue;
                    }

                    return text; // success
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
                $"🔗 {url}");
        }

        private static (string Name, decimal Price) ParseProductText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Silpo product page text was empty.");

            var title = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => MarkdownHeadingRegex.Match(line))
                .Where(m => m.Success)
                .Select(m => CleanTitle(m.Groups["title"].Value))
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException("Silpo product title was not found.");

            var priceText = SilpoPriceRegex
                .Matches(NormalizeWhitespace(text))
                .Select(m => m.Groups["price"].Value)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(priceText))
                throw new InvalidOperationException("Silpo product price was not found.");

            if (!decimal.TryParse(
                    priceText.Replace(",", "."),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var price))
            {
                throw new InvalidOperationException(
                    $"Silpo product price '{priceText}' could not be parsed.");
            }

            return (title!, price);
        }

        private static string CleanTitle(string raw)
        {
            // Page <title> uses " – " (em dash) before the store name:
            //   "Йогурт … 300г – онлайн-супермаркет «Сільпо»"
            var emDashIdx = raw.IndexOf(" \u2013 ", StringComparison.Ordinal);
            if (emDashIdx > 0)
                raw = raw[..emDashIdx];

            // Some headings use " | " as separator instead
            var pipeIdx = raw.IndexOf(" | ", StringComparison.Ordinal);
            if (pipeIdx > 0)
                raw = raw[..pipeIdx];

            // Strip " купити" SEO suffix if present
            var buyIdx = raw.IndexOf(" купити", StringComparison.OrdinalIgnoreCase);
            if (buyIdx > 0)
                raw = raw[..buyIdx];

            return NormalizeWhitespace(HtmlEntity.DeEntitize(raw));
        }

        private static string NormalizeWhitespace(string? text) =>
            Regex.Replace(
                HtmlEntity.DeEntitize(text ?? string.Empty),
                @"\s+",
                " ")
            .Trim();

        private static HttpClient CreateJinaHttpClient(string? apiKey)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            };

            var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/125.0.0.0 Safari/537.36");

            if (!string.IsNullOrWhiteSpace(apiKey))
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            return http;
        }
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace PriceSaver.Server.Services
{
    public sealed class SilpoShortLinkResolver : ISilpoShortLinkResolver
    {
        private const string DeepLinksApiBase = "https://sf-mobile-api.silpo.ua/v1/deep-links/";

        private static readonly Regex ProductPathRegex = new(
            @"^/product/[^/?#]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ShortCodeRegex = new(
            @"^/(?<code>[A-Za-z0-9_-]+)/?$",
            RegexOptions.Compiled);

        private readonly HttpClient _http;
        private readonly ILogger<SilpoShortLinkResolver> _logger;

        public SilpoShortLinkResolver(HttpClient http, ILogger<SilpoShortLinkResolver> logger)
        {
            _http = http;
            _logger = logger;
        }

        public bool NeedsResolve(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return uri.Host.Equals("link.silpo.ua", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string?> ResolveAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!TryExtractShortCode(url, out var shortCode))
                {
                    _logger.LogWarning("Could not extract Silpo short-link code from {Url}", url);
                    return null;
                }

                // link.silpo.ua serves an SPA (HTTP 200), not an HTTP redirect.
                // The product destination comes from Silpo's deep-links API.
                using var response = await _http.GetAsync(
                    DeepLinksApiBase + Uri.EscapeDataString(shortCode),
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Silpo deep-links API returned {StatusCode} for {Url}",
                        (int)response.StatusCode,
                        url);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!doc.RootElement.TryGetProperty("target", out var targetElement)
                    || targetElement.ValueKind != JsonValueKind.String)
                {
                    _logger.LogWarning("Silpo deep-links response missing target for {Url}", url);
                    return null;
                }

                var target = targetElement.GetString();
                if (string.IsNullOrWhiteSpace(target)
                    || !Uri.TryCreate(target, UriKind.Absolute, out var targetUri))
                {
                    _logger.LogWarning("Silpo deep-links target is not an absolute URI for {Url}", url);
                    return null;
                }

                var productUrl = ExtractProductUrl(targetUri);
                if (productUrl is null)
                {
                    _logger.LogWarning(
                        "Silpo deep-links target had no product fallback: {Target} (from {Url})",
                        target,
                        url);
                    return null;
                }

                _logger.LogInformation(
                    "Resolved Silpo short link {Url} to {FinalUrl}",
                    url,
                    productUrl);

                return productUrl;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to resolve Silpo short link {Url}", url);
                return null;
            }
        }

        private static bool TryExtractShortCode(string url, out string shortCode)
        {
            shortCode = string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            var match = ShortCodeRegex.Match(uri.AbsolutePath);
            if (!match.Success)
                return false;

            shortCode = match.Groups["code"].Value;
            return shortCode.Length > 0;
        }

        private static string? ExtractProductUrl(Uri targetUri)
        {
            var query = QueryHelpers.ParseQuery(targetUri.Query);

            if (query.TryGetValue("fallback", out var fallbackValues))
            {
                var fallback = fallbackValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(fallback)
                    && Uri.TryCreate(fallback, UriKind.Absolute, out var fallbackUri)
                    && IsSilpoProductUrl(fallbackUri))
                {
                    return fallbackUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
                }
            }

            if (query.TryGetValue("productId", out var productIdValues))
            {
                var productId = productIdValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(productId)
                    && productId.IndexOfAny(['/', '?', '#']) < 0)
                {
                    return $"https://silpo.ua/product/{productId}";
                }
            }

            return null;
        }

        private static bool IsSilpoProductUrl(Uri uri)
        {
            var hostOk = uri.Host.Equals("silpo.ua", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.silpo.ua", StringComparison.OrdinalIgnoreCase);

            return hostOk && ProductPathRegex.IsMatch(uri.AbsolutePath);
        }
    }
}

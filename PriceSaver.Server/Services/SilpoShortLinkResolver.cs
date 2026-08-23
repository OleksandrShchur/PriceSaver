using System.Text.RegularExpressions;

namespace PriceSaver.Server.Services
{
    public sealed class SilpoShortLinkResolver : ISilpoShortLinkResolver
    {
        private static readonly Regex ProductPathRegex = new(
            @"^/product/[^/?#]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Silpo short-link resolve failed with status {StatusCode} for {Url}",
                        (int)response.StatusCode,
                        url);
                    return null;
                }

                var finalUri = response.RequestMessage?.RequestUri;
                if (finalUri is null || !finalUri.IsAbsoluteUri)
                {
                    _logger.LogWarning("Silpo short-link resolve returned no final URI for {Url}", url);
                    return null;
                }

                if (!IsSilpoProductUrl(finalUri))
                {
                    _logger.LogWarning(
                        "Silpo short-link resolved to non-product URL {FinalUrl} from {Url}",
                        finalUri,
                        url);
                    return null;
                }

                _logger.LogInformation(
                    "Resolved Silpo short link {Url} to {FinalUrl}",
                    url,
                    finalUri);

                return finalUri.ToString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to resolve Silpo short link {Url}", url);
                return null;
            }
        }

        private static bool IsSilpoProductUrl(Uri uri)
        {
            var hostOk = uri.Host.Equals("silpo.ua", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("www.silpo.ua", StringComparison.OrdinalIgnoreCase);

            return hostOk && ProductPathRegex.IsMatch(uri.AbsolutePath);
        }
    }
}

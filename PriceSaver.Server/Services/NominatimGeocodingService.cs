using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Extensions;
using PriceSaver.Server.Models;
using PriceSaver.Server.Options;

namespace PriceSaver.Server.Services
{
    public sealed class NominatimGeocodingService : INominatimGeocodingService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly NominatimOptions _options;
        private readonly ILogger<NominatimGeocodingService> _logger;

        private static readonly SemaphoreSlim RateGate = new(1, 1);
        private static DateTime NextAllowedUtc = DateTime.MinValue;

        public NominatimGeocodingService(
            HttpClient httpClient,
            IOptions<NominatimOptions> options,
            ILogger<NominatimGeocodingService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<GeocodingResult>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<GeocodingResult>();
            }

            var limit = _options.SearchResultLimit;
            var path =
                $"search?q={Uri.EscapeDataString(query.Trim())}&format=json&limit={limit}&addressdetails=0";

            var json = await SendAsync(path, cancellationToken);
            if (json is null)
            {
                return Array.Empty<GeocodingResult>();
            }

            var items = JsonSerializer.Deserialize<List<NominatimPlaceDto>>(json, JsonOptions)
                ?? new List<NominatimPlaceDto>();

            return items
                .Select(TryMap)
                .Where(r => r is not null)
                .Cast<GeocodingResult>()
                .ToList();
        }

        public async Task<GeocodingResult?> ReverseAsync(
            decimal latitude,
            decimal longitude,
            CancellationToken cancellationToken)
        {
            var lat = latitude.ToString(CultureInfo.InvariantCulture);
            var lon = longitude.ToString(CultureInfo.InvariantCulture);
            var path = $"reverse?lat={lat}&lon={lon}&format=json&addressdetails=0";

            var json = await SendAsync(path, cancellationToken);
            if (json is null)
            {
                return null;
            }

            var item = JsonSerializer.Deserialize<NominatimPlaceDto>(json, JsonOptions);
            return item is null ? null : TryMap(item);
        }

        private async Task<string?> SendAsync(string relativePath, CancellationToken cancellationToken)
        {
            var policy = HttpClientPolicyExtensions.GetRetryPolicy();
            var interval = TimeSpan.FromSeconds(_options.MinRequestIntervalSeconds);

            await RateGate.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                if (now < NextAllowedUtc)
                {
                    await Task.Delay(NextAllowedUtc - now, cancellationToken);
                }

                try
                {
                    var response = await policy.ExecuteAsync(
                        ct => _httpClient.GetAsync(relativePath, ct),
                        cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning(
                            "Nominatim request failed with {StatusCode} for {Path}",
                            response.StatusCode,
                            relativePath);
                        return null;
                    }

                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    _logger.LogWarning(ex, "Nominatim request error for {Path}", relativePath);
                    return null;
                }
            }
            finally
            {
                NextAllowedUtc = DateTime.UtcNow + interval;
                RateGate.Release();
            }
        }

        private static GeocodingResult? TryMap(NominatimPlaceDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.DisplayName))
            {
                return null;
            }

            if (!decimal.TryParse(dto.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !decimal.TryParse(dto.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                return null;
            }

            return new GeocodingResult
            {
                DisplayName = dto.DisplayName.Trim(),
                Latitude = decimal.Round(lat, 6),
                Longitude = decimal.Round(lon, 6)
            };
        }

        private sealed class NominatimPlaceDto
        {
            [JsonPropertyName("display_name")]
            public string? DisplayName { get; set; }

            [JsonPropertyName("lat")]
            public string? Lat { get; set; }

            [JsonPropertyName("lon")]
            public string? Lon { get; set; }
        }
    }
}

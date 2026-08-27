using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;

namespace PriceSaver.Server.StoreLocations
{
    public sealed class AtbStoreLocationProvider : IStoreLocationProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly ILogger<AtbStoreLocationProvider> _logger;

        public AtbStoreLocationProvider(
            HttpClient httpClient,
            IOptions<StoreLocationSourcesOptions> options,
            ILogger<AtbStoreLocationProvider> logger)
        {
            _httpClient = httpClient;
            _baseUrl = options.Value.Atb.BaseUrl.TrimEnd('/') + "/";
            _logger = logger;
        }

        public async Task<IReadOnlyList<StoreLocationDto>> GetLocationsAsync(CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(_baseUrl, ct);
            response.EnsureSuccessStatusCode();

            // TODO: confirm response shape for ATB — deserialize and map once the API contract is known
            _ = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("ATB store location response mapping is not implemented yet; returning no locations");
            return Array.Empty<StoreLocationDto>();
        }
    }
}

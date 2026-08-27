using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;

namespace PriceSaver.Server.StoreLocations
{
    public sealed class SilpoStoreLocationProvider : IStoreLocationProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly ILogger<SilpoStoreLocationProvider> _logger;

        public SilpoStoreLocationProvider(
            HttpClient httpClient,
            IOptions<StoreLocationSourcesOptions> options,
            ILogger<SilpoStoreLocationProvider> logger)
        {
            _httpClient = httpClient;
            _baseUrl = options.Value.Silpo.BaseUrl.TrimEnd('/') + "/";
            _logger = logger;
        }

        public async Task<IReadOnlyList<StoreLocationDto>> GetLocationsAsync(CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(_baseUrl, ct);
            response.EnsureSuccessStatusCode();

            // TODO: confirm response shape for Silpo — deserialize and map once the API contract is known
            _ = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Silpo store location response mapping is not implemented yet; returning no locations");
            return Array.Empty<StoreLocationDto>();
        }
    }
}

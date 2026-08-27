using Microsoft.AspNetCore.Mvc;
using PriceSaver.Server.Extensions;
using PriceSaver.Server.Models;
using PriceSaver.Server.Services;
using PriceSaver.Server.StoreLocations;

namespace PriceSaver.Server.Controllers
{
    // TODO: add auth once decided — external scheduler may need X-Api-Key / admin auth,
    // or network-level restriction may be enough for now.
    [ApiController]
    [Route("api/locations")]
    public class LocationsController : ControllerBase
    {
        private readonly StoreLocationRefreshService _refreshService;
        private readonly IStoreLocationProvider _atbProvider;
        private readonly IStoreLocationProvider _silpoProvider;
        private readonly IStoreLocationProvider _metroProvider;
        private readonly ILogger<LocationsController> _logger;

        public LocationsController(
            StoreLocationRefreshService refreshService,
            [FromKeyedServices("atb")] IStoreLocationProvider atbProvider,
            [FromKeyedServices("silpo")] IStoreLocationProvider silpoProvider,
            [FromKeyedServices("metro")] IStoreLocationProvider metroProvider,
            ILogger<LocationsController> logger)
        {
            _refreshService = refreshService;
            _atbProvider = atbProvider;
            _silpoProvider = silpoProvider;
            _metroProvider = metroProvider;
            _logger = logger;
        }

        [HttpPost("refresh/atb")]
        public Task<IActionResult> RefreshAtb(CancellationToken cancellationToken) =>
            RefreshStoreAsync(StoreType.ATB, _atbProvider, cancellationToken);

        [HttpPost("refresh/silpo")]
        public Task<IActionResult> RefreshSilpo(CancellationToken cancellationToken) =>
            RefreshStoreAsync(StoreType.Silpo, _silpoProvider, cancellationToken);

        [HttpPost("refresh/metro")]
        public Task<IActionResult> RefreshMetro(CancellationToken cancellationToken) =>
            RefreshStoreAsync(StoreType.Metro, _metroProvider, cancellationToken);

        private async Task<IActionResult> RefreshStoreAsync(
            StoreType storeType,
            IStoreLocationProvider provider,
            CancellationToken cancellationToken)
        {
            var storeLabel = storeType.GetApiLabel();
            _logger.LogInformation("Store location refresh started for {Store}", storeLabel);

            try
            {
                var result = await _refreshService.RefreshAsync(provider, storeType, cancellationToken);

                _logger.LogInformation(
                    "Store location refresh succeeded for {Store}: added={Added}, updated={Updated}, durationMs={DurationMs}",
                    storeLabel,
                    result.Added,
                    result.Updated,
                    result.DurationMs);

                return Ok(new
                {
                    store = storeLabel,
                    added = result.Added,
                    updated = result.Updated,
                    durationMs = result.DurationMs
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsUpstreamFailure(ex))
            {
                _logger.LogError(ex, "Store location refresh failed for {Store} due to upstream API error", storeLabel);
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = $"Failed to fetch locations from {storeLabel} API.",
                    store = storeLabel
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Store location refresh failed for {Store} unexpectedly", storeLabel);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    error = $"Unexpected error refreshing {storeLabel} locations.",
                    store = storeLabel
                });
            }
        }

        private static bool IsUpstreamFailure(Exception ex) =>
            ex is HttpRequestException or TaskCanceledException or TimeoutException;
    }
}

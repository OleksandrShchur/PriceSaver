using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.StoreLocations;

namespace PriceSaver.Server.Services
{
    public sealed class StoreLocationRefreshService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<StoreLocationRefreshService> _logger;

        public StoreLocationRefreshService(
            ApplicationDbContext db,
            ILogger<StoreLocationRefreshService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Upserts locations by (StoreType, ExternalId). Does not delete or deactivate
        /// rows missing from the provider response.
        /// TODO: if soft-deactivation is chosen, mark missing ExternalIds as inactive here.
        /// </summary>
        public async Task<StoreLocationRefreshResult> RefreshAsync(
            IStoreLocationProvider provider,
            StoreType storeType,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var locations = await provider.GetLocationsAsync(cancellationToken);

            var existing = await _db.StoreLocations
                .Where(s => s.StoreType == storeType && s.ExternalId != null)
                .ToListAsync(cancellationToken);

            var byExternalId = existing.ToDictionary(s => s.ExternalId!, StringComparer.Ordinal);

            var added = 0;
            var updated = 0;
            var now = DateTime.UtcNow;

            foreach (var dto in locations)
            {
                if (string.IsNullOrWhiteSpace(dto.ExternalId))
                {
                    _logger.LogWarning(
                        "Skipping location with empty ExternalId for {StoreType}: {Name}",
                        storeType,
                        dto.Name);
                    continue;
                }

                var lat = (decimal)dto.Latitude;
                var lon = (decimal)dto.Longitude;

                if (byExternalId.TryGetValue(dto.ExternalId, out var row))
                {
                    row.LocationName = dto.Name;
                    row.Latitude = lat;
                    row.Longitude = lon;
                    row.LastRefreshedAt = now;
                    updated++;
                }
                else
                {
                    _db.StoreLocations.Add(new StoreLocation
                    {
                        LocationName = dto.Name,
                        Latitude = lat,
                        Longitude = lon,
                        StoreType = storeType,
                        IsOnline = false,
                        ExternalId = dto.ExternalId,
                        LastRefreshedAt = now
                    });
                    added++;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            sw.Stop();

            return new StoreLocationRefreshResult(
                Store: storeType,
                Added: added,
                Updated: updated,
                TotalProcessed: added + updated,
                DurationMs: sw.ElapsedMilliseconds);
        }
    }

    public record StoreLocationRefreshResult(
        StoreType Store,
        int Added,
        int Updated,
        int TotalProcessed,
        long DurationMs);
}

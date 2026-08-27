using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PriceSaver.Server.Models;
using PriceSaver.Server.Services;
using PriceSaver.Server.StoreLocations;
using PriceSaver.Server.Tests.Helpers;

namespace PriceSaver.Server.Tests.Services
{
    public class StoreLocationRefreshServiceTests
    {
        [Fact]
        public async Task RefreshAsync_InsertsNewLocations_AndUpdatesExistingByExternalId()
        {
            await using var db = TestDbContextFactory.CreateInMemory();
            db.StoreLocations.Add(new StoreLocation
            {
                LocationName = "Old name",
                Latitude = 50.0m,
                Longitude = 30.0m,
                StoreType = StoreType.ATB,
                IsOnline = false,
                ExternalId = "ext-1",
                LastRefreshedAt = DateTime.UtcNow.AddDays(-7)
            });
            await db.SaveChangesAsync();

            var provider = new FakeStoreLocationProvider(
            [
                new StoreLocationDto("ext-1", "Updated ATB", 50.45, 30.52),
                new StoreLocationDto("ext-2", "New ATB", 49.84, 24.03)
            ]);

            var sut = new StoreLocationRefreshService(db, NullLogger<StoreLocationRefreshService>.Instance);

            var result = await sut.RefreshAsync(provider, StoreType.ATB, CancellationToken.None);

            result.Added.Should().Be(1);
            result.Updated.Should().Be(1);
            result.TotalProcessed.Should().Be(2);

            var rows = await db.StoreLocations.Where(s => s.StoreType == StoreType.ATB).ToListAsync();
            rows.Should().HaveCount(2);

            var updated = rows.Single(r => r.ExternalId == "ext-1");
            updated.LocationName.Should().Be("Updated ATB");
            updated.Latitude.Should().Be(50.45m);
            updated.Longitude.Should().Be(30.52m);
            updated.LastRefreshedAt.Should().NotBeNull();

            var added = rows.Single(r => r.ExternalId == "ext-2");
            added.LocationName.Should().Be("New ATB");
            added.IsOnline.Should().BeFalse();
        }

        [Fact]
        public async Task RefreshAsync_DoesNotDeleteLocationsMissingFromProviderResponse()
        {
            await using var db = TestDbContextFactory.CreateInMemory();
            db.StoreLocations.Add(new StoreLocation
            {
                LocationName = "Stale ATB",
                Latitude = 50.0m,
                Longitude = 30.0m,
                StoreType = StoreType.ATB,
                ExternalId = "gone",
                IsOnline = false
            });
            await db.SaveChangesAsync();

            var provider = new FakeStoreLocationProvider(
            [
                new StoreLocationDto("still-here", "Still here", 50.1, 30.1)
            ]);

            var sut = new StoreLocationRefreshService(db, NullLogger<StoreLocationRefreshService>.Instance);
            await sut.RefreshAsync(provider, StoreType.ATB, CancellationToken.None);

            (await db.StoreLocations.CountAsync()).Should().Be(2);
            (await db.StoreLocations.AnyAsync(s => s.ExternalId == "gone")).Should().BeTrue();
        }

        private sealed class FakeStoreLocationProvider : IStoreLocationProvider
        {
            private readonly IReadOnlyList<StoreLocationDto> _locations;

            public FakeStoreLocationProvider(IReadOnlyList<StoreLocationDto> locations) =>
                _locations = locations;

            public Task<IReadOnlyList<StoreLocationDto>> GetLocationsAsync(CancellationToken ct) =>
                Task.FromResult(_locations);
        }
    }
}

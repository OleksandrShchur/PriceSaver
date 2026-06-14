using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Data;

namespace PriceSaver.Server.Tests.Helpers
{
    /// <summary>
    /// Helpers for spinning up isolated <see cref="ApplicationDbContext"/> instances
    /// backed by the EF Core in-memory provider.
    /// </summary>
    public static class TestDbContextFactory
    {
        public static ApplicationDbContext CreateInMemory(string? databaseName = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
                .EnableSensitiveDataLogging()
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}

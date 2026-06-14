using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PriceSaver.Server.Data;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Services;

namespace PriceSaver.Server.Tests.Helpers
{
    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> that rewires the app for
    /// integration testing: EF Core in-memory database, a recording Telegram
    /// service, and a configurable fake parser. No real network or SQL Server
    /// dependency is required.
    /// </summary>
    public sealed class PriceSaverWebApplicationFactory : WebApplicationFactory<Program>
    {
        public const string JobsSecret = "integration-test-secret";

        public string DatabaseName { get; } = $"it-{Guid.NewGuid()}";

        public RecordingTelegramService Telegram { get; } = new();

        public FakePriceParser Parser { get; } = new(
            canParse: url => url.Contains("example", StringComparison.OrdinalIgnoreCase),
            parse: (_, _) => Task.FromResult(("Integration Product", 100m)));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Server=(localdb);Database=ignored;",
                    ["Jobs:SecretKey"] = JobsSecret,
                    ["Telegram:BotToken"] = string.Empty,
                    ["Telegram:MaxSubscriptionsPerUser"] = "50",
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // Swap SQL Server for the in-memory provider.
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase(DatabaseName));

                // Swap the real Telegram client for an in-memory recorder.
                services.RemoveAll<ITelegramService>();
                services.AddSingleton<ITelegramService>(Telegram);

                // Replace the real parsers with an offline fake.
                services.RemoveAll<IPriceParser>();
                services.AddSingleton<IPriceParser>(Parser);
            });
        }

        public ApplicationDbContext CreateDbContext()
        {
            var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public void SeedDb(Action<ApplicationDbContext> seed)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            seed(db);
            db.SaveChanges();
        }
    }
}

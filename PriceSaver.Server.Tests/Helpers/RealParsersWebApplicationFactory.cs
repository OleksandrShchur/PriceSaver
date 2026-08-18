using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PriceSaver.Server.Data;
using PriceSaver.Server.Services;

namespace PriceSaver.Server.Tests.Helpers
{
    /// <summary>
    /// Like <see cref="PriceSaverWebApplicationFactory"/> but keeps the real
    /// <c>IPriceParser</c> HttpClient registrations from Program.cs so DI
    /// collisions (shared client name / Referer) can be caught in tests.
    /// </summary>
    public sealed class RealParsersWebApplicationFactory : WebApplicationFactory<Program>
    {
        public string DatabaseName { get; } = $"it-parsers-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Server=(localdb);Database=ignored;",
                    ["Jobs:SecretKey"] = PriceSaverWebApplicationFactory.JobsSecret,
                    ["Telegram:BotToken"] = string.Empty,
                    ["Telegram:MaxSubscriptionsPerUser"] = "50",
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseInMemoryDatabase(DatabaseName));

                services.RemoveAll<ITelegramService>();
                services.AddSingleton<ITelegramService, RecordingTelegramService>();

                services.RemoveAll<ITelegramAlertService>();
                services.AddSingleton<ITelegramAlertService, NoOpTelegramAlertService>();
            });
        }
    }
}

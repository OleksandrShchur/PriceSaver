using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Data;
using PriceSaver.Server.Extensions;
using PriceSaver.Server.Handlers;
using PriceSaver.Server.Middleware;
using PriceSaver.Server.Options;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Services;
using Serilog;
using Serilog.Events;
using System.Net;

try
{
    var builder = WebApplication.CreateBuilder(args);

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console(
            restrictedToMinimumLevel: LogEventLevel.Information,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} | {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: builder.Configuration["Logging:FilePath"] ?? "logs/pricesaver-.txt",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            restrictedToMinimumLevel: LogEventLevel.Debug,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u5}] [{ThreadId}] {SourceContext} | {Message:lj}{NewLine}{Exception}")
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .CreateLogger();

    builder.Host.UseSerilog();

    // Add services to the container.
    builder.Services.AddControllers();

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    builder.Services
        .AddOptions<TelegramOptions>()
        .Bind(builder.Configuration.GetSection(TelegramOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services
        .AddOptions<JobsOptions>()
        .Bind(builder.Configuration.GetSection(JobsOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // Database
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection")));

    // HTTP Clients for parsers

    builder.Services.AddHttpClient<IPriceParser, AtbPriceParser>();

    builder.Services.AddHttpClient<IPriceParser, SilpoPriceParser>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/125.0.0.0 Safari/537.36");

        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/json, text/plain, */*");

        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(
            "uk-UA,uk;q=0.9");

        client.DefaultRequestHeaders.Add("Origin", "https://silpo.ua");
        client.DefaultRequestHeaders.Add("Referer", "https://silpo.ua/");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    });

    builder.Services.AddHttpClient<IPriceParser, MaudauPriceParser>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36");

        client.DefaultRequestHeaders.Accept.ParseAdd(
            "application/json, text/plain, */*");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All
    });

    builder.Services.AddSingleton<ITelegramService, TelegramService>();
    builder.Services.AddSingleton<ITelegramAlertService, TelegramAlertService>();

    // Register handlers
    builder.Services.AddScoped<ITelegramUpdateHandler, TelegramUpdateHandler>();
    builder.Services.AddScoped<ISubscriptionHandler, SubscriptionHandler>();

    // Register services
    builder.Services.AddScoped<PriceCheckerService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

    var app = builder.Build();

    app.UseMiddleware<GlobalExceptionMiddleware>();

    app.UseDefaultFiles();
    app.MapStaticAssets();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.MapFallbackToFile("/index.html");

    var botDisplayName = builder.Configuration["Telegram:BotDisplayName"] ?? "PriceSaver";
    Log.Information("Telegram bot started. BotUsername: @{Username}", botDisplayName);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly during startup");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed for integration testing with WebApplicationFactory<Program>.
public partial class Program { }

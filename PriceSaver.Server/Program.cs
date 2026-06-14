using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Data;
using PriceSaver.Server.Extensions;
using PriceSaver.Server.Handlers;
using PriceSaver.Server.Options;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Services;
using Serilog;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSingleton<IPriceParser, AtbPriceParser>();

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

// Register handlers
builder.Services.AddScoped<ITelegramUpdateHandler, TelegramUpdateHandler>();
builder.Services.AddScoped<ISubscriptionHandler, SubscriptionHandler>();

// Register services
builder.Services.AddScoped<PriceCheckerService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// Logging
builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console());

var app = builder.Build();

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

app.Run();

// Exposed for integration testing with WebApplicationFactory<Program>.
public partial class Program { }

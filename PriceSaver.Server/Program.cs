using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// configuration for DB and services
builder.Services.AddDbContext<PriceSaver.Server.Data.ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register parsers
builder.Services.AddSingleton<PriceSaver.Server.Parsers.IPriceParser, PriceSaver.Server.Parsers.AtbPriceParser>();
builder.Services.AddSingleton<PriceSaver.Server.Parsers.IPriceParser, PriceSaver.Server.Parsers.SilpoPriceParser>();
builder.Services.AddSingleton<PriceSaver.Server.Parsers.IPriceParser, PriceSaver.Server.Parsers.MetroPriceParser>();
builder.Services.AddSingleton<PriceSaver.Server.Parsers.IPriceParser, PriceSaver.Server.Parsers.EpicentrPriceParser>();

// Telegram bot hosted service
builder.Services.Configure<PriceSaver.Server.Services.TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.AddSingleton<PriceSaver.Server.Services.ITelegramService, PriceSaver.Server.Services.TelegramBotHostedService>();
builder.Services.AddHostedService<PriceSaver.Server.Services.TelegramBotHostedService>(sp => (PriceSaver.Server.Services.TelegramBotHostedService)sp.GetRequiredService<PriceSaver.Server.Services.ITelegramService>());

// Price checker
builder.Services.AddScoped<PriceSaver.Server.Services.PriceCheckerService>();

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

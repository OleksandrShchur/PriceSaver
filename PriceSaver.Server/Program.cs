using Microsoft.EntityFrameworkCore;
using PriceSaver.Server.Handlers;
using PriceSaver.Server.Options;
using PriceSaver.Server.Parsers;
using PriceSaver.Server.Services;
using Serilog;

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
    .Bind(builder.Configuration.GetSection(JobsOptions.SectionName));

// configuration for DB and services
builder.Services.AddDbContext<PriceSaver.Server.Data.ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register parsers
builder.Services.AddSingleton<IPriceParser, AtbPriceParser>();
builder.Services.AddSingleton<IPriceParser, SilpoPriceParser>();
builder.Services.AddSingleton<IPriceParser, MetroPriceParser>();
builder.Services.AddSingleton<IPriceParser, EpicentrPriceParser>();

// Telegram bot hosted service
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddSingleton<ITelegramService>(sp => sp.GetRequiredService<TelegramService>());

// Register handlers
builder.Services.AddScoped<ITelegramUpdateHandler, TelegramUpdateHandler>();
builder.Services.AddScoped<ISubscriptionHandler, SubscriptionHandler>();

// Register services
builder.Services.AddScoped<PriceCheckerService>();
builder.Services.AddScoped<IUserService, UserService>();

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

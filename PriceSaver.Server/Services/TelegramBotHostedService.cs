using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using PriceSaver.Server.Data;
using PriceSaver.Server.Models;
using PriceSaver.Server.Parsers;

namespace PriceSaver.Server.Services
{
    public class TelegramOptions
    {
        public string BotToken { get; set; } = string.Empty;
    }

    public class TelegramBotHostedService : BackgroundService, ITelegramService
    {
        private readonly ILogger<TelegramBotHostedService> _logger;
        private readonly TelegramOptions _options;
        private readonly ApplicationDbContext _db;
        private readonly IPriceParser[] _parsers;
        private ITelegramBotClient? _client;

        public TelegramBotHostedService(ILogger<TelegramBotHostedService> logger, IOptions<TelegramOptions> options, ApplicationDbContext db, IPriceParser[] parsers)
        {
            _logger = logger;
            _options = options.Value;
            _db = db;
            _parsers = parsers;
        }

        public async Task SendMessageAsync(long chatId, string text)
        {
            if (_client == null) return;
            await _client.SendTextMessageAsync(chatId, text);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_options.BotToken))
            {
                _logger.LogWarning("Telegram bot token not configured; bot disabled.");
                return Task.CompletedTask;
            }

            _client = new TelegramBotClient(_options.BotToken);
            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
            _client.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, stoppingToken: stoppingToken);
            _logger.LogInformation("Telegram bot started");

            return Task.CompletedTask;
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            if (update.Type != UpdateType.Message || update.Message!.Type != MessageType.Text) return;

            var msg = update.Message;
            var chatId = msg.Chat.Id;
            var text = msg.Text?.Trim() ?? string.Empty;

            if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
            {
                await botClient.SendTextMessageAsync(chatId, "Welcome to PriceSaver! Send a product link to create a subscription. Use /my_subscriptions to manage them.");
                // ensure user exists
                var user = await _db.Users.FindAsync(new object[] { chatId }, ct);
                if (user == null)
                {
                    _db.Users.Add(new User { TelegramId = chatId, Username = msg.From?.Username });
                    await _db.SaveChangesAsync(ct);
                }
                return;
            }

            if (text.StartsWith("/my_subscriptions", StringComparison.OrdinalIgnoreCase))
            {
                var subs = _db.Subscriptions.Where(s => s.UserId == chatId && s.IsActive).ToList();
                if (!subs.Any())
                {
                    await botClient.SendTextMessageAsync(chatId, "You have no active subscriptions.");
                }
                else
                {
                    var lines = subs.Select(s => $"{s.Id} - {s.ProductName} - {s.CurrentPrice} UAH\n{ s.ProductUrl }");
                    await botClient.SendTextMessageAsync(chatId, string.Join("\n\n", lines));
                }
                return;
            }

            // Attempt to detect a product link
            if (Uri.IsWellFormedUriString(text, UriKind.Absolute))
            {
                var url = text;
                var parser = _parsers.FirstOrDefault(p => p.CanParse(url));
                if (parser == null)
                {
                    await botClient.SendTextMessageAsync(chatId, "Sorry, that store is not supported or the link couldn't be recognized.");
                    return;
                }

                try
                {
                    var (name, price) = await parser.ParseAsync(url, ct);
                    var sub = new Subscription
                    {
                        UserId = chatId,
                        ProductUrl = url,
                        StoreType = InferStoreType(parser.StoreKey),
                        ProductName = name,
                        CurrentPrice = price,
                        LastCheckedDate = DateTime.UtcNow
                    };

                    _db.Subscriptions.Add(sub);
                    await _db.SaveChangesAsync(ct);

                    await botClient.SendTextMessageAsync(chatId, $"Subscription created:\n{name}\nPrice: {price} UAH\n{url}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse product link");
                    await botClient.SendTextMessageAsync(chatId, "Failed to parse product details from the link.");
                }
            }
        }

        private static StoreType InferStoreType(string key) => key.ToLower() switch
        {
            "atb" => StoreType.ATB,
            "silpo" => StoreType.Silpo,
            "metro" => StoreType.Metro,
            "epicentr" => StoreType.Epicentr,
            _ => StoreType.Unknown
        };

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
        {
            _logger.LogError(exception, "Telegram bot error");
            return Task.CompletedTask;
        }
    }
}

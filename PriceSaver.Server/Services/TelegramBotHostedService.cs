using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PriceSaver.Server.Services
{
    public class TelegramBotHostedService : BackgroundService
    {
        private readonly ILogger<TelegramBotHostedService> _logger;
        private readonly TelegramOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TelegramService _telegram;

        public TelegramBotHostedService(
            ILogger<TelegramBotHostedService> logger,
            IOptions<TelegramOptions> options,
            IServiceScopeFactory scopeFactory,
            TelegramService telegram)
        {
            _logger = logger;
            _options = options.Value;
            _scopeFactory = scopeFactory;
            _telegram = telegram;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.EnablePolling)
            {
                _logger.LogInformation("Telegram polling is disabled. Use the webhook callback at {WebhookPath}.", _options.WebhookPath);
                return Task.CompletedTask;
            }

            var client = _telegram.Client;
            if (client is null)
            {
                _logger.LogWarning("Telegram bot token not configured; bot disabled.");
                return Task.CompletedTask;
            }

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
            client.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, stoppingToken);
            _logger.LogInformation("Telegram polling started");

            return Task.CompletedTask;
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ITelegramUpdateHandler>();
            await handler.HandleAsync(update, cancellationToken);
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Telegram bot error");
            return Task.CompletedTask;
        }
    }
}

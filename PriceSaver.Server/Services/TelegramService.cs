using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using Telegram.Bot;

namespace PriceSaver.Server.Services
{
    public class TelegramService : ITelegramService
    {
        private readonly TelegramOptions _options;
        private readonly ILogger<TelegramService> _logger;
        private readonly Lazy<ITelegramBotClient?> _client;

        public TelegramService(IOptions<TelegramOptions> options, ILogger<TelegramService> logger)
        {
            _options = options.Value;
            _logger = logger;
            _client = new Lazy<ITelegramBotClient?>(CreateClient);
        }

        public ITelegramBotClient? Client => _client.Value;

        public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            if (Client is null)
            {
                _logger.LogWarning("Telegram bot token is not configured; message to chat {ChatId} was skipped.", chatId);

                return;
            }

            await Client.SendTextMessageAsync(chatId, text, cancellationToken: cancellationToken);
        }

        private ITelegramBotClient? CreateClient()
        {
            if (string.IsNullOrWhiteSpace(_options.BotToken))
            {
                return null;
            }

            return new TelegramBotClient(_options.BotToken);
        }
    }
}

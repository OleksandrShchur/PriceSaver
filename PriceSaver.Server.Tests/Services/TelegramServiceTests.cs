using Microsoft.Extensions.Options;
using PriceSaver.Server.Options;
using PriceSaver.Server.Services;
using PriceSaver.Server.Tests.Helpers;
using Telegram.Bot.Types.ReplyMarkups;

namespace PriceSaver.Server.Tests.Services
{
    public class TelegramServiceTests
    {
        private static TelegramService CreateWithoutToken()
        {
            var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions { BotToken = string.Empty });
            return new TelegramService(options, new TestLogger<TelegramService>());
        }

        [Fact]
        public void Client_IsNull_WhenBotTokenMissing()
        {
            var sut = CreateWithoutToken();

            sut.Client.Should().BeNull();
        }

        [Fact]
        public async Task AllSendMethods_AreNoOps_WhenBotTokenMissing()
        {
            var sut = CreateWithoutToken();
            var keyboard = new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton("x") } });

            var act = async () =>
            {
                await sut.SendMessageAsync(1, "hello");
                await sut.SendMessageWithKeyboardAsync(1, "hello", keyboard);
                await sut.SendMessageWithInlineButtonAsync(1, "hello", "label", "data");
                await sut.DeleteMessageAsync(1, 2);
                await sut.AnswerCallbackQueryAsync("cbq", "text");
            };

            await act.Should().NotThrowAsync();
        }
    }
}

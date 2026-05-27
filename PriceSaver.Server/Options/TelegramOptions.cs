using System.ComponentModel.DataAnnotations;

namespace PriceSaver.Server.Options
{
    public class TelegramOptions
    {
        public const string SectionName = "Telegram";

        public string BotToken { get; set; } = string.Empty;

        public string BotDisplayName { get; set; } = "PriceSaver";

        [Range(1, 100)]
        public int MaxSubscriptionsPerUser { get; set; } = 50;
    }
}

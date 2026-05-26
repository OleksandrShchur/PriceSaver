namespace PriceSaver.Server.Models
{
    public class User
    {
        public long TelegramId { get; set; }
        public string? Username { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

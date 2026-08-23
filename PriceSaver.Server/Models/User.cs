namespace PriceSaver.Server.Models
{
    public class User
    {
        public long TelegramId { get; set; }
        public string? Username { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? LocationName { get; set; }
        public DateTime? LocationUpdatedAt { get; set; }
        public string ConversationState { get; set; } = ConversationStates.None;
        public string? ConversationPayload { get; set; }
    }
}

using System;

namespace PriceSaver.Server.Models
{
    public class PriceHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SubscriptionId { get; set; }
        public decimal Price { get; set; }
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    }
}

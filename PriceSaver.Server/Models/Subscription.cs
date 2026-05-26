using System;

namespace PriceSaver.Server.Models
{
    public class Subscription
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public long UserId { get; set; }
        public string ProductUrl { get; set; } = null!;
        public StoreType StoreType { get; set; }
        public string? ProductName { get; set; }
        public decimal CurrentPrice { get; set; }
        public DateTime? LastCheckedDate { get; set; }
        public bool IsActive { get; set; } = true;
        public bool NotifyOnIncrease { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

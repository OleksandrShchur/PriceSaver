namespace PriceSaver.Server.Models
{
    public class MaudauMarket
    {
        public long Id { get; set; }

        public long ProductId { get; set; }

        public string Slug { get; set; } = null!;

        public string Title { get; set; } = null!;

        public decimal Price { get; set; }

        public decimal OldPrice { get; set; }

        public DateTime LastUpdatedUtc { get; set; }
    }
}

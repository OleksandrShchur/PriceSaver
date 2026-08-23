namespace PriceSaver.Server.Models
{
    public class StoreLocation
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string LocationName { get; set; } = null!;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public StoreType StoreType { get; set; }
        public bool IsOnline { get; set; }
    }
}

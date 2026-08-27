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

        /// <summary>Store's own identifier for upsert; null for static rows (e.g. Maudau).</summary>
        public string? ExternalId { get; set; }

        public DateTime? LastRefreshedAt { get; set; }

        // TODO: consider IsActive (soft-deactivate) for locations that disappear from a store API
        // rather than leaving stale rows forever. Do not delete-and-reinsert on refresh.
    }
}

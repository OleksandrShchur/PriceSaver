namespace PriceSaver.Server.Models
{
    public sealed class GeocodingResult
    {
        public required string DisplayName { get; init; }
        public required decimal Latitude { get; init; }
        public required decimal Longitude { get; init; }
    }
}

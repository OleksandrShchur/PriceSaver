namespace PriceSaver.Server.StoreLocations
{
    public interface IStoreLocationProvider
    {
        Task<IReadOnlyList<StoreLocationDto>> GetLocationsAsync(CancellationToken ct);
    }

    public record StoreLocationDto(
        string ExternalId,
        string Name,
        double Latitude,
        double Longitude);
}

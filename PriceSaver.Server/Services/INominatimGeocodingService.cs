using PriceSaver.Server.Models;

namespace PriceSaver.Server.Services
{
    public interface INominatimGeocodingService
    {
        Task<IReadOnlyList<GeocodingResult>> SearchAsync(string query, CancellationToken cancellationToken);

        Task<GeocodingResult?> ReverseAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken);
    }
}

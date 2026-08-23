using System.ComponentModel.DataAnnotations;

namespace PriceSaver.Server.Options
{
    public class NominatimOptions
    {
        public const string SectionName = "Nominatim";

        [Required]
        [Url]
        public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

        /// <summary>Identifying User-Agent required by Nominatim usage policy.</summary>
        [Required]
        [MinLength(3)]
        public string UserAgent { get; set; } = "PriceSaver/1.0 (https://github.com/OleksandrShchur/PriceSaver)";

        /// <summary>Minimum interval between outbound Nominatim requests (seconds).</summary>
        [Range(0.5, 60)]
        public double MinRequestIntervalSeconds { get; set; } = 1.0;

        [Range(1, 10)]
        public int SearchResultLimit { get; set; } = 3;
    }
}

using System.Text.Json.Serialization;

namespace PriceSaver.Server.DTOs
{
    /// <summary>
    /// The <c>offer</c> object embedded in a Maudau product.
    /// Monetary values are returned as integers in the smallest currency unit
    /// (e.g. 49400 == 494.00 UAH).
    /// </summary>
    public class MaudauOfferDto
    {
        [JsonPropertyName("price")]
        public long Price { get; set; }

        [JsonPropertyName("old_price")]
        public long OldPrice { get; set; }

        [JsonPropertyName("stock")]
        public int Stock { get; set; }

        [JsonPropertyName("availability_status")]
        public string? AvailabilityStatus { get; set; }
    }
}

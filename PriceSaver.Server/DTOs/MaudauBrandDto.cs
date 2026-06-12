using System.Text.Json.Serialization;

namespace PriceSaver.Server.DTOs
{
    /// <summary>
    /// The <c>brand</c> object embedded in a Maudau product.
    /// </summary>
    public class MaudauBrandDto
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
    }
}

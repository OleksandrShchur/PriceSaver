using System.Text.Json.Serialization;

namespace PriceSaver.Server.DTOs
{
    /// <summary>
    /// Strongly typed DTO matching a single product entry returned by
    /// https://backend.prod.maudau.click/v1/user/products/searches.
    /// </summary>
    public class MaudauProductDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("reviews_count")]
        public int ReviewsCount { get; set; }

        [JsonPropertyName("rating")]
        public string? Rating { get; set; }

        [JsonPropertyName("is_age_confirmed")]
        public bool IsAgeConfirmed { get; set; }

        [JsonPropertyName("main_category_slug")]
        public string? MainCategorySlug { get; set; }

        [JsonPropertyName("offer")]
        public MaudauOfferDto? Offer { get; set; }

        [JsonPropertyName("brand")]
        public MaudauBrandDto? Brand { get; set; }
    }
}

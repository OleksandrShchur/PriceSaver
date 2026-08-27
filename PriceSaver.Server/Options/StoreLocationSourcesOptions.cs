using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace PriceSaver.Server.Options
{
    public class StoreLocationSourcesOptions
    {
        public const string SectionName = "StoreLocationSources";

        [Required]
        [ValidateObjectMembers]
        public StoreLocationSourceEntry Atb { get; set; } = new();

        [Required]
        [ValidateObjectMembers]
        public StoreLocationSourceEntry Silpo { get; set; } = new();

        [Required]
        [ValidateObjectMembers]
        public StoreLocationSourceEntry Metro { get; set; } = new();
    }

    public class StoreLocationSourceEntry
    {
        [Required]
        [Url]
        public string BaseUrl { get; set; } = string.Empty;
    }
}

using System.Text.Json.Serialization;

namespace JEWELLBISREACT.Model
{
    public class ItemGroupCreation
    {
        [JsonPropertyName("fitemCode")]
        public string? fitemCode { get; set; }

        [JsonPropertyName("subGroup")]
        public string? subGroup { get; set; }

        [JsonPropertyName("mainGroup")]
        public string? mainGroup { get; set; }

        [JsonPropertyName("FAclevel")]
        public string? FAclevel { get; set; }

        /// <summary>Image filename stored in DB (e.g. "C_00012.jpg")</summary>
        [JsonPropertyName("fImage")]
        public string? fImage { get; set; }

        /// <summary>Availability — "1" = available, "0" = not available</summary>
        [JsonPropertyName("fShow")]
        public string? fShow { get; set; }
    }
}

using System.Text.Json.Serialization;

namespace JEWELLBISREACT.Model
{
    public class Counter_Creation
    {
        [JsonPropertyName("fcode")]
        public string Fcode { get; set; }

        [JsonPropertyName("fbox")]
        public string Fbox { get; set; }

        [JsonPropertyName("fboxwt")]
        public string FboxWt { get; set; }

        [JsonPropertyName("ftagwt")]
        public string FTagWt { get; set; }
    }
}

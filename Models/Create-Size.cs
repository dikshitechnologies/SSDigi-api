using System.Text.Json.Serialization;

namespace JEWELLBISREACT.Model
{
    public class Create_Size
    {
        [JsonPropertyName("fcode")]
        public string Fcode { get; set; }

        [JsonPropertyName("fsize")]
        public string Fsize { get; set; }
    }
}

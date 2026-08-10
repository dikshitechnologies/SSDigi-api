using System.Text.Json.Serialization;

namespace CHITSCHEME.Models
{
    public class Rate_Fixing
    {
        public class RateFixing
        {
            [JsonPropertyName("fcode")]
            public string FCODE { get; set; }
            [JsonPropertyName("fname")]
            public string FNAME { get; set; }
            [JsonPropertyName("frate")]
            public string FRATE { get; set; }
        }

        public class OldRateFix
        {
            [JsonPropertyName("fold_gold_va")]
            public string FOLDGOLDVA { get; set; }

            [JsonPropertyName("fold_gold_dust")]
            public string FOLDGOLDDUST { get; set; }

            [JsonPropertyName("fold_gold_rate")]
            public string FOLDGOLDRATE { get; set; }

            [JsonPropertyName("fold_silver_va")]
            public string FOLDSILVERVA { get; set; }

            [JsonPropertyName("fold_silver_dust")]
            public string FOLDSILVERDUST { get; set; }

            [JsonPropertyName("fold_silver_rate")]
            public string FOLDSILVERRATE { get; set; }
        }

        //---------------------------------Use this for both GET (response) and PUT/POST (request) if needed----------------------------
        public class RateFixingData
        {
            [JsonPropertyName("divisionData")]
            public List<RateFixing> DivisionData { get; set; } = new();

            [JsonPropertyName("rateFixData")]
            public List<OldRateFix> RateFixData { get; set; } = new();
        }

        // -----------------------------------------------------Use this for structured update requests------------------------------------------------
        public class FullRateFixingRequest
        {
            [JsonPropertyName("division")]
            public List<RateFixing> Division { get; set; }

            [JsonPropertyName("rateFix")]
            public OldRateFix RateFix { get; set; }
        }


    }
}

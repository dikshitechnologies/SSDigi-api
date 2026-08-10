using System.Text.Json.Serialization;

namespace CHITSCHEME.Models
{
    public class ChitSchemeModel
    {
        public List<SchemeList> SchemeDetails { get; set; }

        /// <summary>1 = user came via referral, 0 = normal</summary>
        public int HasReferral { get; set; }

        /// <summary>RegisterUsers.UserID of the person enrolling</summary>
        public string UserId { get; set; }

        /// <summary>
        /// The referrer's UserID returned by /ApplyReferral validation.
        /// Required when HasReferral = 1.
        /// </summary>
        public string ReferrerId { get; set; }
    }



    public class SchemeList
    {
        public string CusCode { get; set; }
        public string SchemeCode { get; set; }
        public string Amount { get; set; }
        public string FDUE { get; set; }
        public string TotalAmt { get; set; }
        public string CompCode { get; set; }
        [JsonPropertyName("weight")]
        public string Weight { get; set; }

        public string fbwt { get; set; }
        public string fbamt { get; set; }
        public string fbfinalamt { get; set; }
        public string finalwt { get; set; }
        public string FGRATE { get; set; }


    }
}

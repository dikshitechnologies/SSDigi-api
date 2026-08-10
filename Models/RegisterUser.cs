using Newtonsoft.Json;

namespace CHITSCHEME.Models
{
    public class RegisterUser
    {
        [JsonProperty("Firstname")]
        public string Firstname { get; set; }

        [JsonProperty("Email")]
        public string Email { get; set; }

        [JsonProperty("Phonenumber")]
        public string Phonenumber { get; set; }

        [JsonProperty("FcmToken")]
        public string FcmToken { get; set; }

        [JsonProperty("DeviceType")]
        public string DeviceType { get; set; }

        /// <summary>
        /// Optional: Referral code entered by the new user during registration.
        /// </summary>
        [JsonProperty("ReferCode")]
        public string? ReferCode { get; set; }
    }
}

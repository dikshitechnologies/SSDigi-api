using Azure;
using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GatewayController : ControllerBase
    {
        private readonly string key = "1J1KBYKL3Z";   // Use your Easebuzz key
        private readonly string salt = "0R1WPX2WVA";  // Use your Easebuzz salt
        private readonly string apiUrl = "https://pay.easebuzz.in/payment/initiateLink";

        [HttpPost("Initiate")]
        public async Task<IActionResult> InitiatePayment([FromBody] PaymentRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request");

            var txnid = $"TXN{DateTime.UtcNow.Ticks}";

            // Hash format as per Easebuzz for Initiate
            string hashString = $"{key}|{txnid}|{request.Amount}|{request.ProductInfo}|{request.FirstName}|{request.Email}|||||||||||{salt}";
            string hash = GenerateHash512(hashString);

            var postData = new Dictionary<string, string>
            {
                { "key", key },
                { "txnid", txnid },
                { "amount", request.Amount },
                { "firstname", request.FirstName },
                { "email", request.Email },
                { "phone", request.Phone },
                { "productinfo", request.ProductInfo },
                { "surl", request.Surl },
                { "furl", request.Furl }, 
                { "udf1", "" }, { "udf2", "" }, { "udf3", "" }, { "udf4", "" }, { "udf5", "" },
                { "udf6", "" }, { "udf7", "" }, { "udf8", "" }, { "udf9", "" }, { "udf10", "" },
                { "hash", hash }
            };

            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(postData);
            var response = await client.PostAsync(apiUrl, content);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var statusProp) && statusProp.GetInt32() == 1)
            {
                var token = root.GetProperty("data").GetString();
                string paymentUrl = $"https://pay.easebuzz.in/pay/{token}";

                return Ok(new { status = "ok", txnid, payment_url = paymentUrl , token = token });
            }

            string message = root.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "Unknown error";
            return BadRequest(new { status = "failed", message, response = json });
        }


        private string GenerateHash512(string text)
        {
            using var sha512 = SHA512.Create();
            var hash = sha512.ComputeHash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }


        [HttpPost("Verify")]
        public async Task<IActionResult> VerifyPayment([FromBody] PaymentVerifyRequest request)
        {
            try
            {
                var payload = new Dictionary<string, string>
                {
                    { "key", "1J1KBYKL3Z" },
                    { "txnid", request.TxnId },
                    { "hash", request.Hash } // optional if required
                };

                using var client = new HttpClient();
                var response = await client.PostAsync("https://dashboard.easebuzz.in/transaction/v2.1/retrieve",
                    new FormUrlEncodedContent(payload));

                var json = await response.Content.ReadAsStringAsync();
                return Ok(JsonDocument.Parse(json));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = false, error = ex.Message });
            }
        }

        public class PaymentVerifyRequest
        {
            public string TxnId { get; set; }
            public string Hash { get; set; } // optional
        }

        public class PaymentResponse
        {
            public string status { get; set; }
            public string txnid { get; set; }
            public string amount { get; set; }
            public string productinfo { get; set; }
            public string firstname { get; set; }
            public string email { get; set; }
            public string udf1 { get; set; }
            public string udf2 { get; set; }
            public string udf3 { get; set; }
            public string udf4 { get; set; }
            public string udf5 { get; set; }
            public string udf6 { get; set; }
            public string udf7 { get; set; }
            public string udf8 { get; set; }
            public string udf9 { get; set; }
            public string udf10 { get; set; }
            public string hash { get; set; }
        }




    }

    public class PaymentRequest
        {
            public string Amount { get; set; }
            public string FirstName { get; set; }
            public string Email { get; set; }
            public string Phone { get; set; }
            public string ProductInfo { get; set; }
            public string Surl { get; set; }
            public string Furl { get; set; }
        }
   
}

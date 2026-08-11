using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using JEWELLBISREACT.DBConnection;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class OmniPaymentController : ControllerBase
    {
        private const string API_KEY           = "bb3eae66-3902-412e-85eb-7f681a8a4080";
        private const string SALT              = "fd8bb2fc7d3780445d9f51ba22ee3996cb655085";
        private const string PG_CREATE_URL     = "https://pgbiz.omniware.in/v2/getpaymentrequesturl";
        private const string PG_STATUS_URL     = "https://pgbiz.omniware.in/v2/paymentstatus";
        private const string PG_EXPIRE_URL     = "https://pgbiz.omniware.in/v2/expirepaymentrequesturl";

        private readonly IHttpClientFactory _httpFactory;

        public OmniPaymentController(IHttpClientFactory httpFactory)
        {
            _httpFactory = httpFactory;
        }

        // ── 1. CREATE ORDER ──────────────────────────────────────────────────────
        [HttpPost("create-order")]
        public async Task<IActionResult> CreateOrder([FromBody] OmniOrderRequest req)
        {
            if (req == null)
                return BadRequest(new { message = "Request body is required" });

            if (!decimal.TryParse(req.Amount, System.Globalization.NumberStyles.Any,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out decimal amountValue))
                return BadRequest(new { message = "Invalid amount format" });

            if (amountValue <= 0)
                return BadRequest(new { message = "Amount must be greater than 0" });

            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest(new { message = "Name is required" });

            if (string.IsNullOrWhiteSpace(req.Email))
                return BadRequest(new { message = "Email is required" });

            if (string.IsNullOrWhiteSpace(req.Phone))
                return BadRequest(new { message = "Phone is required" });

            // Auto-generate unique OrderId — same pattern as Razorpay order["id"]
            string orderId = "OMN" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // All PG-specific fields handled here — user sends only what they know
            var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "api_key",            API_KEY },
                { "order_id",           orderId },
                { "mode",               "LIVE" },
                { "amount",             amountValue.ToString("F2") },
                { "currency",           "INR" },
                { "description",        string.IsNullOrWhiteSpace(req.Description) ? "Payment" : req.Description },
                { "name",               req.Name },
                { "email",              req.Email },
                { "phone",              req.Phone },
                { "city",               "Chennai" },
                { "state",              "Tamil Nadu" },
                { "country",            "IND" },
                { "zip_code",           "600001" },
                { "return_url",         $"{Request.Scheme}://{Request.Host}/api/OmniPayment/record" },
                { "return_url_failure", $"{Request.Scheme}://{Request.Host}/api/OmniPayment/record" },
                { "return_url_cancel",  $"{Request.Scheme}://{Request.Host}/api/OmniPayment/record" },
            };

            parameters["hash"] = ComputeHash(parameters);

            try
            {
                var client   = _httpFactory.CreateClient("OmniPG");
                var response = await client.PostAsync(PG_CREATE_URL,
                                   new FormUrlEncodedContent(parameters));

                string body = await response.Content.ReadAsStringAsync();

                // PG sometimes returns HTML error pages — catch before JSON parse
                if (!body.TrimStart().StartsWith("{"))
                    return StatusCode(502, new
                    {
                        message    = "PG returned an unexpected response (not JSON)",
                        httpStatus = (int)response.StatusCode,
                        rawBody    = body
                    });

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode,
                        new { message = "PG request failed", detail = body });

                using var doc = JsonDocument.Parse(body);
                var root      = doc.RootElement;

                if (root.TryGetProperty("error", out var err))
                    return BadRequest(new
                    {
                        message = "Payment Gateway error",
                        code    = err.GetProperty("code").GetRawText(),
                        detail  = err.GetProperty("message").GetString()
                    });

                var data = root.GetProperty("data");

                return Ok(new
                {
                    orderId        = orderId,
                    paymentUrl     = data.GetProperty("url").GetString(),
                    uuid           = data.GetProperty("uuid").GetString(),
                    expiryDatetime = data.TryGetProperty("expiry_datetime", out var exp)
                                        ? exp.GetString() : null,
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error creating order", error = ex.Message });
            }
        }

        // ── 2. VERIFY PAYMENT ────────────────────────────────────────────────────
        [HttpPost("verify-payment")]
        public async Task<IActionResult> VerifyPayment([FromBody] OmniVerifyRequest req)
        {
            try
            {
                if (req == null ||
                    (string.IsNullOrWhiteSpace(req.OrderId) &&
                     string.IsNullOrWhiteSpace(req.TransactionId)))
                {
                    return BadRequest(new { message = "OrderId or TransactionId is required" });
                }

                var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    { "api_key", API_KEY }
                };

                if (!string.IsNullOrWhiteSpace(req.OrderId))
                    parameters["order_id"] = req.OrderId;

                if (!string.IsNullOrWhiteSpace(req.TransactionId))
                    parameters["transaction_id"] = req.TransactionId;

                parameters["hash"] = ComputeHash(parameters);

                var client   = _httpFactory.CreateClient("OmniPG");
                var response = await client.PostAsync(PG_STATUS_URL,
                                   new FormUrlEncodedContent(parameters));

                string body = await response.Content.ReadAsStringAsync();

                if (!body.TrimStart().StartsWith("{"))
                    return StatusCode(502, new
                    {
                        message    = "PG returned an unexpected response (not JSON)",
                        httpStatus = (int)response.StatusCode,
                        rawBody    = body
                    });

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode,
                        new { message = "PG status check failed", detail = body });

                using var doc = JsonDocument.Parse(body);
                var root      = doc.RootElement;

                if (root.TryGetProperty("error", out var err))
                    return BadRequest(new
                    {
                        message = "Payment Gateway error",
                        code    = err.GetProperty("code").GetRawText(),
                        detail  = err.GetProperty("message").GetString()
                    });

                return Ok(new { status = "success", data = JsonSerializer.Deserialize<object>(root.GetRawText()) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error", error = ex.Message });
            }
        }

        // ── 3. RECORD  (PG posts here after payment — return_url) ───────────────
        [AllowAnonymous]
        [HttpPost("record")]
        public IActionResult RecordPayment([FromForm] OmniRecordModel model)
        {
            try
            {
                if (model == null)
                    return BadRequest(new { message = "Empty payload" });

                // Verify hash before trusting the response
                var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);

                void Add(string key, string? val)
                { if (!string.IsNullOrEmpty(val)) parameters[key] = val; }

                Add("transaction_id",   model.TransactionId);
                Add("payment_mode",     model.PaymentMode);
                Add("payment_channel",  model.PaymentChannel);
                Add("payment_datetime", model.PaymentDatetime);
                Add("response_code",    model.ResponseCode);
                Add("response_message", model.ResponseMessage);
                Add("order_id",         model.OrderId);
                Add("amount",           model.Amount);
                Add("currency",         model.Currency);
                Add("description",      model.Description);
                Add("name",             model.Name);
                Add("email",            model.Email);
                Add("phone",            model.Phone);
                Add("city",             model.City);
                Add("state",            model.State);
                Add("country",          model.Country);
                Add("zip_code",         model.ZipCode);
                Add("udf1",             model.Udf1);
                Add("udf2",             model.Udf2);
                Add("udf3",             model.Udf3);
                Add("udf4",             model.Udf4);
                Add("udf5",             model.Udf5);

                if (!string.IsNullOrWhiteSpace(model.Hash))
                {
                    string expected = ComputeHash(parameters);
                    if (!string.Equals(expected, model.Hash, StringComparison.OrdinalIgnoreCase))
                        return BadRequest(new { status = "error", message = "Hash mismatch. Possible tampering." });
                }

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    string query = @"
                        INSERT INTO OmniPaymentRecords
                        (OrderId, TransactionId, ResponseCode, ResponseMessage, Amount,
                         PaymentMode, PaymentChannel, PaymentDatetime, Name, Email, Phone, CreatedAt)
                        VALUES
                        (@OrderId, @TransactionId, @ResponseCode, @ResponseMessage, @Amount,
                         @PaymentMode, @PaymentChannel, @PaymentDatetime, @Name, @Email, @Phone, GETDATE())";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@OrderId",         model.OrderId         ?? "");
                        cmd.Parameters.AddWithValue("@TransactionId",   model.TransactionId   ?? "");
                        cmd.Parameters.AddWithValue("@ResponseCode",    model.ResponseCode    ?? "");
                        cmd.Parameters.AddWithValue("@ResponseMessage", model.ResponseMessage ?? "");
                        cmd.Parameters.AddWithValue("@Amount",          model.Amount          ?? "");
                        cmd.Parameters.AddWithValue("@PaymentMode",     model.PaymentMode     ?? "");
                        cmd.Parameters.AddWithValue("@PaymentChannel",  model.PaymentChannel  ?? "");
                        cmd.Parameters.AddWithValue("@PaymentDatetime", model.PaymentDatetime ?? "");
                        cmd.Parameters.AddWithValue("@Name",            model.Name            ?? "");
                        cmd.Parameters.AddWithValue("@Email",           model.Email           ?? "");
                        cmd.Parameters.AddWithValue("@Phone",           model.Phone           ?? "");

                        con.Open();
                        cmd.ExecuteNonQuery();
                        con.Close();
                    }
                }

                return Ok(new
                {
                    status         = model.ResponseCode == "0" ? "success" : "failed",
                    message        = model.ResponseMessage,
                    transactionId  = model.TransactionId,
                    orderId        = model.OrderId,
                    amount         = model.Amount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "failed", message = ex.Message });
            }
        }

        // ── 4. VERIFY (expire a payment URL) ────────────────────────────────────
        [HttpPost("verify")]
        public async Task<IActionResult> Verify([FromBody] OmniExpireRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Uuid))
                return BadRequest(new { message = "Uuid is required" });

            var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "api_key", API_KEY },
                { "uuid",    req.Uuid }
            };

            parameters["hash"] = ComputeHash(parameters);

            try
            {
                var client   = _httpFactory.CreateClient("OmniPG");
                var response = await client.PostAsync(PG_EXPIRE_URL,
                                   new FormUrlEncodedContent(parameters));

                string body = await response.Content.ReadAsStringAsync();

                if (!body.TrimStart().StartsWith("{"))
                    return StatusCode(502, new
                    {
                        message    = "PG returned an unexpected response (not JSON)",
                        httpStatus = (int)response.StatusCode,
                        rawBody    = body
                    });

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode,
                        new { message = "PG expire request failed", detail = body });

                using var doc = JsonDocument.Parse(body);
                return Ok(new { message = "Saved", data = JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText()) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error", error = ex.Message });
            }
        }

        // ── HASH HELPER (Appendix 2 — SHA512 | SALT | sorted values | UPPERCASE) ─
        private static string ComputeHash(SortedDictionary<string, string> parameters)
        {
            // Sort keys case-insensitively (a,A,b,B... not A,B,a,b)
            var sorted = parameters
                .Where(k => k.Key != "hash" && !string.IsNullOrEmpty(k.Value))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            sb.Append(SALT);

            foreach (var kvp in sorted)
                sb.Append('|').Append(kvp.Value.Trim());

            using var sha = SHA512.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())))
                               .Replace("-", "").ToUpper();
        }

        // ── DEBUG — inspect exactly what is posted to PG (remove in production) ─
        [HttpPost("debug-payload")]
        public IActionResult DebugPayload([FromBody] OmniOrderRequest req)
        {
            if (req == null) return BadRequest();

            if (!decimal.TryParse(req.Amount, System.Globalization.NumberStyles.Any,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out decimal amountValue))
                return BadRequest(new { message = "Invalid amount format" });

            string orderId = "OMN" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "api_key",            API_KEY },
                { "order_id",           orderId },
                { "mode",               "LIVE" },
                { "amount",             amountValue.ToString("F2") },
                { "currency",           "INR" },
                { "description",        string.IsNullOrWhiteSpace(req.Description) ? "Payment" : req.Description },
                { "name",               req.Name },
                { "email",              req.Email },
                { "phone",              req.Phone },
                { "city",               "Chennai" },
                { "state",              "Tamil Nadu" },
                { "country",            "IND" },
                { "zip_code",           "600001" },
                { "return_url",         $"{Request.Scheme}://{Request.Host}/api/OmniPayment/record" },
                { "return_url_failure", $"{Request.Scheme}://{Request.Host}/api/OmniPayment/record" },
                { "return_url_cancel",  $"{Request.Scheme}://{Request.Host}/api/OmniPayment/record" },
            };

            string hash = ComputeHash(parameters);
            parameters["hash"] = hash;

            // Show the hash string before hashing for verification
            var sorted = parameters
                .Where(k => k.Key != "hash" && !string.IsNullOrEmpty(k.Value))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string hashInput = SALT + string.Concat(sorted.Select(k => "|" + k.Value.Trim()));

            return Ok(new
            {
                postUrl     = PG_CREATE_URL,
                parameters  = parameters,
                hashInput   = hashInput,
                computedHash= hash
            });
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // MODELS  — only what the user needs to send
    // ════════════════════════════════════════════════════════════════════════════

    // 1. create-order
    public class OmniOrderRequest
    {
        public string  Amount      { get; set; }   // e.g. "250.00"
        public string  Name        { get; set; }   // customer name
        public string  Email       { get; set; }   // customer email
        public string  Phone       { get; set; }   // customer phone
        public string? Description { get; set; }   // optional — default: "Payment"
    }

    // 2. verify-payment
    public class OmniVerifyRequest
    {
        public string? OrderId       { get; set; }  // pass OrderId
        public string? TransactionId { get; set; }  // or TransactionId
    }

    // 3. record  — posted by PG as form data (user never calls this directly)
    public class OmniRecordModel
    {
        [FromForm(Name = "transaction_id")]   public string? TransactionId   { get; set; }
        [FromForm(Name = "payment_mode")]     public string? PaymentMode     { get; set; }
        [FromForm(Name = "payment_channel")]  public string? PaymentChannel  { get; set; }
        [FromForm(Name = "payment_datetime")] public string? PaymentDatetime { get; set; }
        [FromForm(Name = "response_code")]    public string? ResponseCode    { get; set; }
        [FromForm(Name = "response_message")] public string? ResponseMessage { get; set; }
        [FromForm(Name = "error_desc")]       public string? ErrorDesc       { get; set; }
        [FromForm(Name = "order_id")]         public string? OrderId         { get; set; }
        [FromForm(Name = "amount")]           public string? Amount          { get; set; }
        [FromForm(Name = "currency")]         public string? Currency        { get; set; }
        [FromForm(Name = "description")]      public string? Description     { get; set; }
        [FromForm(Name = "name")]             public string? Name            { get; set; }
        [FromForm(Name = "email")]            public string? Email           { get; set; }
        [FromForm(Name = "phone")]            public string? Phone           { get; set; }
        [FromForm(Name = "address_line_1")]   public string? AddressLine1    { get; set; }
        [FromForm(Name = "address_line_2")]   public string? AddressLine2    { get; set; }
        [FromForm(Name = "city")]             public string? City            { get; set; }
        [FromForm(Name = "state")]            public string? State           { get; set; }
        [FromForm(Name = "country")]          public string? Country         { get; set; }
        [FromForm(Name = "zip_code")]         public string? ZipCode         { get; set; }
        [FromForm(Name = "udf1")]             public string? Udf1            { get; set; }
        [FromForm(Name = "udf2")]             public string? Udf2            { get; set; }
        [FromForm(Name = "udf3")]             public string? Udf3            { get; set; }
        [FromForm(Name = "udf4")]             public string? Udf4            { get; set; }
        [FromForm(Name = "udf5")]             public string? Udf5            { get; set; }
        [FromForm(Name = "hash")]             public string? Hash            { get; set; }
    }

    // 4. verify (expire URL)
    public class OmniExpireRequest
    {
        public string Uuid { get; set; }  // UUID returned by create-order
    }
}

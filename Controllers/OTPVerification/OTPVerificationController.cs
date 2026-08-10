using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.OTPVerification
{
    [Route("api/[controller]")]
    [ApiController]
    public class OTPVerificationController : ControllerBase
    {
        // SMS gateway constants
        private const string SmsKey        = "6dad4e29de7c4fcf3ec27b96f44c5934";
        private const string SmsSender     = "DIKTEC";
        private const string SmsTemplateId = "1607100000000385126";
        private const string SmsBaseUrl    = "https://site.ping4sms.com/api/smsapi";

        // Demo credential – Apple App Store review account
        private const string DemoPhoneNumber = "9999999999";
        private const string DemoOtp         = "123456";

        // POST api/OTPVerification/send-otp
        [AllowAnonymous]
        [HttpPost("send-otp")]
        public async Task<IActionResult> SendOtp([FromBody] SendOtpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Phone) || request.Phone.Length != 10)
                return BadRequest(new { message = "A valid 10-digit phone number is required." });

            bool isDemoAccount = request.Phone == DemoPhoneNumber;

            try
            {
                // Fixed OTP for demo account; random for everyone else
                string otp     = isDemoAccount ? DemoOtp : new Random().Next(100000, 999999).ToString();
                DateTime expiry = DateTime.Now.AddMinutes(10);

                // ── 1. Save OTP to DB ────────────────────────────────────────────
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Upsert: update existing unverified row for this phone, or insert a new one
                var cmd = new SqlCommand(@"
                    IF EXISTS (
                        SELECT 1 FROM OTPVerification
                        WHERE Phone = @phone AND IsVerified = 0
                    )
                        UPDATE OTPVerification
                        SET OTP          = @otp,
                            ExpiryTime   = @expiryTime,
                            IsVerified   = 0,
                            VerifiedTime = NULL
                        WHERE Phone = @phone AND IsVerified = 0
                    ELSE
                        INSERT INTO OTPVerification (Phone, OTP, ExpiryTime, IsVerified, VerifiedTime)
                        VALUES (@phone, @otp, @expiryTime, 0, NULL);
                ", connection);

                cmd.Parameters.AddWithValue("@phone", request.Phone);
                cmd.Parameters.AddWithValue("@otp", otp);
                cmd.Parameters.AddWithValue("@expiryTime", expiry);

                await cmd.ExecuteNonQueryAsync();

                // ── 2. Send SMS (skipped for demo account) ──────────────────────
                string smsStatus = "skipped";
                string smsResult = "Demo account – no SMS sent.";

                if (!isDemoAccount)
                {
                    string smsMessage = Uri.EscapeDataString(
                        $"Dear Customer, Your OTP for Pukhraj Elite Jewellers is {otp}. This OTP is valid for 10 minutes. Please do not share this OTP with anyone -DIKSHI");

                    string smsUrl = $"{SmsBaseUrl}" +
                                    $"?key={SmsKey}" +
                                    $"&route=2" +
                                    $"&sender={SmsSender}" +
                                    $"&number={request.Phone}" +
                                    $"&sms={smsMessage}" +
                                    $"&templateid={SmsTemplateId}";

                    using var httpClient = new HttpClient();
                    var smsResponse = await httpClient.GetAsync(smsUrl);
                    smsResult = await smsResponse.Content.ReadAsStringAsync();
                    smsStatus = smsResponse.IsSuccessStatusCode ? "delivered" : "failed";
                }

                return Ok(new
                {
                    message    = "OTP sent successfully.",
                    phone      = request.Phone,
                    expiryTime = expiry,
                    smsStatus,
                    smsGatewayResponse = smsResult
                });
            }
            catch (SqlException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Database error. Please try again later." });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An unexpected error occurred. Please try again later." });
            }
        }

        // POST api/OTPVerification/verify-otp
        [AllowAnonymous]
        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Phone) || request.Phone.Length != 10)
                return BadRequest(new { message = "A valid 10-digit phone number is required." });

            if (string.IsNullOrWhiteSpace(request.OTP))
                return BadRequest(new { message = "OTP is required." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Fetch the latest unverified OTP for this phone
                var selectCmd = new SqlCommand(@"
                    SELECT TOP 1 Id, OTP, ExpiryTime, IsVerified
                    FROM OTPVerification
                    WHERE Phone = @phone
                    ORDER BY Id DESC
                ", connection);

                selectCmd.Parameters.AddWithValue("@phone", request.Phone);

                int    otpId             = 0;
                string storedOtp         = null;
                DateTime expiryTime      = DateTime.MinValue;
                bool isAlreadyVerified   = false;

                using (var reader = await selectCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        otpId             = Convert.ToInt32(reader["Id"]);
                        storedOtp         = reader["OTP"].ToString();
                        expiryTime        = Convert.ToDateTime(reader["ExpiryTime"]);
                        isAlreadyVerified = Convert.ToBoolean(reader["IsVerified"]);
                    }
                }

                if (storedOtp == null)
                    return NotFound(new { message = "No OTP found. Please request a new OTP." });

                if (isAlreadyVerified)
                    return BadRequest(new { message = "OTP has already been used. Please request a new OTP." });

                if (DateTime.Now > expiryTime)
                    return BadRequest(new { message = "OTP has expired. Please request a new OTP." });

                if (storedOtp != request.OTP)
                    return BadRequest(new { message = "Invalid OTP. Please try again." });

                // Mark as verified
                var updateCmd = new SqlCommand(@"
                    UPDATE OTPVerification
                    SET IsVerified   = 1,
                        VerifiedTime = @verifiedTime
                    WHERE Id = @id
                ", connection);

                updateCmd.Parameters.AddWithValue("@verifiedTime", DateTime.Now);
                updateCmd.Parameters.AddWithValue("@id", otpId);

                await updateCmd.ExecuteNonQueryAsync();

                return Ok(new
                {
                    message = "OTP verified successfully.",
                    phone   = request.Phone
                });
            }
            catch (SqlException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Database error. Please try again later." });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An unexpected error occurred. Please try again later." });
            }
        }
    }

    public class SendOtpRequest
    {
        public string Phone { get; set; }
    }

    public class VerifyOtpRequest
    {
        public string Phone { get; set; }
        public string OTP   { get; set; }
    }
}

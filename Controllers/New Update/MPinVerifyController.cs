using CHITSCHEME.Helpers;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

namespace CHITSCHEME_SSDigi.Controllers.New_Update
{
    [Route("api/auth")]
    [ApiController]
    public class MPinVerifyController : ControllerBase
    {
        private readonly IConfiguration _config;

        public MPinVerifyController(IConfiguration config)
        {
            _config = config;
        }
       
        // ================== 2. Send OTP ==================
        [AllowAnonymous]
        [HttpPost("sendotp")]
        public async Task<IActionResult> SendOTP([FromBody] SendOTPRequest request)
        {
            if (request.UserId <= 0)
                return BadRequest(new { success = false, message = "UserId is required." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if user exists and get phone number
                var userCmd = new SqlCommand(@"
                    SELECT PhoneNumber FROM RegisterUsers WHERE UserId = @UserId", connection);
                userCmd.Parameters.AddWithValue("@UserId", request.UserId);

                string phoneNumber = null;
                using (var reader = await userCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        phoneNumber = reader["PhoneNumber"].ToString();
                    }
                }

                if (string.IsNullOrEmpty(phoneNumber))
                    return NotFound(new { success = false, message = "User not found." });

                // Demo account: Apple Store review number — fixed OTP, no SMS
                bool isDemoAccount = phoneNumber == "9999999999";

                // Generate 6-digit OTP
                string otp = isDemoAccount ? "123456" : new Random().Next(100000, 999999).ToString();
                DateTime otpExpiry = DateTime.Now.AddMinutes(10);

                // Check if an active OTP already exists for this phone
                var checkOtpCmd = new SqlCommand(@"
                    SELECT Id FROM OTPVerification 
                    WHERE Phone = @Phone AND IsVerified = 0 AND ExpiryTime > GETDATE()", connection);
                checkOtpCmd.Parameters.AddWithValue("@Phone", phoneNumber);

                var existingOtpId = await checkOtpCmd.ExecuteScalarAsync();

                if (existingOtpId != null)
                {
                    // Update existing OTP record
                    var updateOtpCmd = new SqlCommand(@"
                        UPDATE OTPVerification 
                        SET OTP = @OTP, ExpiryTime = @ExpiryTime, IsVerified = 0, VerifiedTime = NULL
                        WHERE Id = @Id", connection);
                    updateOtpCmd.Parameters.AddWithValue("@OTP", otp);
                    updateOtpCmd.Parameters.AddWithValue("@ExpiryTime", otpExpiry);
                    updateOtpCmd.Parameters.AddWithValue("@Id", existingOtpId);
                    await updateOtpCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    // Insert new OTP record
                    var insertOtpCmd = new SqlCommand(@"
                        INSERT INTO OTPVerification (Phone, OTP, ExpiryTime, IsVerified, VerifiedTime)
                        VALUES (@Phone, @OTP, @ExpiryTime, 0, NULL)", connection);
                    insertOtpCmd.Parameters.AddWithValue("@Phone", phoneNumber);
                    insertOtpCmd.Parameters.AddWithValue("@OTP", otp);
                    insertOtpCmd.Parameters.AddWithValue("@ExpiryTime", otpExpiry);
                    await insertOtpCmd.ExecuteNonQueryAsync();
                }

                // Demo account: skip SMS, OTP is always 123456
                if (!isDemoAccount)
                {
                    // Send OTP via ping4sms gateway
                    // Template: Dear Customer, Your OTP for {#var#} is {#var#}. This OTP is valid for 10 minutes. Please do not share this OTP with anyone -DIKSHI
                    string smsMessage = Uri.EscapeDataString(
                        $"Dear Customer, Your OTP for SSDigi is {otp}. This OTP is valid for 10 minutes. Please do not share this OTP with anyone -DIKSHI");

                    string smsUrl = $"https://site.ping4sms.com/api/smsapi" +
                        $"?key=6dad4e29de7c4fcf3ec27b96f44c5934" +
                        $"&route=2" +
                        $"&sender=DIKTEC" +
                        $"&number={phoneNumber}" +
                        $"&sms={smsMessage}" +
                        $"&templateid=1607100000000385126";

                    using var httpClient = new System.Net.Http.HttpClient();
                    var smsResponse = await httpClient.GetAsync(smsUrl);

                    if (!smsResponse.IsSuccessStatusCode)
                    {
                        return StatusCode(StatusCodes.Status502BadGateway, new
                        {
                            success = false,
                            message = "Failed to send OTP via SMS. Please try again."
                        });
                    }
                }

                return Ok(new
                {
                    success = true,
                    message = "OTP sent successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = "An error occurred while sending OTP.",
                    error = ex.Message
                });
            }
        }

        // ================== 3. Verify OTP ==================
        [AllowAnonymous]
        [HttpPost("verifyotp")]
        public async Task<IActionResult> VerifyOTP([FromBody] VerifyOTPRequest request)
        {
            if (request.UserId <= 0)
                return BadRequest(new { success = false, message = "UserId is required." });

            if (string.IsNullOrWhiteSpace(request.OTP))
                return BadRequest(new { success = false, message = "OTP is required." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Get phone number and MPIN status for this user
                var userCmd = new SqlCommand(@"
                    SELECT PhoneNumber, IsMPINEnabled FROM RegisterUsers WHERE UserId = @UserId", connection);
                userCmd.Parameters.AddWithValue("@UserId", request.UserId);

                string phoneNumber = null;
                bool isMPINEnabled = false;

                using (var reader = await userCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        phoneNumber = reader["PhoneNumber"].ToString();
                        isMPINEnabled = reader["IsMPINEnabled"] != DBNull.Value && Convert.ToBoolean(reader["IsMPINEnabled"]);
                    }
                }

                if (string.IsNullOrEmpty(phoneNumber))
                    return NotFound(new { success = false, message = "User not found." });

                // Look up the latest unverified OTP for this phone
                var otpCmd = new SqlCommand(@"
                    SELECT TOP 1 Id, OTP, ExpiryTime 
                    FROM OTPVerification 
                    WHERE Phone = @Phone AND IsVerified = 0
                    ORDER BY Id DESC", connection);
                otpCmd.Parameters.AddWithValue("@Phone", phoneNumber);

                int otpId = 0;
                string storedOTP = null;
                DateTime? otpExpiry = null;

                using (var reader = await otpCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        otpId = Convert.ToInt32(reader["Id"]);
                        storedOTP = reader["OTP"]?.ToString();
                        otpExpiry = reader["ExpiryTime"] != DBNull.Value ? (DateTime?)reader["ExpiryTime"] : null;
                    }
                }

                if (otpId == 0 || string.IsNullOrEmpty(storedOTP))
                    return BadRequest(new { success = false, message = "No OTP found. Please request a new OTP." });

                if (otpExpiry.HasValue && DateTime.Now > otpExpiry.Value)
                    return BadRequest(new { success = false, message = "OTP has expired. Please request a new OTP." });

                // Verify OTP
                if (storedOTP != request.OTP)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid OTP."
                    });
                }

                // Mark OTP as verified
                var verifyCmd = new SqlCommand(@"
                    UPDATE OTPVerification 
                    SET IsVerified = 1, VerifiedTime = @VerifiedTime
                    WHERE Id = @Id", connection);
                verifyCmd.Parameters.AddWithValue("@VerifiedTime", DateTime.Now);
                verifyCmd.Parameters.AddWithValue("@Id", otpId);
                await verifyCmd.ExecuteNonQueryAsync();

                // Determine next step
                string nextStep = isMPINEnabled ? "ENTER_MPIN" : "CREATE_MPIN";

                return Ok(new
                {
                    success = true,
                    message = "OTP verified successfully.",
                    nextStep = nextStep
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = "An error occurred while verifying OTP.",
                    error = ex.Message
                });
            }
        }

        // ================== 4. Create MPIN ==================
        [AllowAnonymous]
        [HttpPost("creatempin")]
        public async Task<IActionResult> CreateMPIN([FromBody] CreateMPINRequest request)
        {
            if (request.UserId <= 0)
                return BadRequest(new { success = false, message = "UserId is required." });

            if (string.IsNullOrWhiteSpace(request.MPIN) || request.MPIN.Length != 4)
                return BadRequest(new { success = false, message = "MPIN must be 4 digits." });

            if (request.MPIN != request.ConfirmMPIN)
                return BadRequest(new { success = false, message = "MPIN and Confirm MPIN do not match." });

            if (!System.Text.RegularExpressions.Regex.IsMatch(request.MPIN, @"^\d{4}$"))
                return BadRequest(new { success = false, message = "MPIN must contain only digits." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Check if user already has MPIN
                var checkCmd = new SqlCommand(@"
                    SELECT IsMPINEnabled FROM RegisterUsers WHERE UserId = @UserId", connection);
                checkCmd.Parameters.AddWithValue("@UserId", request.UserId);

                bool isMPINEnabled = false;
                using (var reader = await checkCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        isMPINEnabled = reader["IsMPINEnabled"] != DBNull.Value && Convert.ToBoolean(reader["IsMPINEnabled"]);
                    }
                }

                if (isMPINEnabled)
                    return BadRequest(new { success = false, message = "MPIN already exists. Use reset MPIN to change it." });

                // Hash MPIN
                string mpinHash = HashMPIN(request.MPIN);

                // Store MPIN
                var updateCmd = new SqlCommand(@"
                    UPDATE RegisterUsers 
                    SET MPINHash = @MPINHash, 
                        IsMPINEnabled = 1, 
                        DeviceId = @DeviceId,
                        FailedMPINAttempts = 0,
                        MPINLockedUntil = NULL,
                        MPINCreatedAt = @CreatedAt,
                        MPINUpdatedAt = @UpdatedAt
                    WHERE UserId = @UserId", connection);
                updateCmd.Parameters.AddWithValue("@MPINHash", mpinHash);
                updateCmd.Parameters.AddWithValue("@DeviceId", (object)request.DeviceId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                updateCmd.Parameters.AddWithValue("@UserId", request.UserId);
                await updateCmd.ExecuteNonQueryAsync();

                return Ok(new
                {
                    success = true,
                    message = "MPIN created successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = "An error occurred while creating MPIN.",
                    error = ex.Message
                });
            }
        }

        // ================== 5. Verify MPIN (Login) ==================
        [AllowAnonymous]
        [HttpPost("verifympin")]
        public async Task<IActionResult> VerifyMPIN([FromBody] VerifyMPINRequest request)
        {
            if (request.UserId <= 0)
                return BadRequest(new { success = false, message = "UserId is required." });

            if (string.IsNullOrWhiteSpace(request.MPIN) || request.MPIN.Length != 4)
                return BadRequest(new { success = false, message = "MPIN must be 4 digits." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Get user details
                var userCmd = new SqlCommand(@"
                    SELECT UserName, PhoneNumber, Email, MPINHash, FailedMPINAttempts, MPINLockedUntil, DeviceId,
                           CASE WHEN ReferredByUserId IS NOT NULL THEN 1 ELSE 0 END AS HasReferral,
                           ReferId,
                           ISNULL(ReferredByUserId,       '')  AS ReferredByUserId,
                           ISNULL(CONVERT(NVARCHAR,ReferralDate,120), '') AS ReferralDate,
                           ISNULL(ReferralVoucherNo,      '')  AS ReferralVoucherNo,
                           CASE WHEN ReferralEarnedVoucherNo IS NOT NULL THEN 1 ELSE 0 END AS HasReferralEarned,
                           ISNULL(ReferralEarnedVoucherNo, '') AS ReferralEarnedVoucherNo,
                           ISNULL(CONVERT(NVARCHAR,ReferralEarnedDate,120), '') AS ReferralEarnedDate
                    FROM RegisterUsers 
                    WHERE UserId = @UserId", connection);
                userCmd.Parameters.AddWithValue("@UserId", request.UserId);

                string username = null;
                string phone = null;
                string email = null;
                string mpinHash = null;
                int failedAttempts = 0;
                DateTime? lockedUntil = null;
                string storedDeviceId = null;
                bool fisEcatalog = false;
                bool hasReferral = false;
                string referId = null;
                string referredByUserId = null;
                string referralDate = null;
                string referralVoucherNo = null;
                bool hasReferralEarned = false;
                string referralEarnedVoucherNo = null;
                string referralEarnedDate = null;

                using (var reader = await userCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        username       = reader["UserName"].ToString();
                        phone          = reader["PhoneNumber"].ToString();
                        email          = reader["Email"]?.ToString();
                        mpinHash       = reader["MPINHash"]?.ToString();
                        failedAttempts = reader["FailedMPINAttempts"] != DBNull.Value ? Convert.ToInt32(reader["FailedMPINAttempts"]) : 0;
                        lockedUntil    = reader["MPINLockedUntil"] != DBNull.Value ? (DateTime?)reader["MPINLockedUntil"] : null;
                        storedDeviceId = reader["DeviceId"]?.ToString();
                        hasReferral             = Convert.ToBoolean(reader["HasReferral"]);
                        referId                 = reader["ReferId"]?.ToString();
                        referredByUserId        = reader["ReferredByUserId"].ToString();
                        referralDate            = reader["ReferralDate"].ToString();
                        referralVoucherNo       = reader["ReferralVoucherNo"].ToString();
                        hasReferralEarned       = Convert.ToBoolean(reader["HasReferralEarned"]);
                        referralEarnedVoucherNo = reader["ReferralEarnedVoucherNo"].ToString();
                        referralEarnedDate      = reader["ReferralEarnedDate"].ToString();
                    }
                }

                // Get Company Settings
                var companyCmd = new SqlCommand("SELECT TOP 1 fisEcatalog FROM Company", connection);
                var result = await companyCmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                {
                    fisEcatalog = Convert.ToBoolean(result);
                }

                if (string.IsNullOrEmpty(mpinHash))
                    return BadRequest(new { success = false, message = "MPIN not set. Please create MPIN first." });

                // Check if account is locked
                if (lockedUntil.HasValue && DateTime.Now < lockedUntil.Value)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Account is temporarily locked due to too many failed attempts.",
                        lockedUntil = lockedUntil.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                    });
                }

                // If lock period has expired, reset failed attempts
                if (lockedUntil.HasValue && DateTime.Now >= lockedUntil.Value)
                {
                    var resetLockCmd = new SqlCommand(@"
                        UPDATE RegisterUsers 
                        SET FailedMPINAttempts = 0, MPINLockedUntil = NULL 
                        WHERE UserId = @UserId", connection);
                    resetLockCmd.Parameters.AddWithValue("@UserId", request.UserId);
                    await resetLockCmd.ExecuteNonQueryAsync();
                    failedAttempts = 0;
                }

                // Verify MPIN
                string inputHash = HashMPIN(request.MPIN);
                if (mpinHash != inputHash)
                {
                    failedAttempts++;
                    DateTime? newLockedUntil = null;

                    // Lock account after 5 failed attempts for 30 minutes
                    if (failedAttempts >= 5)
                    {
                        newLockedUntil = DateTime.Now.AddMinutes(30);
                    }

                    var updateFailedCmd = new SqlCommand(@"
                        UPDATE RegisterUsers 
                        SET FailedMPINAttempts = @FailedAttempts, 
                            MPINLockedUntil = @LockedUntil 
                        WHERE UserId = @UserId", connection);
                    updateFailedCmd.Parameters.AddWithValue("@FailedAttempts", failedAttempts);
                    updateFailedCmd.Parameters.AddWithValue("@LockedUntil", (object)newLockedUntil ?? DBNull.Value);
                    updateFailedCmd.Parameters.AddWithValue("@UserId", request.UserId);
                    await updateFailedCmd.ExecuteNonQueryAsync();

                    if (failedAttempts >= 5)
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message = "Too many failed attempts. Account locked for 30 minutes.",
                            lockedUntil = newLockedUntil.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                        });
                    }

                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid MPIN.",
                        remainingAttempts = 5 - failedAttempts
                    });
                }

                // MPIN verified successfully
                // Update device ID and reset failed attempts
                var updateSuccessCmd = new SqlCommand(@"
                    UPDATE RegisterUsers 
                    SET FailedMPINAttempts = 0, 
                        MPINLockedUntil = NULL, 
                        DeviceId = @DeviceId,
                        LastLogin = @LastLogin
                    WHERE UserId = @UserId", connection);
                updateSuccessCmd.Parameters.AddWithValue("@DeviceId", (object)request.DeviceId ?? DBNull.Value);
                updateSuccessCmd.Parameters.AddWithValue("@LastLogin", DateTime.Now);
                updateSuccessCmd.Parameters.AddWithValue("@UserId", request.UserId);
                await updateSuccessCmd.ExecuteNonQueryAsync();

                // Generate JWT token
                var token = JwtHelper.GenerateJwtToken(phone, "User", _config);

                return Ok(new
                {
                    success    = true,
                    token      = token,
                    username   = username,
                    phone      = phone,
                    email      = email,
                    userId     = request.UserId,
                    fisEcatalog = fisEcatalog,

                    // ── Referral status ──────────────────────────────────────
                    referId                 = referId,

                    // As referee (User 2 — used someone's refer code)
                    hasReferral             = hasReferral,
                    referredByUserId        = referredByUserId,
                    referralDate            = referralDate,
                    referralVoucherNo       = referralVoucherNo,

                    // As referrer (User 1 — someone used their refer code)
                    hasReferralEarned       = hasReferralEarned,
                    referralEarnedVoucherNo = referralEarnedVoucherNo,
                    referralEarnedDate      = referralEarnedDate
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = "An error occurred while verifying MPIN.",
                    error = ex.Message
                });
            }
        }

        // ================== 6. Reset MPIN (Forgot MPIN) ==================
        [AllowAnonymous]
        [HttpPost("resetmpin")]
        public async Task<IActionResult> ResetMPIN([FromBody] ResetMPINRequest request)
        {
            if (request.UserId <= 0)
                return BadRequest(new { success = false, message = "UserId is required." });

            if (string.IsNullOrWhiteSpace(request.OTP))
                return BadRequest(new { success = false, message = "OTP is required." });

            if (string.IsNullOrWhiteSpace(request.NewMPIN) || request.NewMPIN.Length != 4)
                return BadRequest(new { success = false, message = "New MPIN must be 4 digits." });

            if (request.NewMPIN != request.ConfirmMPIN)
                return BadRequest(new { success = false, message = "New MPIN and Confirm MPIN do not match." });

            if (!System.Text.RegularExpressions.Regex.IsMatch(request.NewMPIN, @"^\d{4}$"))
                return BadRequest(new { success = false, message = "MPIN must contain only digits." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Get phone number for this user
                var userCmdReset = new SqlCommand(@"
                    SELECT PhoneNumber FROM RegisterUsers WHERE UserId = @UserId", connection);
                userCmdReset.Parameters.AddWithValue("@UserId", request.UserId);

                string phoneNumber = null;
                using (var reader = await userCmdReset.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                        phoneNumber = reader["PhoneNumber"].ToString();
                }

                if (string.IsNullOrEmpty(phoneNumber))
                    return NotFound(new { success = false, message = "User not found." });

                // Look up the latest unverified OTP for this phone from OTPVerification table
                var otpCmd = new SqlCommand(@"
                    SELECT TOP 1 Id, OTP, ExpiryTime 
                    FROM OTPVerification 
                    WHERE Phone = @Phone AND IsVerified = 0
                    ORDER BY Id DESC", connection);
                otpCmd.Parameters.AddWithValue("@Phone", phoneNumber);

                int otpId = 0;
                string storedOTP = null;
                DateTime? otpExpiry = null;

                using (var reader = await otpCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        otpId = Convert.ToInt32(reader["Id"]);
                        storedOTP = reader["OTP"]?.ToString();
                        otpExpiry = reader["ExpiryTime"] != DBNull.Value ? (DateTime?)reader["ExpiryTime"] : null;
                    }
                }

                if (otpId == 0 || string.IsNullOrEmpty(storedOTP))
                    return BadRequest(new { success = false, message = "No OTP found. Please request a new OTP." });

                if (otpExpiry.HasValue && DateTime.Now > otpExpiry.Value)
                    return BadRequest(new { success = false, message = "OTP has expired. Please request a new OTP." });

                if (storedOTP != request.OTP)
                    return BadRequest(new { success = false, message = "Invalid OTP." });

                // Mark OTP as verified
                var verifyOtpCmd = new SqlCommand(@"
                    UPDATE OTPVerification 
                    SET IsVerified = 1, VerifiedTime = @VerifiedTime
                    WHERE Id = @Id", connection);
                verifyOtpCmd.Parameters.AddWithValue("@VerifiedTime", DateTime.Now);
                verifyOtpCmd.Parameters.AddWithValue("@Id", otpId);
                await verifyOtpCmd.ExecuteNonQueryAsync();

                // Update MPIN in RegisterUsers
                string newMPINHash = HashMPIN(request.NewMPIN);

                var updateCmd = new SqlCommand(@"
                    UPDATE RegisterUsers 
                    SET MPINHash = @MPINHash, 
                        IsMPINEnabled = 1,
                        DeviceId = @DeviceId,
                        FailedMPINAttempts = 0,
                        MPINLockedUntil = NULL,
                        MPINUpdatedAt = @UpdatedAt
                    WHERE UserId = @UserId", connection);
                updateCmd.Parameters.AddWithValue("@MPINHash", newMPINHash);
                updateCmd.Parameters.AddWithValue("@DeviceId", (object)request.DeviceId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
                updateCmd.Parameters.AddWithValue("@UserId", request.UserId);
                await updateCmd.ExecuteNonQueryAsync();

                return Ok(new
                {
                    success = true,
                    message = "MPIN reset successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    message = "An error occurred while resetting MPIN.",
                    error = ex.Message
                });
            }
        }

        // ================== Helper Method: Hash MPIN ==================
        private string HashMPIN(string mpin)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(mpin);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }

    // ================== Request Models ==================
    public class SendOTPRequest
    {
        public int UserId { get; set; }
    }

    public class VerifyOTPRequest
    {
        public int UserId { get; set; }
        public string OTP { get; set; }
    }

    public class CreateMPINRequest
    {
        public int UserId { get; set; }
        public string MPIN { get; set; }
        public string ConfirmMPIN { get; set; }
        public string DeviceId { get; set; }
    }

    public class VerifyMPINRequest
    {
        public int UserId { get; set; }
        public string MPIN { get; set; }
        public string DeviceId { get; set; }
    }

    public class ResetMPINRequest
    {
        public int UserId { get; set; }
        public string OTP { get; set; }
        public string NewMPIN { get; set; }
        public string ConfirmMPIN { get; set; }
        public string DeviceId { get; set; }
    }
}

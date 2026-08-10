using CHITSCHEME.Helpers;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using static QRCoder.PayloadGenerator;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthRegController : ControllerBase
    {
        private readonly IConfiguration _config;

        public AuthRegController(IConfiguration config)
        {
            _config = config;
        }
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Phone))
                return BadRequest(new { success = false, message = "Phone number is required." });

            if (request.Phone.Length != 10)
                return BadRequest(new { success = false, message = "Phone number must be 10 digits." });

            if (!IsPhoneNumberValid(request.Phone))
                return BadRequest(new { success = false, message = "Invalid phone number format." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // -------- Check Party --------
                string partyName = null;
                string partyPhone = null;

                var partyCmd = new SqlCommand(@"
                    SELECT TOP 1 FACNAME, FPHONE 
                    FROM Party 
                    WHERE faclevel < 0 
                      AND fparent LIKE '0000100044%' 
                      AND fphone = @phone
                    ORDER BY FCODE", connection);
                partyCmd.Parameters.AddWithValue("@phone", request.Phone);

                using (var reader = await partyCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        partyName = reader["FACNAME"].ToString();
                        partyPhone = reader["FPHONE"].ToString();
                    }
                }

                // -------- Check RegisterUsers --------
                string username = string.Empty;
                int userId = 0;
                bool isMPINEnabled = false;
                string deviceId = null;

                var regDetailsCmd = new SqlCommand(@"
                    SELECT UserId, UserName, PhoneNumber, IsMPINEnabled, DeviceId 
                    FROM RegisterUsers 
                    WHERE PhoneNumber = @phone", connection);
                regDetailsCmd.Parameters.AddWithValue("@phone", request.Phone);

                using (var reader = await regDetailsCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        userId = Convert.ToInt32(reader["UserId"]);
                        username = reader["UserName"].ToString();
                        partyPhone = reader["PhoneNumber"].ToString();
                        isMPINEnabled = reader["IsMPINEnabled"] != DBNull.Value && Convert.ToBoolean(reader["IsMPINEnabled"]);
                        deviceId = reader["DeviceId"]?.ToString();
                    }
                }

                // -------- If party exists but not registered, insert new user --------
                if (partyName != null && partyPhone != null && userId == 0)
                {
                    using var transaction = connection.BeginTransaction();

                    try
                    {
                        var getMaxIdCmd = new SqlCommand(
                            "SELECT ISNULL(MAX(UserId), 1000) + 1 FROM RegisterUsers WITH (TABLOCKX)",
                            connection, transaction);
                        userId = (int)await getMaxIdCmd.ExecuteScalarAsync();

                        var insertCmd = new SqlCommand(@"
                            INSERT INTO RegisterUsers (UserId, UserName, PhoneNumber, Email, PasswordHash, CreatedAt, FcmToken, DeviceType, LastLogin, IsMPINEnabled)
                            VALUES (@UserId, @UserName, @PhoneNumber, @Email, @PasswordHash, @CreatedAt, @FcmToken, @DeviceType, @LastLogin, 0)",
                            connection, transaction);

                        insertCmd.Parameters.AddWithValue("@UserId", userId);
                        insertCmd.Parameters.AddWithValue("@UserName", partyName);
                        insertCmd.Parameters.AddWithValue("@PhoneNumber", partyPhone);
                        insertCmd.Parameters.AddWithValue("@Email", "");
                        insertCmd.Parameters.AddWithValue("@PasswordHash", "");
                        insertCmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                        insertCmd.Parameters.AddWithValue("@FcmToken", (object)request.FcmToken ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@DeviceType", (object)request.DeviceType ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@LastLogin", DateTime.Now);
                        
                        await insertCmd.ExecuteNonQueryAsync();
                        await transaction.CommitAsync();

                        // ── Upsert UserActivity for new user ────────────────
                        var activityCmd = new SqlCommand(@"
                            INSERT INTO UserActivity (CustomerCode, LastLogin, LastSeen, LoginCount, LastNotificationSent)
                            VALUES (@CustomerCode, GETDATE(), GETDATE(), 1, NULL)",
                            connection);
                        activityCmd.Parameters.AddWithValue("@CustomerCode", partyPhone);
                        await activityCmd.ExecuteNonQueryAsync();

                        // New user - need to verify OTP and create MPIN
                        return Ok(new
                        {
                            success = true,
                            userId = userId,
                            username = partyName,
                            phone = partyPhone,
                            isMPINEnabled = false,
                            deviceMatched = false,
                            nextStep = "VERIFY_OTP"
                        });
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }

                // -------- If party exists and already registered --------
                if (partyName != null && partyPhone != null && userId > 0)
                {
                    bool deviceMatched = string.IsNullOrEmpty(deviceId) || deviceId == request.DeviceId;

                    // Update FCM token and device type
                    var updateCmd = new SqlCommand(@"
                        UPDATE RegisterUsers 
                        SET FcmToken = @FcmToken, DeviceType = @DeviceType
                        WHERE UserId = @UserId", connection);
                    updateCmd.Parameters.AddWithValue("@FcmToken", (object)request.FcmToken ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@DeviceType", (object)request.DeviceType ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@UserId", userId);
                    await updateCmd.ExecuteNonQueryAsync();

                    // ── Upsert UserActivity for existing user ────────────
                    var uaCmd = new SqlCommand(@"
                        IF EXISTS (SELECT 1 FROM UserActivity WHERE CustomerCode = @CustomerCode)
                            UPDATE UserActivity
                               SET LastLogin  = GETDATE(),
                                   LastSeen   = GETDATE(),
                                   LoginCount = LoginCount + 1
                             WHERE CustomerCode = @CustomerCode
                        ELSE
                            INSERT INTO UserActivity (CustomerCode, LastLogin, LastSeen, LoginCount, LastNotificationSent)
                            VALUES (@CustomerCode, GETDATE(), GETDATE(), 1, NULL)",
                        connection);
                    uaCmd.Parameters.AddWithValue("@CustomerCode", partyPhone);
                    await uaCmd.ExecuteNonQueryAsync();

                    // Determine next step based on MPIN and device status
                    string nextStep;
                    if (!isMPINEnabled)
                    {
                        nextStep = "VERIFY_OTP"; // Need to verify OTP before creating MPIN
                    }
                    else if (!deviceMatched)
                    {
                        nextStep = "VERIFY_OTP"; // New device - need OTP verification
                    }
                    else
                    {
                        nextStep = "ENTER_MPIN"; // All good - just enter MPIN
                    }

                    return Ok(new
                    {
                        success = true,
                        userId = userId,
                        username = username,
                        phone = partyPhone,
                        isMPINEnabled = isMPINEnabled,
                        deviceMatched = deviceMatched,
                        nextStep = nextStep
                    });
                }

                // -------- If party does not exist but user is registered --------
                if (partyName == null && userId > 0)
                {
                    bool deviceMatched = string.IsNullOrEmpty(deviceId) || deviceId == request.DeviceId;

                    var updateCmd = new SqlCommand(@"
                        UPDATE RegisterUsers 
                        SET FcmToken = @FcmToken, DeviceType = @DeviceType
                        WHERE UserId = @UserId", connection);
                    updateCmd.Parameters.AddWithValue("@FcmToken", (object)request.FcmToken ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@DeviceType", (object)request.DeviceType ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@UserId", userId);
                    await updateCmd.ExecuteNonQueryAsync();

                    // ── Upsert UserActivity — user registered but no scheme yet ──
                    // partyPhone may be null here (no Party entry), use request.Phone
                    var uaCmd2 = new SqlCommand(@"
                        IF EXISTS (SELECT 1 FROM UserActivity WHERE CustomerCode = @CustomerCode)
                            UPDATE UserActivity
                               SET LastLogin  = GETDATE(),
                                   LastSeen   = GETDATE(),
                                   LoginCount = LoginCount + 1
                             WHERE CustomerCode = @CustomerCode
                        ELSE
                            INSERT INTO UserActivity (CustomerCode, LastLogin, LastSeen, LoginCount, LastNotificationSent)
                            VALUES (@CustomerCode, GETDATE(), GETDATE(), 1, NULL)",
                        connection);
                    uaCmd2.Parameters.AddWithValue("@CustomerCode", request.Phone.Trim());
                    await uaCmd2.ExecuteNonQueryAsync();

                    string nextStep;
                    if (!isMPINEnabled)
                    {
                        nextStep = "VERIFY_OTP";
                    }
                    else if (!deviceMatched)
                    {
                        nextStep = "VERIFY_OTP";
                    }
                    else
                    {
                        nextStep = "ENTER_MPIN";
                    }

                    return Ok(new
                    {
                        success = true,
                        userId = userId,
                        username = username,
                        phone = partyPhone,
                        isMPINEnabled = isMPINEnabled,
                        deviceMatched = deviceMatched,
                        nextStep = nextStep
                    });
                }

                // -------- Otherwise --------
                return Unauthorized(new { success = false, message = "Please check the phone number." });
            }
            catch (SqlException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Database error. Please try again later." });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "An unexpected error occurred. Please try again later." });
            }
        }



        private bool IsPhoneNumberValid(string phone)
        {

            var regex = new Regex(@"^\d{10}$");
            return regex.IsMatch(phone);
        }



        [HttpGet("AdminValidate/{username}/{password}")]
        public async Task<IActionResult> AdminValidate([FromRoute] string username, [FromRoute] string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return BadRequest(new { message = "Username and password are required." });

            try
            {

                var responseDivisions = new
                {
                    divisions = new
                    {
                        gold = new List<object>(),
                        silver = new List<object>()
                    }
                };





                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();


                // Fetch Division Data
                var divisionCmd = new SqlCommand(
                    @"SELECT fCode, fName, fRate 
            FROM Division 
           WHERE fCode IN ('0003','0002','0014','0004','0005')",
                    connection);

                using (var reader1 = await divisionCmd.ExecuteReaderAsync())
                {
                    while (await reader1.ReadAsync())
                    {
                        string code = reader1["fCode"].ToString();
                        string name = reader1["fName"].ToString();
                        decimal rate = Convert.ToDecimal(reader1["fRate"]);

                        // GOLD: 14K, 18K, 22K, 24K
                        if (code == "0003" || code == "0002" || code == "0014" || code == "0004")
                        {
                            responseDivisions.divisions.gold.Add(new
                            {
                                name,
                                rate
                            });
                        }

                        // SILVER
                        if (code == "0005")
                        {
                            responseDivisions.divisions.silver.Add(new
                            {
                                name,
                                rate
                            });
                        }
                    }
                }
                // ✅ Updated: Select more columns
                var cmd = new SqlCommand(@"
            SELECT TOP 1 FCOMPCODE, FADMIN, PHONE1
            FROM COMPANY 
            WHERE  FSUP = @username AND FADMIN = @password", connection);

                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@password", password);

                using var reader = await cmd.ExecuteReaderAsync();


                if (await reader.ReadAsync())
                {
                    string fcompcode = reader["FCOMPCODE"].ToString();
                    string adminName = reader["FADMIN"].ToString();
                    string phone = reader["PHONE1"].ToString();

                    // ✅ Optionally use phone in token
                    var token = JwtHelper.GenerateJwtToken(phone, "Admin", _config);

                    return Ok(new
                    {
                        role = "Admin",
                        token,
                        UserPermission = "A",
                        UserId = fcompcode,
                        AdminName = adminName,
                        Phone = phone,
                        responseDivisions
                    });
                }
                else
                {
                    return Unauthorized(new { message = "Invalid admin credentials." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error validating admin.", error = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpPost("guest-login")]
        public async Task<IActionResult> GuestLogin()
        {
            try
            {


                var responseDivisions = new
                {
                    divisions = new
                    {
                        gold = new List<object>(),
                        silver = new List<object>()
                    }
                };

                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Fetch Division Data
                var divisionCmd = new SqlCommand(
                    @"SELECT fCode, fName, fRate 
            FROM Division 
           WHERE fCode IN ('0003','0002','0014','0004','0005')",
                    connection);

                using (var reader = await divisionCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string code = reader["fCode"].ToString();
                        string name = reader["fName"].ToString();
                        decimal rate = Convert.ToDecimal(reader["fRate"]);

                        // GOLD: 14K, 18K, 22K, 24K
                        if (code == "0003" || code == "0002" || code == "0014" || code == "0004")
                        {
                            responseDivisions.divisions.gold.Add(new
                            {
                                name,
                                rate
                            });
                        }

                        // SILVER
                        if (code == "0005")
                        {
                            responseDivisions.divisions.silver.Add(new
                            {
                                name,
                                rate
                            });
                        }
                    }
                }


                // Generate a random GuestId
                string guestId = Guid.NewGuid().ToString("N").Substring(0, 10);

                // Generate JWT token with Guest role
                var token = JwtHelper.GenerateJwtToken(guestId, "Guest", _config);

                return Ok(new
                {
                    role = "Guest",
                    token,
                    UserPermission = "G",
                    GuestId = guestId,
                    username = "Guest User",
                    email = "",
                    responseDivisions
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error creating guest login.", error = ex.Message });
            }
        }







    }
}

public class AuthLoginRequest
{
    public string Phone { get; set; }
}
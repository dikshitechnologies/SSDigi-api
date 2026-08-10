
using CHITSCHEME.Helpers;
using CHITSCHEME.Models;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class RegisterPage2Controller : ControllerBase
    {
        // ── Firebase singleton (thread-safe) ─────────────────────────────────
        private static bool _firebaseInitialized = false;
        private static readonly object _fbLock   = new();

        private static void EnsureFirebaseInitialized()
        {
            if (_firebaseInitialized) return;
            lock (_fbLock)
            {
                if (_firebaseInitialized) return;
                var jsonPath = Path.Combine(AppContext.BaseDirectory,
                    "pukhraj-chit-firebase-adminsdk-fbsvc-b739f8988d.json");
                if (FirebaseApp.DefaultInstance == null)
                {
                    FirebaseApp.Create(new AppOptions
                    {
                        Credential = GoogleCredential.FromFile(jsonPath)
                    });
                }
                _firebaseInitialized = true;
            }
        }

        /// <summary>
        /// Sends a welcome push notification to the newly registered user's device.
        /// Fires and forgets — registration is not blocked if this fails.
        /// </summary>
        private static async Task SendWelcomeNotificationAsync(string? fcmToken, string userName)
        {
            if (string.IsNullOrWhiteSpace(fcmToken)) return;

            try
            {
                EnsureFirebaseInitialized();

                string title = "Welcome to Pukhraj Elite Jewellers! 🎉";
                string body  = $"Hi {userName}! Explore our latest collections, daily gold rates, and exclusive savings schemes.";

                var message = new Message
                {
                    Token        = fcmToken,
                    Notification = new Notification { Title = title, Body = body },
                    Android      = new AndroidConfig
                    {
                        Priority     = Priority.High,
                        Notification = new AndroidNotification
                        {
                            Title     = title,
                            Body      = body,
                            Sound     = "default",
                            ChannelId = "general"
                        }
                    },
                    Apns = new ApnsConfig
                    {
                        Aps = new Aps { Sound = "default", Badge = 1 }
                    },
                    Data = new Dictionary<string, string>
                    {
                        { "click_action", "FLUTTER_NOTIFICATION_CLICK" },
                        { "type",         "welcome" }
                    }
                };

                await FirebaseMessaging.DefaultInstance.SendAsync(message);
            }
            catch
            {
                // Welcome notification failure must never break registration
            }
        }

        //---------------------------------------------Duplicate Name Checking ---------------------------------
        private bool RegistruserExists(SqlConnection con, string sectionName)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT 1 FROM RegisterUsers  where PhoneNumber=@PhoneNumber", con))
            {
                cmd.Parameters.AddWithValue("@PhoneNumber", sectionName);
                return cmd.ExecuteScalar() != null;
            }
        }

        // ── Generate a unique 10-character alphanumeric Refer ID ────────────
        private static string GenerateReferId()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Range(0, 10)
                .Select(_ => chars[random.Next(chars.Length)])
                .ToArray());
        }

        private static async Task<string> GenerateUniqueReferIdAsync(SqlConnection conn)
        {
            string referId;
            do
            {
                referId = GenerateReferId();
                using var checkCmd = new SqlCommand(
                    "SELECT 1 FROM RegisterUsers WHERE ReferId = @ReferId", conn);
                checkCmd.Parameters.AddWithValue("@ReferId", referId);
                var exists = await checkCmd.ExecuteScalarAsync();
                if (exists == null) break;
            } while (true);
            return referId;
        }



        [HttpPost("RegisterUser")]
        public async Task<IActionResult> RegisterUser([FromBody] RegisterUser model)
        {

            if (model == null  )
            {
                return BadRequest("Model cannot be null.");
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(model.Firstname) || model.Firstname.ToLower() == "string")
            {
                return BadRequest("First name is empty.");
            }
            if (model.Firstname.Length > 100)
            {
                return BadRequest("First name cannot exceed 100 characters.");
            }


            if (string.IsNullOrWhiteSpace(model.Phonenumber) || model.Phonenumber.ToLower() == "string")
            {
                return BadRequest("Phone number is empty.");
            }
            if (model.Phonenumber.Length > 20)
            {
                return BadRequest("Phone number cannot exceed 20 characters.");
            }

            using(SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
            {
                await connection.OpenAsync();

                using (SqlCommand cmd = new SqlCommand("SELECT COUNT(*) FROM REGISTERUSERS WHERE PHONENUMBER=@PHONENUMBER", connection))
                {
                    cmd.Parameters.AddWithValue("PHONENUMBER", model.Phonenumber);
                    int count = (int)await cmd.ExecuteScalarAsync();
                    if (count > 0)
                    {
                        return BadRequest(new { message = "Phone number already exists." });
                    }
                }
            }
           

                string maxRegisterIdQuery = "SELECT MAX(UserID) FROM RegisterUsers";
            string insertQuery = @"
        INSERT INTO RegisterUsers (UserID,UserName, Email, PhoneNumber, PasswordHash,CreatedAt, FcmToken, DeviceType, LastLogin, ReferId)
        VALUES (@UserID,@UserName, @Email, @PhoneNumber, @PasswordHash,@CreatedAt, @FcmToken, @DeviceType, @LastLogin, @ReferId);
        SELECT SCOPE_IDENTITY();";

            using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
            {
                try
                {
                    // Open connection
                    await conn.OpenAsync();

                    // Get the last user id from the database
                    using (SqlCommand maxIdCommand = new SqlCommand(maxRegisterIdQuery, conn))
                    {
                        object result = await maxIdCommand.ExecuteScalarAsync();

                        string newUserCode;
                        if (result == DBNull.Value || result == null)
                        {
                            newUserCode = "1000";  // First user
                        }
                        else
                        {
                            string lastUserId = result.ToString();
                            if (int.TryParse(lastUserId, out int lastId))
                            {
                                int nextId = lastId + 1;
                                newUserCode = nextId.ToString("D4");  
                            }
                            else
                            {
                                return StatusCode(500, new { message = "Invalid user ID format in database." });
                            }
                        }

                        if (RegistruserExists(conn, model.Phonenumber))
                        {
                            return Conflict(new { message = "Phonenumber  already exists" });
                        }

                        // Generate unique Refer ID for new user
                        string newReferId = await GenerateUniqueReferIdAsync(conn);

                        // Insert new user with the generated user code
                        using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@UserID", newUserCode);
                            cmd.Parameters.AddWithValue("@UserName", model.Firstname);
                            cmd.Parameters.AddWithValue("@Email", model.Email);
                            cmd.Parameters.AddWithValue("@PhoneNumber", model.Phonenumber);
                            cmd.Parameters.AddWithValue("@PasswordHash", "");
                            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
                            cmd.Parameters.AddWithValue("@FcmToken", (object)model.FcmToken ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@DeviceType", (object)model.DeviceType ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@LastLogin", DateTime.Now);
                            cmd.Parameters.AddWithValue("@ReferId", newReferId);
                            int rowsAffected = await cmd.ExecuteNonQueryAsync();

                            if (rowsAffected > 0)
                            {
                                // ── Apply referral if a ReferCode was provided ──────────────────
                                bool referralApplied = false;
                                if (!string.IsNullOrWhiteSpace(model.ReferCode))
                                {
                                    // Look up the referrer by their ReferId
                                    using var referrerCmd = new SqlCommand(
                                        "SELECT UserID FROM RegisterUsers WHERE ReferId = @ReferId", conn);
                                    referrerCmd.Parameters.AddWithValue("@ReferId", model.ReferCode.Trim().ToUpper());
                                    var referrerIdObj = await referrerCmd.ExecuteScalarAsync();

                                    if (referrerIdObj != null)
                                    {
                                        string referrerId = referrerIdObj.ToString();
                                        // Must not refer themselves
                                        if (referrerId != newUserCode)
                                        {
                                            using var applyCmd = new SqlCommand(@"
                                                UPDATE RegisterUsers
                                                SET ReferredByUserId = @ReferrerId,
                                                    ReferralDate     = GETDATE()
                                                WHERE UserID = @NewUserId
                                                  AND ReferredByUserId IS NULL", conn);
                                            applyCmd.Parameters.AddWithValue("@ReferrerId", referrerId);
                                            applyCmd.Parameters.AddWithValue("@NewUserId", newUserCode);
                                            await applyCmd.ExecuteNonQueryAsync();
                                            referralApplied = true;
                                        }
                                    }
                                }

                                // ── Fire welcome notification (non-blocking) ──
                                _ = SendWelcomeNotificationAsync(model.FcmToken, model.Firstname);

                                return Ok(new
                                {
                                    message = "User registered successfully",
                                    referId = newReferId,
                                    referralApplied = referralApplied
                                });
                            }
                            else
                            {
                                return StatusCode(500, "Failed to register user.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception
                    return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
                }
                finally
                {
                    // Ensure that the connection is closed
                    conn.Close();
                }
            }
        }




        [HttpGet("profilePage/{UserID}")]
        public IActionResult ProfilePage(string UserID)
        {
            try
            {
                var token = Request.Headers["Authorization"].ToString()
                   .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                   .Trim();

                if (string.IsNullOrWhiteSpace(token))
                    return Unauthorized("Token is missing or invalid.");


                if (!new JwtSecurityTokenHandler().CanReadToken(token))
                    return Unauthorized("Malformed JWT.");

                string role = JwtHelper.GetRoleFromJwtToken(token);

                if (string.IsNullOrEmpty(role))
                    return Unauthorized(new { message = "Invalid or expired token" });

                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    connection.Open();
                    string query;

                    if (role == "Admin")
                    {
                        // Admin → COMPANY table (no AddressLine, City, etc.)
                        query = @"
                    SELECT 
                        fcompname AS UserName, 
                        PHONE1 AS Email,
                        '' AS AddressLine,
                        '' AS City,
                        '' AS State,
                        '' AS Pincode,
                        '' AS fprofileImg,
                        '' AS ReferredBy,
                        0 AS HasReferral,
                        '' AS ReferId
                    FROM COMPANY
                    WHERE fcompcode = @UserID";
                    }
                    else
                    {
                        // Normal user → RegisterUsers table
                        query = @"
                    SELECT 
                        UserName, 
                        Email, 
                        ISNULL(AddressLine, '') AS AddressLine,
                        ISNULL(City, '') AS City,
                        ISNULL(State, '') AS State,
                        ISNULL(Pincode, '') AS Pincode,
                        ISNULL(fprofileImg, '') AS fprofileImg,
                        ISNULL(CAST(ReferredByUserId AS NVARCHAR(50)), '') AS ReferredBy,
                        CASE WHEN ReferredByUserId IS NOT NULL THEN 1 ELSE 0 END AS HasReferral,
                        ISNULL(CAST(ReferId AS NVARCHAR(50)), '') AS ReferId,
                        ISNULL(CONVERT(NVARCHAR, ReferralDate, 120), '') AS ReferralDate,
                        ISNULL(ReferralVoucherNo, '') AS ReferralVoucherNo,
                        CASE WHEN ReferralEarnedVoucherNo IS NOT NULL THEN 1 ELSE 0 END AS HasReferralEarned,
                        ISNULL(ReferralEarnedVoucherNo, '') AS ReferralEarnedVoucherNo,
                        ISNULL(CONVERT(NVARCHAR, ReferralEarnedDate, 120), '') AS ReferralEarnedDate
                    FROM RegisterUsers 
                    WHERE UserID = @UserID";
                    }

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserID", UserID);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var result = new
                                {
                                    UserName    = reader["UserName"].ToString(),
                                    Email       = reader["Email"].ToString(),
                                    AddressLine = reader["AddressLine"].ToString(),
                                    City        = reader["City"].ToString(),
                                    State       = reader["State"].ToString(),
                                    Pincode     = reader["Pincode"].ToString(),
                                    fprofileImg = reader["fprofileImg"].ToString(),

                                    // ── Referral status ─────────────────────
                                    ReferId          = reader["ReferId"].ToString(),

                                    // As referee (used someone's code)
                                    HasReferral      = Convert.ToBoolean(reader["HasReferral"]),
                                    ReferredBy       = reader["ReferredBy"].ToString(),
                                    ReferralDate     = reader["ReferralDate"].ToString(),
                                    ReferralVoucherNo = reader["ReferralVoucherNo"].ToString(),

                                    // As referrer (someone used their code)
                                    HasReferralEarned       = Convert.ToBoolean(reader["HasReferralEarned"]),
                                    ReferralEarnedVoucherNo = reader["ReferralEarnedVoucherNo"].ToString(),
                                    ReferralEarnedDate      = reader["ReferralEarnedDate"].ToString()
                                };

                                return Ok(result);
                            }
                            else
                            {
                                return NotFound(new { message = "User not found" });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }


    }
}

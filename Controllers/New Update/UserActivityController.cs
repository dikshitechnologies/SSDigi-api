using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.New_Update
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserActivityController : ControllerBase
    {
        // Key design:
        //   UserActivity.CustomerCode = PhoneNumber (common key across all tables)
        //   FCMToken lives in RegisterUsers — scheduler reads it from there via JOIN
        //   UserActivity only tracks: LastLogin, LastSeen, LoginCount, LastNotificationSent

        // -------------------------------------------------------
        // POST api/UserActivity/login
        // Called automatically from AuthRegController on every login.
        // Tracks LastLogin, LastSeen, LoginCount.
        // No FCMToken needed here — RegisterUsers already has it.
        // -------------------------------------------------------
        [HttpPost("login")]
        public async Task<IActionResult> UpdateLogin([FromBody] UserLoginActivityRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerCode))
                return BadRequest(new { success = false, message = "CustomerCode is required." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
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

                cmd.Parameters.AddWithValue("@CustomerCode", request.CustomerCode.Trim());
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { success = true, message = "Login activity recorded." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // POST api/UserActivity/last-seen
        // Optional: call when app goes to background via AppState.
        // Middleware handles this automatically for all API calls.
        // -------------------------------------------------------
        [HttpPost("last-seen")]
        public async Task<IActionResult> UpdateLastSeen([FromBody] LastSeenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerCode))
                return BadRequest(new { success = false, message = "CustomerCode is required." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    IF EXISTS (SELECT 1 FROM UserActivity WHERE CustomerCode = @CustomerCode)
                        UPDATE UserActivity
                           SET LastSeen = GETDATE()
                         WHERE CustomerCode = @CustomerCode",
                    connection);

                cmd.Parameters.AddWithValue("@CustomerCode", request.CustomerCode.Trim());
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { success = true });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET api/UserActivity/stats/{customerCode}   (admin)
        // Returns activity + scheme count for a customer.
        // customerCode = phone number
        // -------------------------------------------------------
        [HttpGet("stats/{customerCode}")]
        public async Task<IActionResult> GetStats(string customerCode)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    SELECT
                        ua.CustomerCode,
                        ua.LastLogin,
                        ua.LastSeen,
                        ua.LoginCount,
                        ua.LastNotificationSent,
                        ru.UserName,
                        ru.FcmToken,
                        ru.DeviceType,
                        (SELECT COUNT(*)
                         FROM   Party p
                         WHERE  p.FPHONE   = ua.CustomerCode
                           AND  p.fparent  LIKE '0000100044%'
                           AND  p.faclevel < 0) AS SchemeCount
                    FROM   UserActivity ua
                    LEFT JOIN RegisterUsers ru ON ru.PhoneNumber = ua.CustomerCode
                    WHERE  ua.CustomerCode = @CustomerCode",
                    connection);

                cmd.Parameters.AddWithValue("@CustomerCode", customerCode);

                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return NotFound(new { success = false, message = "No activity found for this customer." });

                int schemeCount = Convert.ToInt32(reader["SchemeCount"]);

                return Ok(new
                {
                    success              = true,
                    customerCode         = reader["CustomerCode"].ToString(),
                    userName             = reader["UserName"]             == DBNull.Value ? null : reader["UserName"].ToString(),
                    deviceType           = reader["DeviceType"]           == DBNull.Value ? null : reader["DeviceType"].ToString(),
                    hasFcmToken          = reader["FcmToken"]             != DBNull.Value && !string.IsNullOrWhiteSpace(reader["FcmToken"].ToString()),
                    schemeCount,
                    hasScheme            = schemeCount > 0,
                    lastLogin            = reader["LastLogin"]            == DBNull.Value ? null : Convert.ToDateTime(reader["LastLogin"]).ToString("yyyy-MM-dd HH:mm:ss"),
                    lastSeen             = reader["LastSeen"]             == DBNull.Value ? null : Convert.ToDateTime(reader["LastSeen"]).ToString("yyyy-MM-dd HH:mm:ss"),
                    loginCount           = Convert.ToInt32(reader["LoginCount"]),
                    lastNotificationSent = reader["LastNotificationSent"] == DBNull.Value ? null : Convert.ToDateTime(reader["LastNotificationSent"]).ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }
    }

    // ─── Request models ──────────────────────────────────────────
    public class UserLoginActivityRequest
    {
        public string CustomerCode { get; set; } = string.Empty;
    }

    public class LastSeenRequest
    {
        public string CustomerCode { get; set; } = string.Empty;
    }
}

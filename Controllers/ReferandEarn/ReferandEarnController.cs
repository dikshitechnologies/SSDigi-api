using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME_SSDigi.Controllers.ReferandEarn
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReferandEarnController : ControllerBase
    {
        // ── GET /api/ReferandEarn/MyReferId/{userId} ──────────────────────────
        /// <summary>
        /// Returns the logged-in user's own Refer ID so they can share it.
        /// </summary>
        [HttpGet("MyReferId/{userId}")]
        public async Task<IActionResult> GetMyReferId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest(new { success = false, message = "UserId is required." });

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                using var cmd = new SqlCommand(
                    "SELECT ReferId, UserName FROM RegisterUsers WHERE UserID = @UserId", conn);
                cmd.Parameters.AddWithValue("@UserId", userId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return NotFound(new { success = false, message = "User not found." });

                return Ok(new
                {
                    success  = true,
                    referId  = reader["ReferId"]?.ToString(),
                    userName = reader["UserName"]?.ToString()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ── POST /api/ReferandEarn/ApplyReferral ──────────────────────────────
        /// <summary>
        /// Validates a Refer Code only — NO DB write happens here.
        /// Returns isValid + referrerId so the frontend can pass hasReferral:1
        /// and referrerId into InsertChitScheme, which does the actual save.
        /// </summary>
        [HttpPost("ApplyReferral")]
        public async Task<IActionResult> ApplyReferral([FromBody] ApplyReferralRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
                return BadRequest(new { success = false, message = "UserId is required." });

            if (string.IsNullOrWhiteSpace(request.ReferCode))
                return BadRequest(new { success = false, message = "ReferCode is required." });

            string referCode = request.ReferCode.Trim().ToUpper();

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                // 1. Refer ID exists?
                using var referrerCmd = new SqlCommand(
                    "SELECT UserID, UserName FROM RegisterUsers WHERE ReferId = @ReferId", conn);
                referrerCmd.Parameters.AddWithValue("@ReferId", referCode);

                string referrerId = null;
                string referrerName = null;
                using (var reader = await referrerCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        referrerId   = reader["UserID"].ToString();
                        referrerName = reader["UserName"].ToString();
                    }
                }

                if (referrerId == null)
                    return BadRequest(new { success = false, message = "Refer ID does not exist." });

                // 2. Not referring themselves?
                if (referrerId == request.UserId)
                    return BadRequest(new { success = false, message = "You cannot use your own Refer ID." });

                // 3. User hasn't already been referred?
                using var checkCmd = new SqlCommand(
                    "SELECT ReferredByUserId FROM RegisterUsers WHERE UserID = @UserId", conn);
                checkCmd.Parameters.AddWithValue("@UserId", request.UserId);
                var existing = await checkCmd.ExecuteScalarAsync();

                if (existing != null && existing != DBNull.Value
                    && !string.IsNullOrWhiteSpace(existing.ToString()))
                    return BadRequest(new { success = false, message = "You have already used a referral." });

                // ── Validation passed. No DB write here. ──────────────────────
                // Frontend takes referrerId + passes hasReferral:1 to InsertChitScheme.
                return Ok(new
                {
                    success      = true,
                    isValid      = true,
                    message      = "Refer code is valid.",
                    referrerId   = referrerId,
                    referrerName = referrerName
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ── GET /api/ReferandEarn/MyReferrals/{userId} ────────────────────────
        /// <summary>
        /// Returns a list of users who were referred by this user.
        /// </summary>
        [HttpGet("MyReferrals/{userId}")]
        public async Task<IActionResult> GetMyReferrals(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest(new { success = false, message = "UserId is required." });

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
                    SELECT UserID, UserName, PhoneNumber, ReferralDate
                    FROM RegisterUsers
                    WHERE ReferredByUserId = @UserId
                    ORDER BY ReferralDate DESC", conn);
                cmd.Parameters.AddWithValue("@UserId", userId);

                var referrals = new List<object>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    referrals.Add(new
                    {
                        userId       = reader["UserID"].ToString(),
                        userName     = reader["UserName"].ToString(),
                        phoneNumber  = reader["PhoneNumber"].ToString(),
                        referralDate = reader["ReferralDate"] != DBNull.Value
                            ? Convert.ToDateTime(reader["ReferralDate"]).ToString("yyyy-MM-dd HH:mm:ss")
                            : null
                    });
                }

                return Ok(new
                {
                    success       = true,
                    totalReferrals = referrals.Count,
                    referrals
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    // ── Request Models ────────────────────────────────────────────────────────
    public class ApplyReferralRequest
    {
        public string UserId    { get; set; }
        public string ReferCode { get; set; }
    }
}

using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace CHITSCHEME_PukhRaj.Controllers.Reports
{
    [Route("api/[controller]")]
    [ApiController]
    public class InactiveUsersController : ControllerBase
    {
        [HttpGet("GetInactiveUsers")]
        public async Task<IActionResult> GetInactiveUsers(
            DateTime? fromDate = null,
            DateTime? toDate = null)
        {
            try
            {
                var inactiveUsers = new List<InactiveUserDto>();

                // Default dates if not supplied
                fromDate ??= new DateTime(2026, 1, 1);
                toDate ??= new DateTime(2028, 7, 28);

                var connectionString = DBHelper.GetConnection();

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                string query = @"
SELECT 
    RU.UserID,
    RU.UserName,
    RU.PhoneNumber,
    RU.Email,
    RU.CreatedAt
FROM RegisterUsers RU
LEFT JOIN Party P
    ON LTRIM(RTRIM(RU.PhoneNumber)) = LTRIM(RTRIM(P.fPhone))
WHERE P.fPhone IS NULL
    AND (@FromDate IS NULL OR CAST(RU.CreatedAt AS DATE) >= @FromDate)
    AND (@ToDate IS NULL OR CAST(RU.CreatedAt AS DATE) <= @ToDate)
ORDER BY RU.CreatedAt DESC;";

                using var cmd = new SqlCommand(query, connection);

                cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value =
                    fromDate.HasValue ? fromDate.Value.Date : DBNull.Value;

                cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value =
                    toDate.HasValue ? toDate.Value.Date : DBNull.Value;

                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    inactiveUsers.Add(new InactiveUserDto
                    {
                        UserID = reader["UserID"] == DBNull.Value ? 0 : Convert.ToInt32(reader["UserID"]),
                        UserName = reader["UserName"]?.ToString(),
                        PhoneNumber = reader["PhoneNumber"]?.ToString(),
                        Email = reader["Email"] == DBNull.Value ? "" : reader["Email"].ToString(),
                        CreatedAt = reader["CreatedAt"] == DBNull.Value
                            ? DateTime.MinValue
                            : Convert.ToDateTime(reader["CreatedAt"])
                    });
                }

                return Ok(new
                {
                    Status = true,
                    Message = "Inactive users fetched successfully.",
                    Count = inactiveUsers.Count,
                    Data = inactiveUsers
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    Status = false,
                    Message = ex.Message
                });
            }
        }
    }

    public class InactiveUserDto
    {
        public int UserID { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
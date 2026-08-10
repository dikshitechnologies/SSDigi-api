using CHITSCHEME.Helpers;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserAdminController : ControllerBase
    {

        [HttpGet("GetUserList")]
        public IActionResult GetUserList(
        string? search = "",
        int page = 1,
        int pageSize = 10)
        {
            // 🔐 JWT Validation
            var token = Request.Headers["Authorization"]
                .ToString()
                .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (string.IsNullOrWhiteSpace(token))
                return Unauthorized("Token is missing or invalid.");

            if (!new JwtSecurityTokenHandler().CanReadToken(token))
                return Unauthorized("Malformed JWT.");

            string role = JwtHelper.GetRoleFromJwtToken(token);

            if (string.IsNullOrEmpty(role))
                return Unauthorized(new { message = "Invalid or expired token" });

            // Pagination safety
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            int offset = (page - 1) * pageSize;

            var data = new List<object>();
            int totalRecords = 0;

            using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
            {
                connection.Open();

                string countQuery = @"
            SELECT COUNT(*)
            FROM RegisterUsers where 
               (@search IS NULL OR @search = '' 
                   OR userId LIKE '%' + @search + '%' 
                   OR Username LIKE '%' + @search + '%')";

                using (SqlCommand countCmd = new SqlCommand(countQuery, connection))
                {
                    countCmd.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);

                    totalRecords = (int)countCmd.ExecuteScalar();
                }

              
                string dataQuery = @"
            	   SELECT UserID, UserName, PhoneNumber,isActive
                    FROM RegisterUsers
                    WHERE 
                       (@search IS NULL OR @search = '' 
                           OR UserID LIKE '%' + @search + '%' 
                           OR UserName LIKE '%' + @search + '%')
                    ORDER BY UserName
                    OFFSET @offset ROWS
                    FETCH NEXT @pageSize ROWS ONLY";

                using (SqlCommand cmd = new SqlCommand(dataQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    cmd.Parameters.AddWithValue("@pageSize", pageSize);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            data.Add(new
                            {
                                fcode = reader["UserID"].ToString(),
                                facname = reader["UserName"].ToString(),
                                isActive = reader["isActive"].ToString(),
                                FPHONE = reader["PhoneNumber"].ToString()
                            });
                        }
                    }
                }
            }

            return Ok(new
            {
                page,
                pageSize,
                totalRecords,
                totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),
                data
            });
        }


        [HttpDelete("DeleteUser/{userId}")]
        public IActionResult DeleteUser(string userId)
        {
            // 🔐 JWT Validation
            var token = Request.Headers["Authorization"]
                .ToString()
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

                string query = @"
            UPDATE RegisterUsers
            SET isActive = 0
            WHERE UserID = @userId";

                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);

                    int rowsAffected = cmd.ExecuteNonQuery();

                    if (rowsAffected == 0)
                    {
                        return NotFound(new
                        {
                            success = false,
                            message = "User not found"
                        });
                    }
                }
            }

            return Ok(new
            {
                success = true,
                message = "User deactivated successfully"
            });
        }
        [HttpPut("ActivateUser/{userId}")]
        public IActionResult ActivateUser(string userId)
        {
            // 🔐 JWT Validation
            var token = Request.Headers["Authorization"]
                .ToString()
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

                string query = @"
        UPDATE RegisterUsers
        SET isActive = 1
        WHERE UserID = @userId";

                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);

                    int rowsAffected = cmd.ExecuteNonQuery();

                    if (rowsAffected == 0)
                    {
                        return NotFound(new
                        {
                            success = false,
                            message = "User not found"
                        });
                    }
                }
            }

            return Ok(new
            {
                success = true,
                message = "User activated successfully"
            });
        }
    }
}

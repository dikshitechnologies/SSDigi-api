using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.New_Update
{
    [Route("api/[controller]")]
    [ApiController]
    public class CustomerReviewsController : ControllerBase
    {
        // -------------------------------------------------------
        // POST api/CustomerReviews/add
        // Stores a new customer review.
        // -------------------------------------------------------
        [HttpPost("add")]
        public async Task<IActionResult> AddReview([FromBody] AddReviewRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CusCode))
                return BadRequest(new { success = false, message = "cusCode is required." });

            if (string.IsNullOrWhiteSpace(request.Rating))
                return BadRequest(new { success = false, message = "rating is required." });

            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    INSERT INTO CustomerReviews (CustomerCode, Rating, ReviewText, ItemCode, Fid, IsActive, CreatedDate)
                    VALUES (@CustomerCode, @Rating, @ReviewText, @ItemCode, @Fid, 1, @CreatedDate);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    connection);

                cmd.Parameters.AddWithValue("@CustomerCode", request.CusCode.Trim());
                cmd.Parameters.AddWithValue("@Rating",       request.Rating.Trim());
                cmd.Parameters.AddWithValue("@ReviewText",   string.IsNullOrWhiteSpace(request.Comment)
                                                                 ? (object)DBNull.Value
                                                                 : request.Comment.Trim());
                cmd.Parameters.AddWithValue("@ItemCode",     request.ItemCode.Trim());
                cmd.Parameters.AddWithValue("@Fid",          request.Fid.Trim());
                cmd.Parameters.AddWithValue("@CreatedDate",  DateTime.Now);

                int newId = (int)await cmd.ExecuteScalarAsync();

                return Ok(new
                {
                    success  = true,
                    message  = "Review submitted successfully.",
                    id       = newId
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

        // -------------------------------------------------------
        // GET api/CustomerReviews/get-all
        // Returns all reviews (admin use).
        // -------------------------------------------------------
        [HttpGet("get-all")]
        public async Task<IActionResult> GetAllReviews()
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    SELECT Id, CustomerCode, CustomerName, Rating, ReviewText, IsActive, CreatedDate
                    FROM CustomerReviews
                    ORDER BY Id DESC",
                    connection);

                using var reader = await cmd.ExecuteReaderAsync();

                var list = new List<object>();
                while (await reader.ReadAsync())
                {
                    list.Add(new
                    {
                        id           = Convert.ToInt32(reader["Id"]),
                        customerCode = reader["CustomerCode"].ToString(),
                        customerName = reader["CustomerName"] == DBNull.Value ? null : reader["CustomerName"].ToString(),
                        rating       = reader["Rating"].ToString(),
                        reviewText   = reader["ReviewText"] == DBNull.Value ? null : reader["ReviewText"].ToString(),
                        isActive     = Convert.ToBoolean(reader["IsActive"]),
                        createdDate  = Convert.ToDateTime(reader["CreatedDate"]).ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }

                return Ok(new { success = true, data = list });
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
        // GET api/CustomerReviews/get-active
        // Returns only active reviews (app/website display).
        // -------------------------------------------------------
        [HttpGet("get-active")]
        public async Task<IActionResult> GetActiveReviews()
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    SELECT Id, CustomerCode, CustomerName, Rating, ReviewText, IsActive, CreatedDate
                    FROM CustomerReviews
                    WHERE IsActive = 1
                    ORDER BY Id DESC",
                    connection);

                using var reader = await cmd.ExecuteReaderAsync();

                var list = new List<object>();
                while (await reader.ReadAsync())
                {
                    list.Add(new
                    {
                        id           = Convert.ToInt32(reader["Id"]),
                        customerCode = reader["CustomerCode"].ToString(),
                        customerName = reader["CustomerName"] == DBNull.Value ? null : reader["CustomerName"].ToString(),
                        rating       = reader["Rating"].ToString(),
                        reviewText   = reader["ReviewText"] == DBNull.Value ? null : reader["ReviewText"].ToString(),
                        createdDate  = Convert.ToDateTime(reader["CreatedDate"]).ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }

                return Ok(new { success = true, data = list });
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
        // GET api/CustomerReviews/get-by-customer/{customerCode}
        // Returns all reviews for a specific customer.
        // -------------------------------------------------------
        [HttpGet("get-by-customer/{customerCode}")]
        public async Task<IActionResult> GetByCustomer(string customerCode)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    SELECT Id, CustomerCode, CustomerName, Rating, ReviewText, IsActive, CreatedDate
                    FROM CustomerReviews
                    WHERE CustomerCode = @CustomerCode
                    ORDER BY Id DESC",
                    connection);

                cmd.Parameters.AddWithValue("@CustomerCode", customerCode);

                using var reader = await cmd.ExecuteReaderAsync();

                var list = new List<object>();
                while (await reader.ReadAsync())
                {
                    list.Add(new
                    {
                        id           = Convert.ToInt32(reader["Id"]),
                        customerCode = reader["CustomerCode"].ToString(),
                        customerName = reader["CustomerName"] == DBNull.Value ? null : reader["CustomerName"].ToString(),
                        rating       = reader["Rating"].ToString(),
                        reviewText   = reader["ReviewText"] == DBNull.Value ? null : reader["ReviewText"].ToString(),
                        isActive     = Convert.ToBoolean(reader["IsActive"]),
                        createdDate  = Convert.ToDateTime(reader["CreatedDate"]).ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }

                return Ok(new { success = true, data = list });
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
        // PUT api/CustomerReviews/toggle-active/{id}
        // Flips the IsActive flag (admin approve / hide review).
        // -------------------------------------------------------
        [HttpPut("toggle-active/{id}")]
        public async Task<IActionResult> ToggleActive(int id)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    UPDATE CustomerReviews
                       SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END
                     WHERE Id = @Id;

                    SELECT IsActive FROM CustomerReviews WHERE Id = @Id;",
                    connection);

                cmd.Parameters.AddWithValue("@Id", id);

                var result = await cmd.ExecuteScalarAsync();

                if (result == null)
                    return NotFound(new { success = false, message = $"Review with Id {id} not found." });

                bool isActive = Convert.ToBoolean(result);

                return Ok(new
                {
                    success  = true,
                    id,
                    isActive,
                    message  = isActive ? "Review is now active." : "Review is now inactive."
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

        // -------------------------------------------------------
        // DELETE api/CustomerReviews/delete/{id}
        // Permanently removes a review by Id.
        // -------------------------------------------------------
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteReview(int id)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(
                    "DELETE FROM CustomerReviews WHERE Id = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", id);

                int rows = await cmd.ExecuteNonQueryAsync();

                if (rows == 0)
                    return NotFound(new { success = false, message = $"Review with Id {id} not found." });

                return Ok(new { success = true, message = $"Review {id} deleted successfully." });
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

    // -------------------------------------------------------
    // Request model
    // -------------------------------------------------------
    public class AddReviewRequest
    {
        public string  ItemCode { get; set; } = string.Empty;   // itemCode
        public string  CusCode  { get; set; } = string.Empty;   // cusCode → CustomerCode
        public string  Fid      { get; set; } = string.Empty;   // fid → uniqueID
        public string  Rating   { get; set; } = string.Empty;   // rating → Rating
        public string? Comment  { get; set; }                   // comment → ReviewText
    }
}

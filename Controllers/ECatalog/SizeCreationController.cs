using System.Data;
using CHITSCHEME.Global;
using JEWELLBISREACT.DBConnection;
using JEWELLBISREACT.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace JEWELLBISREACT.Controllers.ECatalog
{
    [Route("api/[controller]")]
    [ApiController]
    public class SizeCreationController : ControllerBase
    {
        // -------------------------------------------------------
        // GET – list with pagination & search
        // GET /api/SizeCreation/getSizeItem?page=1&pageSize=10&search=small
        // -------------------------------------------------------
        [HttpGet("getSizeItem")]
        public async Task<IActionResult> GetSizeItems(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string search = "")
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            try
            {
                string whereClause = string.IsNullOrWhiteSpace(search)
                    ? ""
                    : "WHERE UPPER(FSIZE) LIKE @search";

                string countQuery = $"SELECT COUNT(1) FROM SIZE {whereClause}";
                string dataQuery  = $@"
                    SELECT FCODE, FSIZE
                    FROM   SIZE
                    {whereClause}
                    ORDER  BY FSIZE ASC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                int total = 0;
                var list  = new List<Create_Size>();

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    using (SqlCommand cmd = new SqlCommand(countQuery, con))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.ToUpper() + "%");

                        total = (int)await cmd.ExecuteScalarAsync();
                    }

                    using (SqlCommand cmd = new SqlCommand(dataQuery, con))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.ToUpper() + "%");

                        cmd.Parameters.AddWithValue("@offset",   (page - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                list.Add(new Create_Size
                                {
                                    Fcode = reader["FCODE"] == DBNull.Value ? null : reader["FCODE"].ToString(),
                                    Fsize = reader["FSIZE"] == DBNull.Value ? null : reader["FSIZE"].ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    totalRecords = total,
                    page,
                    pageSize,
                    totalPages  = (int)Math.Ceiling((double)total / pageSize),
                    data        = list
                });
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET – next fcode
        // -------------------------------------------------------
        [HttpGet("SizeNextFcode")]
        public async Task<IActionResult> GetNextFcode()
        {
            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand("SELECT MAX(FCODE) FROM SIZE", con))
                    {
                        var result  = await cmd.ExecuteScalarAsync();
                        string max  = (result == null || result == DBNull.Value) ? "0000" : result.ToString();
                        int current = int.TryParse(max, out int parsed) ? parsed : 0;
                        int next    = current + 1;
                        string code = next.ToString().PadLeft(Math.Max(max.Length, 4), '0');
                        return Ok(code);
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // Helper
        // -------------------------------------------------------
        private bool SizeExists(SqlConnection con, string fsize, string excludeCode = null)
        {
            string query = "SELECT 1 FROM SIZE WHERE UPPER(FSIZE) = @FSIZE";
            if (excludeCode != null) query += " AND FCODE <> @FCODE";

            using (SqlCommand cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@FSIZE", fsize.ToUpper());
                if (excludeCode != null)
                    cmd.Parameters.AddWithValue("@FCODE", excludeCode);

                return cmd.ExecuteScalar() != null;
            }
        }

        // -------------------------------------------------------
        // POST – create size
        // -------------------------------------------------------
        [HttpPost("createSize")]
        public async Task<IActionResult> CreateSize([FromBody] Create_Size newSize)
        {
            if (newSize == null)
                return BadRequest(new { message = "Invalid size data." });

            if (string.IsNullOrWhiteSpace(newSize.Fcode) || string.IsNullOrWhiteSpace(newSize.Fsize))
                return BadRequest(new { message = "Size code and size name cannot be empty." });

            if (newSize.Fsize.Length > 15)
                return BadRequest(new { message = "Size name must be 1-15 characters long." });

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    if (SizeExists(con, newSize.Fsize))
                        return Conflict(new { message = "This size already exists." });

                    using (SqlCommand cmd = new SqlCommand(
                        "INSERT INTO SIZE (FCODE, FSIZE) VALUES (@FCODE, @FSIZE)", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE", newSize.Fcode);
                        cmd.Parameters.AddWithValue("@FSIZE", newSize.Fsize.ToUpper());

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return StatusCode(500, new { message = "Failed to create size." });
                    }
                }

                return Ok(new { message = "Size created successfully." });
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // PUT – update size  (FIX: duplicate check before SqlCommand, NotFound on 0 rows, async)
        // -------------------------------------------------------
        [HttpPut("UpdateSize")]
        public async Task<IActionResult> UpdateSize([FromBody] Create_Size newSize)
        {
            if (newSize == null)
                return BadRequest(new { message = "Invalid size data." });

            if (string.IsNullOrWhiteSpace(newSize.Fcode) || string.IsNullOrWhiteSpace(newSize.Fsize))
                return BadRequest(new { message = "Size code and size name cannot be empty." });

            if (newSize.Fsize.Length > 15)
                return BadRequest(new { message = "Size name must be 1-15 characters long." });

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    // Duplicate check BEFORE executing the update
                    if (SizeExists(con, newSize.Fsize, newSize.Fcode))
                        return Conflict(new { message = "Size name already exists. Please choose a different name." });

                    using (SqlCommand cmd = new SqlCommand(
                        "UPDATE SIZE SET FSIZE = @FSIZE WHERE FCODE = @FCODE", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE", newSize.Fcode);
                        cmd.Parameters.AddWithValue("@FSIZE", newSize.Fsize.ToUpper());

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return NotFound(new { message = "Size record not found." });
                    }
                }

                return Ok(new { message = "Size updated successfully." });
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // DELETE – delete size  (FIX: DoesValueExist inside try/catch)
        // -------------------------------------------------------
        [HttpDelete("DeleteSize/{fCode}")]
        public async Task<IActionResult> DeleteSize([FromRoute] string fCode)
        {
            if (string.IsNullOrWhiteSpace(fCode))
                return BadRequest(new { message = "Size code cannot be empty." });

            try
            {
                var checker = new CheckIfValueExists();
                bool usedInTx       = await checker.DoesValueExist("ITEMTRANSACTION", "FSIZE", fCode);
                bool usedInPurchase = await checker.DoesValueExist("ITEMPURCHASE",    "FSIZE", fCode);

                if (usedInTx || usedInPurchase)
                    return Conflict(new { message = "Size is used in related tables and cannot be deleted." });

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand("DELETE FROM SIZE WHERE FCODE = @FCODE", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE", fCode);
                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return NotFound(new { message = "Size record not found." });
                    }
                }

                return Ok(new { message = "Size deleted successfully." });
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }
    }
}

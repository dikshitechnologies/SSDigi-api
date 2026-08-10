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
    public class CounterCreationController : ControllerBase
    {
        // -------------------------------------------------------
        // GET – list with pagination & search
        // GET /api/CounterCreation/getCounterList?page=1&pageSize=10&search=gold
        // -------------------------------------------------------
        [HttpGet("getCounterList")]
        public async Task<IActionResult> GetCounterList(
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
                    : "WHERE UPPER(fbox) LIKE @search";

                string countQuery = $"SELECT COUNT(1) FROM Box {whereClause}";
                string dataQuery  = $@"
                    SELECT fcode, fbox, fwt, ftagwt
                    FROM   Box
                    {whereClause}
                    ORDER  BY fbox ASC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                int total = 0;
                var list  = new List<Counter_Creation>();

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // total count
                    using (SqlCommand cmd = new SqlCommand(countQuery, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.ToUpper() + "%");

                        total = (int)await cmd.ExecuteScalarAsync();
                    }

                    // paged data
                    using (SqlCommand cmd = new SqlCommand(dataQuery, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.ToUpper() + "%");

                        cmd.Parameters.AddWithValue("@offset",   (page - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                list.Add(new Counter_Creation
                                {
                                    Fcode   = reader["fcode"]?.ToString(),
                                    Fbox    = reader["fbox"]?.ToString(),
                                    FboxWt  = string.IsNullOrEmpty(reader["fwt"]?.ToString())    ? "-" : reader["fwt"].ToString(),
                                    FTagWt  = string.IsNullOrEmpty(reader["ftagwt"]?.ToString()) ? "-" : reader["ftagwt"].ToString(),
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
        [HttpGet("CounterNextFcode")]
        public async Task<IActionResult> GetNextFcode()
        {
            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand("SELECT MAX(FCODE) FROM BOX", con))
                    {
                        var result   = await cmd.ExecuteScalarAsync();
                        string max   = (result == null || result == DBNull.Value) ? "0001" : result.ToString();
                        int current  = int.TryParse(max, out int parsed) ? parsed : 0;
                        int next     = current + 1;
                        string fcode = next.ToString().PadLeft(Math.Max(max.Length, 4), '0');
                        return Ok(fcode);
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
        // Helpers
        // -------------------------------------------------------
        private bool BOXExists(SqlConnection con, string fbox, string excludeCode = null)
        {
            string query = "SELECT 1 FROM BOX WHERE UPPER(FBOX) = @FBOX";
            if (excludeCode != null) query += " AND FCODE <> @FCODE";

            using (SqlCommand cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@FBOX", fbox.ToUpper());
                if (excludeCode != null)
                    cmd.Parameters.AddWithValue("@FCODE", excludeCode);

                return cmd.ExecuteScalar() != null;
            }
        }

        // -------------------------------------------------------
        // POST – create counter
        // -------------------------------------------------------
        [HttpPost("createCounter")]
        public async Task<IActionResult> CreateCounter([FromBody] Counter_Creation newCounter)
        {
            if (newCounter == null)
                return BadRequest(new { message = "Invalid counter data." });

            if (string.IsNullOrWhiteSpace(newCounter.Fcode) || string.IsNullOrWhiteSpace(newCounter.Fbox))
                return BadRequest(new { message = "Counter code and box name cannot be empty." });

            if (newCounter.Fbox.Length > 25)
                return BadRequest(new { message = "Box name must be 1-25 characters long." });

            float? fboxWt = null;
            if (!string.IsNullOrWhiteSpace(newCounter.FboxWt))
            {
                if (!float.TryParse(newCounter.FboxWt, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return BadRequest(new { message = "FboxWt must be a valid number." });
                fboxWt = v;
            }

            float? ftagWt = null;
            if (!string.IsNullOrWhiteSpace(newCounter.FTagWt))
            {
                if (!float.TryParse(newCounter.FTagWt, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return BadRequest(new { message = "FTagWt must be a valid number." });
                ftagWt = v;
            }

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    if (BOXExists(con, newCounter.Fbox))
                        return Conflict(new { message = "This Box name already exists." });

                    using (SqlCommand cmd = new SqlCommand(
                        "INSERT INTO BOX (FCODE, FBOX, FWT, FTAGWT) VALUES (@FCODE, @FBOX, @FWT, @FTAGWT)", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE",  newCounter.Fcode);
                        cmd.Parameters.AddWithValue("@FBOX",   newCounter.Fbox.ToUpper());
                        cmd.Parameters.AddWithValue("@FWT",    fboxWt.HasValue ? (object)fboxWt.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@FTAGWT", ftagWt.HasValue ? (object)ftagWt.Value : DBNull.Value);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return StatusCode(500, new { message = "Failed to create counter." });
                    }
                }

                return Ok(new { message = "Counter created successfully." });
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
        // PUT – update counter   (FIX: duplicate check before command, async throughout)
        // -------------------------------------------------------
        [HttpPut("UpdateCounter")]
        public async Task<IActionResult> UpdateCounter([FromBody] Counter_Creation newCounter)
        {
            if (newCounter == null)
                return BadRequest(new { message = "Invalid counter data." });

            if (string.IsNullOrWhiteSpace(newCounter.Fcode) || string.IsNullOrWhiteSpace(newCounter.Fbox))
                return BadRequest(new { message = "Counter code and box name cannot be empty." });

            if (newCounter.Fbox.Length > 25)
                return BadRequest(new { message = "Box name must be 1-25 characters long." });

            float? fboxWt = null;
            if (!string.IsNullOrWhiteSpace(newCounter.FboxWt))
            {
                if (!float.TryParse(newCounter.FboxWt, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return BadRequest(new { message = "FboxWt must be a valid number." });
                fboxWt = v;
            }

            float? ftagWt = null;
            if (!string.IsNullOrWhiteSpace(newCounter.FTagWt))
            {
                if (!float.TryParse(newCounter.FTagWt, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return BadRequest(new { message = "FTagWt must be a valid number." });
                ftagWt = v;
            }

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    // Duplicate check BEFORE executing the update
                    if (BOXExists(con, newCounter.Fbox, newCounter.Fcode))
                        return Conflict(new { message = "Counter name already exists. Please choose a different name." });

                    using (SqlCommand cmd = new SqlCommand(
                        "UPDATE BOX SET FBOX=@FBOX, FWT=@FWT, FTAGWT=@FTAGWT WHERE FCODE=@FCODE", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE",  newCounter.Fcode);
                        cmd.Parameters.AddWithValue("@FBOX",   newCounter.Fbox.ToUpper());
                        cmd.Parameters.AddWithValue("@FWT",    fboxWt.HasValue ? (object)fboxWt.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@FTAGWT", ftagWt.HasValue ? (object)ftagWt.Value : DBNull.Value);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return NotFound(new { message = "Counter not found." });
                    }
                }

                return Ok(new { message = "Counter updated successfully." });
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
        // DELETE – delete counter  (FIX: DoesValueExist inside try/catch)
        // -------------------------------------------------------
        [HttpDelete("deleteCounter/{fCode}")]
        public async Task<IActionResult> DeleteCounter([FromRoute] string fCode)
        {
            if (string.IsNullOrWhiteSpace(fCode))
                return BadRequest(new { message = "Counter code cannot be empty." });

            try
            {
                var checker = new CheckIfValueExists();
                bool usedInTx       = await checker.DoesValueExist("ITEMTRANSACTION", "FBOX", fCode);
                bool usedInPurchase = await checker.DoesValueExist("ITEMPURCHASE",    "FBOX", fCode);

                if (usedInTx || usedInPurchase)
                    return Conflict(new { message = "Counter is used in related tables and cannot be deleted." });

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand("DELETE FROM BOX WHERE FCODE = @FCODE", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE", fCode);
                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return NotFound(new { message = "Counter not found." });
                    }
                }

                return Ok(new { message = "Counter deleted successfully." });
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

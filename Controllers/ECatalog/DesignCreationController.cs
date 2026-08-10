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
    public class DesignCreationController : ControllerBase
    {
        // -------------------------------------------------------
        // GET – list with pagination & search
        // GET /api/DesignCreation/getDesignItem?page=1&pageSize=10&search=ring
        // -------------------------------------------------------
        [HttpGet("getDesignItem")]
        public async Task<IActionResult> GetDesignItems(
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
                    : "WHERE UPPER(FNAME) LIKE @search";

                string countQuery = $"SELECT COUNT(1) FROM DESIGN {whereClause}";
                string dataQuery  = $@"
                    SELECT FCODE, FNAME
                    FROM   DESIGN
                    {whereClause}
                    ORDER  BY FNAME ASC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                int total = 0;
                var list  = new List<DesignItems>();

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
                                list.Add(new DesignItems
                                {
                                    DesignCode = reader["FCODE"] == DBNull.Value ? null : reader["FCODE"].ToString(),
                                    DesignName = reader["FNAME"] == DBNull.Value ? null : reader["FNAME"].ToString()
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
        [HttpGet("getNextFcode")]
        public async Task<IActionResult> GetNextFcode()
        {
            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand("SELECT MAX(FCODE) FROM DESIGN", con))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        string max = (result == null || result == DBNull.Value) ? "0000" : result.ToString();
                        int current = int.TryParse(max, out int v) ? v : 0;
                        string next = (current + 1).ToString().PadLeft(4, '0');
                        return Ok(next);
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
        private bool DesignNameExists(SqlConnection con, string name, string excludeCode = null)
        {
            string query = "SELECT 1 FROM DESIGN WHERE UPPER(FNAME) = @FNAME";
            if (excludeCode != null) query += " AND FCODE <> @FCODE";

            using (SqlCommand cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@FNAME", name.ToUpper());
                if (excludeCode != null)
                    cmd.Parameters.AddWithValue("@FCODE", excludeCode);

                return cmd.ExecuteScalar() != null;
            }
        }

        // -------------------------------------------------------
        // POST – create design
        // -------------------------------------------------------
        [HttpPost("createDesign")]
        public async Task<IActionResult> CreateDesign([FromBody] DesignItem newDesign)
        {
            if (newDesign == null)
                return BadRequest(new { message = "Design data is required." });

            if (string.IsNullOrWhiteSpace(newDesign.DesignCode) || string.IsNullOrWhiteSpace(newDesign.DesignName))
                return BadRequest(new { message = "DesignCode and DesignName are required." });

            if (newDesign.DesignCode.Length > 10 || newDesign.DesignName.Length > 25)
                return BadRequest(new { message = "DesignCode max 10 chars, DesignName max 25 chars." });

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    if (DesignNameExists(con, newDesign.DesignName))
                        return Conflict(new { message = "Design name already exists." });

                    using (SqlCommand cmd = new SqlCommand(
                        "INSERT INTO DESIGN (FCODE, FNAME) VALUES (@FCODE, @FNAME)", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE", newDesign.DesignCode);
                        cmd.Parameters.AddWithValue("@FNAME", newDesign.DesignName.ToUpper());

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return StatusCode(500, new { message = "Failed to create design item." });
                    }
                }

                return Ok(new { message = "Design created successfully." });
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
        // PUT – update design  (FIX: duplicate check before SqlCommand, async)
        // -------------------------------------------------------
        [HttpPut("updateDesign")]
        public async Task<IActionResult> UpdateDesign([FromBody] DesignItem newDesign)
        {
            if (newDesign == null)
                return BadRequest(new { message = "Design data is required." });

            if (string.IsNullOrWhiteSpace(newDesign.DesignCode) || string.IsNullOrWhiteSpace(newDesign.DesignName))
                return BadRequest(new { message = "DesignCode and DesignName are required." });

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    // Duplicate check BEFORE executing the update
                    if (DesignNameExists(con, newDesign.DesignName, newDesign.DesignCode))
                        return Conflict(new { message = "Design name already exists. Please choose a different name." });

                    using (SqlCommand cmd = new SqlCommand(
                        "UPDATE DESIGN SET FNAME = @FNAME WHERE FCODE = @FCODE", con))
                    {
                        cmd.Parameters.AddWithValue("@FNAME", newDesign.DesignName.ToUpper());
                        cmd.Parameters.AddWithValue("@FCODE", newDesign.DesignCode);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return NotFound(new { message = "Design item not found." });
                    }
                }

                return Ok(new { message = "Design updated successfully." });
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
        // DELETE – delete design  (FIX: DoesValueExist inside try/catch)
        // -------------------------------------------------------
        [HttpDelete("deleteDesign/{designCode}")]
        public async Task<IActionResult> DeleteDesign([FromRoute] string designCode)
        {
            if (string.IsNullOrWhiteSpace(designCode))
                return BadRequest(new { message = "DesignCode is required." });

            try
            {
                var checker = new CheckIfValueExists();
                bool usedInTx       = await checker.DoesValueExist("ITEMTRANSACTION", "FDESIGN", designCode);
                bool usedInPurchase = await checker.DoesValueExist("ITEMPURCHASE",    "FDESIGN", designCode);

                if (usedInTx || usedInPurchase)
                    return Conflict(new { message = "Design is used in related tables and cannot be deleted." });

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(
                        "DELETE FROM DESIGN WHERE FCODE = @FCODE", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE", designCode);
                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return NotFound(new { message = "Design item not found." });
                    }
                }

                return Ok(new { message = "Design deleted successfully." });
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

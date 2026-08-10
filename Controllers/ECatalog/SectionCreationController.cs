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
    public class SectionCreationController : ControllerBase
    {
        // -------------------------------------------------------
        // GET – list with pagination & search
        // GET /api/SectionCreation/getSectionItems?page=1&pageSize=10&search=gold
        // -------------------------------------------------------
        [HttpGet("getSectionItems")]
        public async Task<IActionResult> GetSectionItems(
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

                string countQuery = $"SELECT COUNT(1) FROM SECTION {whereClause}";
                string dataQuery  = $@"
                    SELECT FCODE, FNAME
                    FROM   SECTION
                    {whereClause}
                    ORDER  BY FNAME ASC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                int total = 0;
                var list  = new List<Section_Creation>();

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
                                list.Add(new Section_Creation
                                {
                                    SectionCode = reader["FCODE"] == DBNull.Value ? null : reader["FCODE"].ToString().Trim(),
                                    SectionName = reader["FNAME"] == DBNull.Value ? null : reader["FNAME"].ToString().Trim()
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
        [HttpGet("getNextSection")]
        public async Task<IActionResult> GetNextFcode()
        {
            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand("SELECT MAX(FCODE) FROM SECTION", con))
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
        private bool SectionNameExists(SqlConnection con, string name, string excludeCode = null)
        {
            string query = "SELECT 1 FROM SECTION WHERE UPPER(FNAME) = @FNAME";
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
        // POST – create section
        // -------------------------------------------------------
        [HttpPost("createSection")]
        public async Task<IActionResult> CreateSection([FromBody] Section_Creation newSection)
        {
            if (newSection == null)
                return BadRequest(new { message = "Section data is required." });

            if (string.IsNullOrWhiteSpace(newSection.SectionCode) || string.IsNullOrWhiteSpace(newSection.SectionName))
                return BadRequest(new { message = "SectionCode and SectionName are required." });

            if (newSection.SectionCode.Length > 10 || newSection.SectionName.Length > 25)
                return BadRequest(new { message = "SectionCode max 10 chars, SectionName max 25 chars." });

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    if (SectionNameExists(con, newSection.SectionName))
                        return Conflict(new { message = "Section name already exists." });

                    using (SqlCommand cmd = new SqlCommand(
                        "INSERT INTO SECTION (FCODE, FNAME) VALUES (@FCODE, @FNAME)", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE", newSection.SectionCode.Trim());
                        cmd.Parameters.AddWithValue("@FNAME", newSection.SectionName.Trim().ToUpper());

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return StatusCode(500, new { message = "Failed to create section." });
                    }
                }

                return Ok(new { message = "Section created successfully." });
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
        // PUT – update section  (FIX: duplicate check before SqlCommand, async)
        // -------------------------------------------------------
        [HttpPut("updateSection")]
        public async Task<IActionResult> UpdateSection([FromBody] Section_Creation newSection)
        {
            if (newSection == null)
                return BadRequest(new { message = "Section data is required." });

            if (string.IsNullOrWhiteSpace(newSection.SectionCode) || string.IsNullOrWhiteSpace(newSection.SectionName))
                return BadRequest(new { message = "SectionCode and SectionName are required." });

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    // Duplicate check BEFORE executing the update
                    if (SectionNameExists(con, newSection.SectionName, newSection.SectionCode))
                        return Conflict(new { message = "Section name already exists. Please choose a different name." });

                    using (SqlCommand cmd = new SqlCommand(
                        "UPDATE SECTION SET FNAME = @FNAME WHERE FCODE = @FCODE", con))
                    {
                        cmd.Parameters.AddWithValue("@FNAME", newSection.SectionName.Trim().ToUpper());
                        cmd.Parameters.AddWithValue("@FCODE", newSection.SectionCode.Trim());

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return NotFound(new { message = "Section not found." });
                    }
                }

                return Ok(new { message = "Section updated successfully." });
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
        // DELETE – delete section  (FIX: DoesValueExist inside try/catch, async)
        // -------------------------------------------------------
        [HttpDelete("deleteSection/{sectionCode}")]
        public async Task<IActionResult> DeleteSection([FromRoute] string sectionCode)
        {
            if (string.IsNullOrWhiteSpace(sectionCode))
                return BadRequest(new { message = "SectionCode is required." });

            try
            {
                var checker = new CheckIfValueExists();
                bool usedInTx       = await checker.DoesValueExist("ITEMTRANSACTION", "FSECTION", sectionCode);
                bool usedInPurchase = await checker.DoesValueExist("ITEMPURCHASE",    "FSECTION", sectionCode);

                if (usedInTx || usedInPurchase)
                    return Conflict(new { message = "Section is used in related tables and cannot be deleted." });

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(
                        "DELETE FROM SECTION WHERE FCODE = @FCODE", con))
                    {
                        cmd.Parameters.AddWithValue("@FCODE", sectionCode);
                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            return NotFound(new { message = "Section not found." });
                    }
                }

                return Ok(new { message = "Section deleted successfully." });
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

using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.DigiBenefits
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class DigiBenefitsController : ControllerBase
    {
        public class SchemeBenefitDto
        {
            public string SchemeCode { get; set; }
            public string SchemeName { get; set; }
            public string Slab1Per { get; set; }
            public string Slab2Per { get; set; }
            public string Slab3Per { get; set; }
            public string Slab4Per { get; set; }
        }

        public class UpdateBenefitDto
        {
            public string SchemeCode { get; set; }
            public string Slab1Per { get; set; }
            public string Slab2Per { get; set; }
            public string Slab3Per { get; set; }
            public string Slab4Per { get; set; }
        }

        [HttpGet("GetBenefits")]
        public async Task<IActionResult> GetBenefits()
        {
            var benefitsList = new List<SchemeBenefitDto>();

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    string query = @"
                        SELECT SchemeCode, SchemeName, Slab1Per, Slab2Per, Slab3Per, Slab4Per
                        FROM SchemeBenefitMaster";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                benefitsList.Add(new SchemeBenefitDto
                                {
                                    SchemeCode = reader["SchemeCode"]?.ToString(),
                                    SchemeName = reader["SchemeName"]?.ToString(),
                                    Slab1Per  = reader["Slab1Per"]?.ToString(),
                                    Slab2Per  = reader["Slab2Per"]?.ToString(),
                                    Slab3Per  = reader["Slab3Per"]?.ToString(),
                                    Slab4Per  = reader["Slab4Per"]?.ToString(),
                                });
                            }
                        }
                    }
                }

                return Ok(benefitsList);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Database error occurred.", details = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Unexpected error occurred.", details = ex.Message });
            }
        }

        [HttpPut("updateBenefit")]
        public async Task<IActionResult> UpdateBenefit([FromBody] UpdateBenefitDto payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.SchemeCode))
                return BadRequest(new { error = "SchemeCode is required." });

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    string query = @"
                        UPDATE SchemeBenefitMaster
                        SET Slab1Per = @Slab1Per,
                            Slab2Per = @Slab2Per,
                            Slab3Per = @Slab3Per,
                            Slab4Per = @Slab4Per
                        WHERE SchemeCode = @SchemeCode";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SchemeCode", payload.SchemeCode);
                        cmd.Parameters.AddWithValue("@Slab1Per", payload.Slab1Per ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Slab2Per", payload.Slab2Per ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Slab3Per", payload.Slab3Per ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Slab4Per", payload.Slab4Per ?? (object)DBNull.Value);

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                            return NotFound(new { error = "No record found for the given SchemeCode." });
                    }
                }

                return Ok(new { message = "Benefit updated successfully.", schemeCode = payload.SchemeCode });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { error = "Database error occurred.", details = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Unexpected error occurred.", details = ex.Message });
            }
        }
    }
}

using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SettingsController : ControllerBase
    {
        [HttpPost("SavePlatformCharge")]
        public async Task<IActionResult> SavePlatformCharge(
         [FromBody] PlatformChargePostDto dto)
        {
            using SqlConnection conn = new SqlConnection(DBHelper.GetConnection());
            await conn.OpenAsync();

            DateTime today = DateTime.Today;

            async Task DeleteAndInsert(string type, ChargeDto data)
            {
                if (data == null) return;

                // ❌ DELETE existing
                string deleteSql = @"
            DELETE FROM PlatformCharge
            WHERE fType = @Type";

                using (SqlCommand deleteCmd = new SqlCommand(deleteSql, conn))
                {
                    deleteCmd.Parameters.AddWithValue("@Type", type);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                // ➕ INSERT fresh
                string insertSql = @"
            INSERT INTO PlatformCharge (fType, fPlatformFee, fGst, fDate)
            VALUES (@Type, @Fee, @Gst, @Date)";

                using (SqlCommand insertCmd = new SqlCommand(insertSql, conn))
                {
                    insertCmd.Parameters.AddWithValue("@Type", type);
                    insertCmd.Parameters.AddWithValue("@Fee", data.PlatformFee);
                    insertCmd.Parameters.AddWithValue("@Gst", data.GstPercent);
                    insertCmd.Parameters.AddWithValue("@Date", today);

                    await insertCmd.ExecuteNonQueryAsync();
                }
            }

            await DeleteAndInsert("P", dto.Scheme);
            await DeleteAndInsert("E", dto.Ecatalog);

            return Ok(new
            {
                Success = true,
                Message = "Platform charges replaced successfully"
            });
        }

        [HttpGet("GetPlatformCharge")]
        public async Task<IActionResult> GetPlatformCharge()
        {
            using SqlConnection conn = new SqlConnection(DBHelper.GetConnection());
            await conn.OpenAsync();

            DateTime today = DateTime.Today;

            string sql = @"
        SELECT fType, fPlatformFee, fGst
        FROM PlatformCharge";

            using SqlCommand cmd = new SqlCommand(sql, conn);

            ChargeDto scheme = null;
            ChargeDto ecatalog = null;

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (reader.Read())
            {
                string type = reader["fType"].ToString();
                decimal fee = Convert.ToDecimal(reader["fPlatformFee"]);
                decimal gst = Convert.ToDecimal(reader["fGst"]);

                if (type == "P")
                    scheme = new ChargeDto { PlatformFee = fee, GstPercent = gst };
                else if (type == "E")
                    ecatalog = new ChargeDto { PlatformFee = fee, GstPercent = gst };
            }

            return Ok(new
            {
                scheme,
                ecatalog
            });
        }



    }
}


public class PlatformChargePostDto
{
    public ChargeDto Scheme { get; set; }
    public ChargeDto Ecatalog { get; set; }
}

public class ChargeDto
{
    public decimal PlatformFee { get; set; }
    public decimal GstPercent { get; set; }
}

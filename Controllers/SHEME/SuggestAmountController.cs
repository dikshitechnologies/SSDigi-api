using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.SHEME
{
    [Route("api/[controller]")]
    [ApiController]
    public class SuggestAmountController : ControllerBase
    {
        [HttpPost("SaveAmountSuggestion")]
        public async Task<IActionResult> SaveAmountSuggestion(
         [FromBody] AmountSuggestionDto dto)
        {
            using SqlConnection conn = new SqlConnection(DBHelper.GetConnection());
            await conn.OpenAsync();

            DateTime today = DateTime.Today;
            using SqlTransaction tran = conn.BeginTransaction();

            try
            {
                // ❌ DELETE OLD DATA FOR TODAY
                string deleteSql = @"
            DELETE FROM AmountSuggestion WHERE
               MetalType IN ('GOLD22K','GOLD24K','SILVER')";

                using (SqlCommand delCmd = new SqlCommand(deleteSql, conn, tran))
                {
                    //delCmd.Parameters.AddWithValue("@Date", today);
                    await delCmd.ExecuteNonQueryAsync();
                }

                // 🔁 INSERT HELPER
                async Task InsertMetal(string metal, List<decimal> values)
                {
                    if (values == null || values.Count != 4)
                        throw new Exception($"{metal} must contain exactly 4 values");

                    foreach (var amt in values)
                    {
                        using SqlCommand cmd = new SqlCommand(@"
                    INSERT INTO AmountSuggestion
                    (MetalType, SuggestAmount, CreatedDate)
                    VALUES (@Metal, @Amount, @Date)", conn, tran);

                        cmd.Parameters.AddWithValue("@Metal", metal);
                        cmd.Parameters.AddWithValue("@Amount", amt);
                        cmd.Parameters.AddWithValue("@Date", today);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await InsertMetal("GOLD22K", dto.Gold22k);
                await InsertMetal("GOLD24K", dto.Gold24k);
                await InsertMetal("SILVER", dto.Silver);

                tran.Commit();

                return Ok(new
                {
                    Success = true,
                    Message = "Amount suggestions updated successfully"
                });
            }
            catch (Exception ex)
            {
                tran.Rollback();
                return BadRequest(ex.Message);
            }
        }




        [HttpGet("GetAmountSuggestion")]
        public async Task<IActionResult> GetAmountSuggestion()
        {
            using SqlConnection conn = new SqlConnection(DBHelper.GetConnection());
            await conn.OpenAsync();

            DateTime today = DateTime.Today;

            string sql = @"
        SELECT MetalType, SuggestAmount
        FROM AmountSuggestion
        ORDER BY SuggestAmount";

            using SqlCommand cmd = new SqlCommand(sql, conn);
            //cmd.Parameters.AddWithValue("@Date", today);

            var gold22k = new List<decimal>();
            var gold24k = new List<decimal>();
            var silver = new List<decimal>();

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (reader.Read())
            {
                string metal = reader["MetalType"].ToString();
                decimal amt = Convert.ToDecimal(reader["SuggestAmount"]);

                if (metal == "GOLD22K") gold22k.Add(amt);
                else if (metal == "GOLD24K") gold24k.Add(amt);
                else if (metal == "SILVER") silver.Add(amt);
            }

            return Ok(new
            {
                gold22k,
                gold24k,
                silver
            });
        }


    }
}

public class AmountSuggestionDto
{
    public List<decimal> Gold22k { get; set; }
    public List<decimal> Gold24k { get; set; }
    public List<decimal> Silver { get; set; }
}

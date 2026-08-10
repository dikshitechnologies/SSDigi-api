using CHITSCHEME.Global;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [Authorize(Roles = "User,Admin")]
    [ApiController]
    public class CartViewController : ControllerBase
    {
        [HttpPost("AddToCart")]
        public async Task<IActionResult> AddToCart([FromBody] Cart cart)
        {
            if (cart == null || string.IsNullOrEmpty(cart.ProductCode))
            {
                return BadRequest(new { error = "Invalid cart data. Product code is required." });
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();

                    string checkProductQuery = "SELECT COUNT(*) FROM cartlist WHERE fCusid = @cusid AND fProductCode = @productCode AND FID =@FID";
                    using (SqlCommand checkCommand = new SqlCommand(checkProductQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@cusid", cart.CusCode);
                        checkCommand.Parameters.AddWithValue("@productCode", cart.ProductCode);
                        checkCommand.Parameters.AddWithValue("@FID", cart.FID);

                        int existingCount = (int)await checkCommand.ExecuteScalarAsync();
                        if (existingCount > 0)
                        {
                            return BadRequest(new { message = "You have already added this product to your cart." });
                        }
                    }

                    string maxCartIdQuery = "SELECT MAX(cartid) FROM cartlist";
                    using (SqlCommand maxIdCommand = new SqlCommand(maxCartIdQuery, connection))
                    {
                        object result = await maxIdCommand.ExecuteScalarAsync();

                        string newCartId;
                        if (result == DBNull.Value || result == null)
                        {
                            newCartId = "00001";
                        }
                        else
                        {
                            string lastCartId = result.ToString();
                            if (int.TryParse(lastCartId, out int lastId))
                            {
                                int nextId = lastId + 1;
                                newCartId = nextId <= 99999 ? nextId.ToString("D5") : nextId.ToString();
                            }
                            else
                            {
                                return StatusCode(500, new { message = "Invalid cart ID format in database." });
                            }
                        }

                        string insertQuery = "INSERT INTO cartlist (cartid, fCusid, fProductCode, Cdate,FID) " +
                                             "VALUES (@cartid, @cusid, @productCode, @date,@FID)";

                        using (SqlCommand insertCommand = new SqlCommand(insertQuery, connection))
                        {
                            insertCommand.Parameters.AddWithValue("@cartid", newCartId);
                            insertCommand.Parameters.AddWithValue("@cusid", cart.CusCode);
                            insertCommand.Parameters.AddWithValue("@productCode", cart.ProductCode);
                            insertCommand.Parameters.AddWithValue("@date", DateTime.Now);
                            insertCommand.Parameters.AddWithValue("@FID", cart.FID);

                            int insertResult = await insertCommand.ExecuteNonQueryAsync();

                            if (insertResult > 0)
                            {
                                return Ok(new { message = "Item added to cart successfully.", cartid = newCartId });
                            }
                            else
                            {
                                return StatusCode(500, new { message = "Failed to add item to cart." });
                            }
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error occurred.", details = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
            }
        }


        [HttpGet("CartItemCount/{fCusid}")]
        public async Task<IActionResult> GetCartItemCount([FromRoute] string fCusid)
        {
            try
            {
                int count = 0;

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    string query = "SELECT COUNT(*) FROM cartlist WHERE fCusid = @fCusid";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@fCusid", fCusid);

                        count = (int)await cmd.ExecuteScalarAsync();
                    }
                }

                return Ok(new { cartItemCount = count });
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { error = "Database error occurred.", details = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Unexpected error occurred.", details = ex.Message });
            }
        }

        [HttpGet]
        [Route("cartViewItem")]
        public async Task<IActionResult> GetCartItems(string fCusid)
        {
            if (string.IsNullOrEmpty(fCusid))
                return BadRequest(new { message = "Customer ID is required." });

            List<CartItem> cartItems = new List<CartItem>();
            decimal AlltotalAmount = 0;

            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT 
                    C.cartid,
                    C.fProductCode AS fItemcode,
                    C.fCusid,
                    op.fParent,
                    i.fItemName,
                    COALESCE(op.FImage1, op.FImage2, op.FImage3, op.FImage4) AS fimage,
                    op.fPieceRate,
                    op.Gms AS NetWt,
                    op.Gross AS fGrossWt,
                    op.Wastage,
                    op.McAmount AS McAmount,
                    op.fOthers,
                    op.fTax,
                    op.StnChrg AS StoneCharges,
                    d.fRate AS GoldRate,
                    c.FID
                FROM CartList C
                INNER JOIN ITEMPURCHASEOP op ON op.fID = C.FID
                LEFT JOIN Division d ON op.fDiv = d.fcode
                JOIN item i ON i.fItemcode = op.Itemcode
                WHERE C.fCusid = @fCusid
                ORDER BY C.cartid DESC;";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@fCusid", fCusid);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string piecerateFlag = reader["fPieceRate"]?.ToString();
                                decimal netWt = SafeGetDecimal(reader, "NetWt");
                                decimal grossWt = SafeGetDecimal(reader, "fGrossWt");
                                decimal wastage = SafeGetDecimal(reader, "Wastage");
                                decimal mc = SafeGetDecimal(reader, "McAmount");
                                decimal stoneCharges = SafeGetDecimal(reader, "StoneCharges");
                                decimal fOthers = SafeGetDecimal(reader, "fOthers");
                                decimal tax = SafeGetDecimal(reader, "fTax");
                                decimal goldRate = SafeGetDecimal(reader, "GoldRate");

                                decimal totalAmount = 0;

                                // ---------------- Price calculation ----------------
                                if (piecerateFlag?.ToUpper() == "Y")
                                {
                                    totalAmount = mc + tax;
                                }
                                else
                                {
                                    var priceResult = PriceCalculator.CalculatePrice(
                                        null, netWt, wastage, 0, goldRate, mc, fOthers, stoneCharges, tax, goldRate
                                    );
                                    totalAmount = priceResult.TotalAmount;
                                }

                                AlltotalAmount += totalAmount;

                                cartItems.Add(new CartItem
                                {
                                    CartId = reader["cartid"]?.ToString() ?? "",
                                    ItemCode = reader["fItemcode"]?.ToString() ?? "",
                                    fparent = reader["fParent"]?.ToString() ?? "",
                                    ItemName = reader["fItemName"]?.ToString() ?? "",
                                    Image = reader["fimage"]?.ToString() ?? "",
                                    FID = reader["FID"]?.ToString() ?? "",
                                    TotalPrice = totalAmount
                                });
                            }
                        }
                    }
                }

                if (cartItems.Count == 0)
                    return NotFound(new { message = "No items found in the cart." });

                return Ok(new
                {
                    AlltotalAmount,
                    cartItems
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An error occurred.", details = ex.Message });
            }
        }


        private decimal SafeGetDecimal(SqlDataReader reader, string columnName)
        {
            object value = reader[columnName];
            if (value == DBNull.Value || string.IsNullOrWhiteSpace(value.ToString()))
                return 0;

            return Convert.ToDecimal(value);
        }



        [HttpDelete("cartDeleteItem/{itemCode}/{FID}")]
        public IActionResult RemoveCartItem(string itemCode, [FromQuery] string fCusid,string FID)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    connection.Open();

                    string query = @"
                DELETE FROM CartList 
                WHERE fProductCode = @itemCode AND fCusid = @fCusid and FID = @FID";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@itemCode", itemCode);
                        command.Parameters.AddWithValue("@fCusid", fCusid);
                        command.Parameters.AddWithValue("@FID", FID);

                        int rowsAffected = command.ExecuteNonQuery();

                        if (rowsAffected > 0)
                        {
                            return Ok("Item removed successfully.");
                        }
                        else
                        {
                            return NotFound();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }




    }
}



public class Cart
{
    public string ProductCode { get; set; }
    public string CusCode { get; set; }
    public string FID { get; set; }
}

public class CartItem
{
    public string CartId { get; set; }
    public string ItemCode { get; set; }
    public string ItemName { get; set; }
    public string fparent { get; set; }
    public string Image { get; set; }
    public decimal TodayRate { get; set; }
    public decimal TotalPrice { get; set; }
    public string FID { get; set; }

}
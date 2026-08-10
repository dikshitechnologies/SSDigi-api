using CHITSCHEME.Global;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class WishListController : ControllerBase
    {

        [HttpPost("AddToWishlist")]
        public async Task<IActionResult> AddToCart([FromBody] Wishlist wishlist)
        {
            if (wishlist == null || string.IsNullOrEmpty(wishlist.ProductCode))
            {
                return BadRequest(new { message = "Invalid Wishlist data. Product code is required." });
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();

                    string checkProductQuery = "SELECT COUNT(*) FROM Wishlist WHERE fCusCode = @fCusCode AND fProductCode = @productCode AND FID = @FID";
                    using (SqlCommand checkCommand = new SqlCommand(checkProductQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@fCusCode", wishlist.CusCode);
                        checkCommand.Parameters.AddWithValue("@productCode", wishlist.ProductCode);
                        checkCommand.Parameters.AddWithValue("@FID", wishlist.FID);

                        int existingCount = (int)await checkCommand.ExecuteScalarAsync();
                        if (existingCount > 0)
                        {
                            return BadRequest(new { message = "You have already added this product to your Wishlist." });
                        }
                    }

                    string maxCartIdQuery = "SELECT MAX(fWishListId) FROM Wishlist";
                    using (SqlCommand maxIdCommand = new SqlCommand(maxCartIdQuery, connection))
                    {
                        object result = await maxIdCommand.ExecuteScalarAsync();

                        string newWishlistId;
                        if (result == DBNull.Value || result == null)
                        {
                            newWishlistId = "00001";
                        }
                        else
                        {
                            string lastWishlistCode = result.ToString();
                            if (int.TryParse(lastWishlistCode, out int lastCode))
                            {
                                int nextId = lastCode + 1;
                                newWishlistId = nextId <= 99999 ? nextId.ToString("D5") : nextId.ToString();
                            }
                            else
                            {
                                return StatusCode(500, new { error = "Invalid cart ID format in database." });
                            }
                        }

                        string insertQuery = "INSERT INTO Wishlist (fWishListId, fCusCode, fProductCode, fWdate,FID) " +
                                             "VALUES (@fWishListId, @fCusCode, @fProductCode, @fWdate,@FID)";

                        using (SqlCommand insertCommand = new SqlCommand(insertQuery, connection))
                        {
                            insertCommand.Parameters.AddWithValue("@fWishListId", newWishlistId);
                            insertCommand.Parameters.AddWithValue("@fCusCode", wishlist.CusCode);
                            insertCommand.Parameters.AddWithValue("@fProductCode", wishlist.ProductCode);
                            insertCommand.Parameters.AddWithValue("@fWdate", DateTime.Now);
                            insertCommand.Parameters.AddWithValue("@FID", wishlist.FID);

                            int insertResult = await insertCommand.ExecuteNonQueryAsync();

                            if (insertResult > 0)
                            {
                                return Ok(new { message = "Item added to Wishlist successfully.", wishlistCode = newWishlistId });
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
                return StatusCode(500, new { error = "Database error occurred.", details = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An unexpected error occurred.", details = ex.Message });
            }
        }

        [HttpGet("WishlistItemCount/{fCusid}")]
        public async Task<IActionResult> GetCartItemCount([FromRoute] string fCusid)
        {
            try
            {
                int count = 0;

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    string query = "SELECT COUNT(*) FROM Wishlist WHERE fCusCode = @fCusid";

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
                return StatusCode(500, new { message = "Database error occurred.", details = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Unexpected error occurred.", details = ex.Message });
            }
        }




        [HttpGet]
        [Route("WishlistViewItem")]
        public async Task<IActionResult> GetWishlistItems(string fCusCode)
        {
            if (string.IsNullOrEmpty(fCusCode))
                return BadRequest(new { message = "Customer ID is required." });

            List<WishlistItem> wishlistItems = new List<WishlistItem>();
            decimal AlltotalAmount = 0;

            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT 
                    W.fWishlistId AS CartId,
                    W.fProductCode AS fItemcode,
                    W.fCusCode,
                    op.fParent,
                    i.fItemName,
                    COALESCE(op.FImage1, op.FImage2, op.FImage3, op.FImage4) AS fimage,
                    op.fPieceRate,
                    op.Gms AS NetWt,
                    op.Gross AS fGrossWt,
                    op.Wastage,
                    op.McAmount,
                    op.fOthers,
                    op.fTax,
                    op.StnChrg AS StoneCharges,
                    d.fRate AS GoldRate,
                    op.fid
                FROM Wishlist W
                INNER JOIN ITEMPURCHASEOP op ON op.fID = W.FID   -- exact row per wishlist entry
                LEFT JOIN Division d ON op.fDiv = d.fcode
                JOIN item i ON W.fProductCode = i.fItemcode
                WHERE W.fCusCode = @fCusCode
                ORDER BY W.fWishlistId DESC;";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@fCusCode", fCusCode);

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

                                wishlistItems.Add(new WishlistItem
                                {
                                    CartId = reader["CartId"]?.ToString() ?? "",
                                    ItemCode = reader["fItemcode"]?.ToString() ?? "",
                                    fparent = reader["fParent"]?.ToString() ?? "",
                                    ItemName = reader["fItemName"]?.ToString() ?? "",
                                    Image = reader["fimage"]?.ToString() ?? "",
                                    FID = reader["FID"]?.ToString() ?? "",
                                    TodayRate = goldRate,
                                    TotalPrice = totalAmount
                                });
                            }
                        }
                    }
                }

                if (wishlistItems.Count == 0)
                    return NotFound(new { message = "No items found in wishlist." });

                return Ok(new
                {
                    AlltotalAmount,
                    wishlistItems
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




        [HttpDelete("WishlistDeleteItem/{itemCode}/{FID}")]
        public IActionResult RemoveCartItem(
     [FromRoute] string itemCode,
     [FromRoute] string FID,
     [FromQuery] string fCusCode)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    connection.Open();

                    string query = @"
                DELETE FROM wishlist 
                WHERE fProductCode = @itemCode AND fCusCode = @fCusCode AND fid = @fid";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@itemCode", itemCode);
                        command.Parameters.AddWithValue("@fCusCode", fCusCode);
                        command.Parameters.AddWithValue("@fid", FID);

                        int rowsAffected = command.ExecuteNonQuery();

                        if (rowsAffected > 0)
                        {
                            return Ok("Item removed successfully.");
                        }
                        else
                        {
                            return NotFound("Item not found.");
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







public class Wishlist
{
    public string ProductCode { get; set; }
    public string CusCode { get; set; }
    public decimal FID { get; set; }
}

public class WishlistItem
{

    public string CartId { get; set; }
    public string ItemCode { get; set; }
    public string fparent { get; set; }
    public string ItemName { get; set; }
    public string Image { get; set; }
    public decimal TodayRate { get; set; }
    public decimal TotalPrice { get; set; }
    public string FID { get; set; }



}





























//[HttpGet]
//[Route("WishlistViewItem")]
//public async Task<IActionResult> GetWishlistItems(string fCusCode)
//{
//    List<WishlistItem> wishlistItems = new List<WishlistItem>();


//    try
//    {
//        using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
//        {
//            await connection.OpenAsync();

//            string query = @"
//                        SELECT 
//                     C.fWishlistId, 
//	                 i.fparent,
//                     i.fItemcode, 
//                     i.fItemName, 
//                     i.fimage, 
//                     i.Weight, 
//                     i.NetWt, 
//                     i.fVA, 
//                     i.fVAGMS, 
//                     i.fMc, 
//                     i.fOthers, 
//                     i.fTax, 
//                     i.fStoneCharges, 
//                     i.fPieceRate, 
//                     i.fRate,
//                     d.fRate AS GoldRate
//                 FROM 
//                     Wishlist C 
//                 INNER JOIN 
//                     item i ON i.fItemcode = C.fProductCode
//                 INNER JOIN 
//                     Division D ON i.fPurity = d.fName
//                 WHERE 
//                     C.fCusCode =@fCusCode";

//            using (SqlCommand command = new SqlCommand(query, connection))
//            {
//                command.Parameters.AddWithValue("@fCusCode", fCusCode);

//                using (SqlDataReader reader = await command.ExecuteReaderAsync())
//                {
//                    while (await reader.ReadAsync())
//                    {
//                        string pieceRate = reader["fPieceRate"]?.ToString();
//                        decimal weight = SafeGetDecimal(reader, "Weight");
//                        decimal NetWt = SafeGetDecimal(reader, "NetWt");
//                        decimal vaPercent = SafeGetDecimal(reader, "fVA");
//                        decimal vaGrams = SafeGetDecimal(reader, "fVAGMS");
//                        decimal mc = SafeGetDecimal(reader, "fMc");
//                        decimal others = SafeGetDecimal(reader, "fOthers");
//                        decimal stoneCharges = SafeGetDecimal(reader, "fStoneCharges");
//                        decimal taxPercent = SafeGetDecimal(reader, "fTax");
//                        decimal goldRate = SafeGetDecimal(reader, "GoldRate");
//                        decimal fRate = SafeGetDecimal(reader, "fRate");

//                        decimal totalItemPrice = 0;
//                        decimal todayRate = 0;

//                        if (pieceRate == "Y")
//                        {
//                            totalItemPrice = fRate + mc + others + stoneCharges;
//                        }
//                        else
//                        {
//                            decimal totalWastage = (vaGrams > 0) ? vaGrams : (NetWt * vaPercent / 100);
//                            decimal totalWeightWithWastage = NetWt + totalWastage;

//                            todayRate = totalWeightWithWastage * goldRate;

//                            totalItemPrice = todayRate + mc + others + stoneCharges;
//                        }

//                        decimal taxAmount = (taxPercent > 0) ? (totalItemPrice * taxPercent / 100) : 0;
//                        totalItemPrice += taxAmount;


//                        WishlistItem item = new WishlistItem
//                        {
//                            CartId = reader["fWishlistId"]?.ToString() ?? "",
//                            ItemCode = reader["fItemcode"]?.ToString() ?? "",
//                            fparent = reader["fparent"]?.ToString() ?? "",
//                            ItemName = reader["fItemName"]?.ToString() ?? "",
//                            Image = reader["fimage"]?.ToString() ?? "",
//                            TodayRate = todayRate,
//                            TotalPrice = totalItemPrice,
//                        };

//                        wishlistItems.Add(item);
//                    }
//                }
//            }
//        }

//        if (wishlistItems.Count == 0)
//        {
//            return NotFound(new { message = "No items found in the wishlist." });
//        }

//        return Ok(new
//        {
//            wishlistItems
//        });
//    }
//    catch (SqlException sqlEx)
//    {
//        return StatusCode(500, new { message = "Database error occurred.", details = sqlEx.Message });
//    }
//    catch (Exception ex)
//    {
//        return StatusCode(500, new { message = "An unexpected error occurred.", details = ex.Message });
//    }
//}
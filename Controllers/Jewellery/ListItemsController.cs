using System.Text.Json.Serialization;
using CHITSCHEME.Global;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [Authorize(Roles = "Guest,User,Admin")]
    [ApiController]
    public class ListItemsController : ControllerBase
    {


        //------------------------------------------------All Category List Items   Mixed --------------------
        //      [HttpGet]
        //      [Route("ItemsList/{itemCode}/{Type}")]
        //      public async Task<IActionResult> ItemsList(
        //    [FromRoute] string itemCode,
        //    [FromQuery] int pageNumber = 1,
        //    [FromQuery] int pageSize = 20,
        //    [FromQuery] string customerCode = "",
        //    [FromRoute] string Type = "All"
        //)
        //      {
        //          var ItemsList = new List<object>();

        //          try
        //          {
        //              using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
        //              {
        //                  await connection.OpenAsync();

        //                  string query = @"
        //              SELECT 
        //                  op.Itemcode AS fItemcode,
        //                  op.fParent,
        //                  i.fItemName,
        //                  op.fPiecerate,
        //                  op.fTax,
        //                  op.Gms AS NetWt,
        //                  op.Gross AS GrossWt,
        //                  op.Wastage,
        //                  op.Mc,
        //                  op.StnChrg AS StoneCharges,
        //                  op.fOthers,
        //                  op.McAmount,
        //                  op.fid,
        //                  COALESCE(op.FImage1, op.FImage2, op.FImage3, op.FImage4) AS fimage,
        //                  op.FImage1,
        //                  op.FImage2,
        //                  op.FImage3,
        //                  op.FImage4,
        //                  d.fRate AS GoldRate,
        //                  op.fDate,
        //                  CASE WHEN w.fProductCode IS NOT NULL THEN 'Y' ELSE 'N' END AS IsWishlist
        //              FROM ITEMPURCHASEOP op
        //              JOIN item i ON i.fItemcode = op.Itemcode
        //              LEFT JOIN Division d ON d.FCODE = op.fDiv
        //              LEFT JOIN Wishlist w ON i.fItemcode = w.fProductCode AND w.fCusCode = @customerCode AND w.fid = op.fid 
        //              WHERE op.Itemcode = @itemCode
        //              ORDER BY op.fDate DESC
        //              OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        //                  using (SqlCommand command = new SqlCommand(query, connection))
        //                  {
        //                      int offset = (pageNumber - 1) * pageSize;

        //                      // Add parameters properly
        //                      command.Parameters.AddWithValue("@itemCode", itemCode ?? string.Empty);
        //                      command.Parameters.AddWithValue("@customerCode", customerCode ?? string.Empty);
        //                      command.Parameters.AddWithValue("@Offset", offset);
        //                      command.Parameters.AddWithValue("@PageSize", pageSize);

        //                      using (SqlDataReader reader = await command.ExecuteReaderAsync())
        //                      {
        //                          while (await reader.ReadAsync())
        //                          {
        //                              string piecerateFlag = reader["fPiecerate"]?.ToString();
        //                              decimal netWt = SafeGetDecimal(reader, "NetWt");
        //                              decimal grossWt = SafeGetDecimal(reader, "GrossWt");
        //                              decimal wastage = SafeGetDecimal(reader, "Wastage");
        //                              decimal mc = SafeGetDecimal(reader, "McAmount");
        //                              decimal stoneCharges = SafeGetDecimal(reader, "StoneCharges");
        //                              decimal fOthers = SafeGetDecimal(reader, "fOthers");
        //                              decimal mcAmount = SafeGetDecimal(reader, "McAmount");
        //                              decimal tax = SafeGetDecimal(reader, "fTax");
        //                              decimal goldRate = SafeGetDecimal(reader, "GoldRate");

        //                              decimal totalAmount = 0;

        //                              // Price calculation
        //                              if (piecerateFlag?.ToUpper() == "Y")
        //                              {
        //                                  totalAmount = mcAmount + tax;
        //                              }
        //                              else
        //                              {
        //                                  totalAmount = PriceCalculator.CalculatePrice(
        //                                  null, netWt, wastage, 0, goldRate, mc, fOthers, stoneCharges, tax, goldRate
        //                              ).TotalAmount;
        //                              }

        //                              ItemsList.Add(new
        //                              {
        //                                  fItemcode = reader["fItemcode"]?.ToString(),
        //                                  fItemName = reader["fItemName"]?.ToString(),
        //                                  fParent = reader["fParent"]?.ToString(),
        //                                  NetWt = netWt,
        //                                  GrossWt = grossWt,
        //                                  Wastage = wastage,
        //                                  fMc = mc,
        //                                  StoneCharges = stoneCharges,
        //                                  fOthers = fOthers,
        //                                  McAmount = mcAmount,
        //                                  fTax = tax,
        //                                  GoldRate = goldRate,
        //                                  TotalAmount = totalAmount,
        //                                  fimage = reader["fimage"]?.ToString() ?? string.Empty,
        //                                  fimage1 = reader["FImage1"]?.ToString(),
        //                                  fimage2 = reader["FImage2"]?.ToString(),
        //                                  fimage3 = reader["FImage3"]?.ToString(),
        //                                  fimage4 = reader["FImage4"]?.ToString(),
        //                                  IsWishlist = reader["IsWishlist"]?.ToString() ?? "N",
        //                                  fID = reader["fid"]?.ToString()
        //                              });
        //                          }
        //                      }
        //                  }
        //              }

        //              return Ok(new { items = ItemsList });
        //          }
        //          catch (SqlException sqlEx)
        //          {
        //              return StatusCode(500, new { error = "Database error occurred.", details = sqlEx.Message });
        //          }
        //          catch (Exception ex)
        //          {
        //              return StatusCode(500, new { error = "Unexpected error occurred.", details = ex.Message });
        //          }
        //      }

        [HttpGet]
        [Route("ItemsList/{itemCode}/{Type}")]
        public async Task<IActionResult> ItemsList(
    [FromRoute] string itemCode,
    [FromRoute] string Type = "All",
    [FromQuery] int pageNumber = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string customerCode = ""
)
        {
            var ItemsList = new List<object>();

            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();


                    string whereCondition = Type.Equals("All", StringComparison.OrdinalIgnoreCase)? "op.fParent = @itemCode": "op.Itemcode = @itemCode";

                    string query = $@"
                SELECT 
                    op.Itemcode AS fItemcode,
                    op.fParent,
                    i.fItemName,
                    op.fPiecerate,
                    op.fTax,
                    op.Gms AS NetWt,
                    op.Gross AS GrossWt,
                    op.Wastage,
                    op.Mc,
                    op.StnChrg AS StoneCharges,
                    op.fOthers,
                    op.McAmount,
                    op.fid,
                    COALESCE(op.FImage1, op.FImage2, op.FImage3, op.FImage4) AS fimage,
                    op.FImage1,
                    op.FImage2,
                    op.FImage3,
                    op.FImage4,
                    d.fRate AS GoldRate,
                    op.fDate,
                    CASE 
                        WHEN w.fProductCode IS NOT NULL THEN 'Y' 
                        ELSE 'N' 
                    END AS IsWishlist
                FROM ITEMPURCHASEOP op
                JOIN item i ON i.fItemcode = op.Itemcode
                LEFT JOIN Division d ON d.FCODE = op.fDiv
                LEFT JOIN Wishlist w 
                    ON i.fItemcode = w.fProductCode 
                    AND w.fCusCode = @customerCode 
                    AND w.fid = op.fid
                WHERE {whereCondition}
                ORDER BY op.fDate DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            ";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        int offset = (pageNumber - 1) * pageSize;

                        command.Parameters.AddWithValue("@itemCode", itemCode);
                        command.Parameters.AddWithValue("@customerCode", customerCode ?? string.Empty);
                        command.Parameters.AddWithValue("@Offset", offset);
                        command.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string piecerateFlag = reader["fPiecerate"]?.ToString();

                                decimal netWt = SafeGetDecimal(reader, "NetWt");
                                decimal wastage = SafeGetDecimal(reader, "Wastage");
                                decimal mc = SafeGetDecimal(reader, "McAmount");
                                decimal stoneCharges = SafeGetDecimal(reader, "StoneCharges");
                                decimal fOthers = SafeGetDecimal(reader, "fOthers");
                                decimal tax = SafeGetDecimal(reader, "fTax");
                                decimal goldRate = SafeGetDecimal(reader, "GoldRate");

                                decimal totalAmount;

                                if (piecerateFlag?.ToUpper() == "Y")
                                {
                                    totalAmount = mc + tax;
                                }
                                else
                                {
                                    totalAmount = PriceCalculator.CalculatePrice(
                                        null,
                                        netWt,
                                        wastage,
                                        0,
                                        goldRate,
                                        mc,
                                        fOthers,
                                        stoneCharges,
                                        tax,
                                        goldRate
                                    ).TotalAmount;
                                }

                                ItemsList.Add(new
                                {
                                    fItemcode = reader["fItemcode"]?.ToString(),
                                    fItemName = reader["fItemName"]?.ToString(),
                                    fParent = reader["fParent"]?.ToString(),
                                    NetWt = netWt,
                                    GrossWt = SafeGetDecimal(reader, "GrossWt"),
                                    Wastage = wastage,
                                    fMc = mc,
                                    StoneCharges = stoneCharges,
                                    fOthers,
                                    McAmount = mc,
                                    fTax = tax,
                                    GoldRate = goldRate,
                                    TotalAmount = totalAmount,
                                    fimage = reader["fimage"]?.ToString() ?? string.Empty,
                                    fimage1 = reader["FImage1"]?.ToString(),
                                    fimage2 = reader["FImage2"]?.ToString(),
                                    fimage3 = reader["FImage3"]?.ToString(),
                                    fimage4 = reader["FImage4"]?.ToString(),
                                    IsWishlist = reader["IsWishlist"]?.ToString() ?? "N",
                                    fID = reader["fid"]?.ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(new { items = ItemsList });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }



        private decimal SafeGetDecimal(SqlDataReader reader, string columnName)
        {
            object value = reader[columnName];
            if (value == DBNull.Value || string.IsNullOrWhiteSpace(value.ToString()))
                return 0;

            return Convert.ToDecimal(value);
        }


        [HttpGet("SubCategorys/{parentCode}")]
        public async Task<IActionResult> SubCtegorys([FromRoute] string parentCode ,[FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            var subItemList = new List<SubcategoryItems>();
       
            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    string query = @"
                SELECT 
                    i.fItemcode, 
                    i.fParent,
                    i.fItemName, 
                    i.fimage
                FROM Item11 i
                WHERE i.fAclevel = 3 AND 
                      i.fparent LIKE @parentPrefix
                ORDER BY i.fItemcode
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@parentPrefix", parentCode+"%");
                        cmd.Parameters.AddWithValue("@Offset", (pageNumber - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                subItemList.Add(new SubcategoryItems
                                {
                                    ItemCode = reader["fItemcode"]?.ToString(),
                                    Fparent = reader["fParent"]?.ToString(),
                                    ItemName = reader["fItemName"]?.ToString(),
                                    Fimage = reader["fimage"]?.ToString()
                                });
                            }
                        }
                    }
                }
                subItemList.Insert(0, new SubcategoryItems
                {
                    ItemCode = "",
                    Fparent = parentCode,
                    ItemName = "All",
                    Fimage = "" 
                });
                return Ok(subItemList);
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


        public class SubcategoryItems{
            public string  ItemCode { get; set; }
            public string ItemName { get; set; } = Empty.ToString();
            public string Fparent { get; set; }
            public string Fimage { get; set; }
        }



        //------------------------------------------------ Items Details ------------------------------------
        [HttpGet]
        [Route("itemDetails/{itemCode}")]
        public async Task<IActionResult> itemDetails(
      [FromRoute] string itemCode,
      [FromQuery] int pageNumber = 1,
      [FromQuery] int pageSize = 20,
      [FromQuery] string customerCode = "",
      [FromQuery] string fID = ""
  )
        {
            var ItemsList = new List<object>();

            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT 
                    op.Itemcode AS fItemcode,
                    op.fParent,
                    i.fItemName,
                    op.fPiecerate,
                    op.fTax,
                    op.Gms AS NetWt,
                    op.Gross AS GrossWt,
                    op.Wastage,
                    op.Mc,
                    op.StnChrg AS StoneCharges,
                    op.fOthers,
                    op.McAmount,
                    op.fid,
                    COALESCE(op.FImage1, op.FImage2, op.FImage3, op.FImage4) AS fimage,
                    op.FImage1,
                    op.FImage2,
                    op.FImage3,
                    op.FImage4,
                    d.fRate AS GoldRate,
                    op.fDate,
                     d.fName,
                    CASE WHEN w.fProductCode IS NOT NULL THEN 'Y' ELSE 'N' END AS IsWishlist
                FROM ITEMPURCHASEOP op
                JOIN item i ON i.fItemcode = op.Itemcode
                LEFT JOIN Division d ON d.FCODE = op.fDiv
                LEFT JOIN Wishlist w ON i.fItemcode = w.fProductCode AND w.fCusCode = @customerCode   AND w.fid = op.fid 
                WHERE op.Itemcode = @itemCode
                    AND (@fID = '' OR op.fid = @fID)
                ORDER BY op.fDate DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        int offset = (pageNumber - 1) * pageSize;

                        // Add parameters properly
                        command.Parameters.AddWithValue("@itemCode", itemCode ?? string.Empty);
                        command.Parameters.AddWithValue("@fID", fID ?? string.Empty);
                        command.Parameters.AddWithValue("@customerCode", customerCode ?? string.Empty);
                        command.Parameters.AddWithValue("@Offset", offset);
                        command.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string piecerateFlag = reader["fPiecerate"]?.ToString();
                                decimal netWt = SafeGetDecimal(reader, "NetWt");
                                decimal grossWt = SafeGetDecimal(reader, "GrossWt");
                                decimal wastage = SafeGetDecimal(reader, "Wastage");
                                decimal mc = SafeGetDecimal(reader, "McAmount");
                                decimal stoneCharges = SafeGetDecimal(reader, "StoneCharges");
                                decimal fOthers = SafeGetDecimal(reader, "fOthers");
                                decimal mcAmount = SafeGetDecimal(reader, "McAmount");
                                decimal tax = SafeGetDecimal(reader, "fTax");
                                decimal goldRate = SafeGetDecimal(reader, "GoldRate");

                                decimal totalAmount = 0;
                                decimal taxAmount = 0;
                                // Price calculation
                                if (piecerateFlag?.ToUpper() == "Y")
                                {
                                    totalAmount = mcAmount + tax;  
                                    taxAmount = 0;                
                                }
                                else
                                {
                                    var priceResult = PriceCalculator.CalculatePrice(
                                        piecerateFlag, netWt, wastage, 0, goldRate, mc, fOthers, stoneCharges, tax, goldRate
                                    );

                                    totalAmount = priceResult.TotalAmount;
                                    taxAmount = priceResult.TaxAmount;
                                }

                                ItemsList.Add(new
                                {
                                    fItemcode = reader["fItemcode"]?.ToString(),
                                    fItemName = reader["fItemName"]?.ToString(),
                                    fParent = reader["fParent"]?.ToString(),
                                    NetWt = netWt,
                                    GrossWt = grossWt,
                                    Wastage = wastage,
                                    fMc = mc,
                                    StoneCharges = stoneCharges,
                                    fOthers = fOthers,
                                    McAmount = mcAmount,
                                    fTax = tax,
                                    TaxAmount = taxAmount,
                                    GoldRate = goldRate,
                                    TotalAmount = totalAmount,
                                    fimage = reader["fimage"]?.ToString() ?? string.Empty,
                                    fimage1 = reader["FImage1"]?.ToString(),
                                    fimage2 = reader["FImage2"]?.ToString(),
                                    fimage3 = reader["FImage3"]?.ToString(),
                                    fimage4 = reader["FImage4"]?.ToString(),
                                    IsWishlist = reader["IsWishlist"]?.ToString() ?? "N",
                                    fID = reader["fid"]?.ToString(),
                                    DiavisionName = reader["fName"]?.ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(new { items = ItemsList });
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
        [Route("SearchItems/{parentCode}")]
        public async Task<IActionResult> SearchItems([FromRoute] string parentCode, [FromQuery] string searchText = "", [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
        {
            List<ListAllItem> SearchItems = new List<ListAllItem>();

            try
            {
                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();

                    string query = @"
                SELECT 
                    I.FITEMCODE,
            I.FPARENT,
            I.FITEMNAME, 
            I.FIMAGE, 
            I.fPieceRate, 
            I.fRate,  
            I.Weight, 
            i.NetWt,
            i.fGrossWt,
            i.LessWt,
            i.fVA,
            i.fVAGMS,
            i.fMc,
            i.fOthers,
            i.fTax,
            i.fStoneCharges,
            i.fimage2,
            i.fimage3,
            i.fimage4,
            i.fPieceRate,
            i.fRate,
            d.fRate AS GoldRate,    
            I.NetWt, 
            i.LessWt,
            I.fVA, 
            I.fVAGMS, 
            I.fMc, 
            I.fOthers, 
            I.fStoneCharges, 
            D.fRate AS GoldRate
                FROM Item11 i
                INNER JOIN Division d ON i.fPurity = d.fName
                WHERE i.fAclevel < 0 AND 
                      i.fParent LIKE @fParent AND
                      (
                        i.fItemName LIKE @search OR 
                        i.fItemcode LIKE @search OR 
                        i.fDesignNo LIKE @search
                      )
                ORDER BY i.fItemcode
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        int offset = (pageNumber - 1) * pageSize;
                        command.Parameters.AddWithValue("@search", "%" + searchText + "%");
                        command.Parameters.AddWithValue("@fParent", parentCode + "%");
                        command.Parameters.AddWithValue("@Offset", offset);
                        command.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string pieceRate = reader["fPieceRate"]?.ToString();
                                decimal baseWeight = SafeGetDecimal(reader, "Weight");
                                decimal netWt = SafeGetDecimal(reader, "NetWt");
                                decimal lessWt = SafeGetDecimal(reader, "LessWt");
                                decimal fGrossWt = SafeGetDecimal(reader, "fGrossWt");
                                decimal fVA = SafeGetDecimal(reader, "fVA");
                                decimal fVAGMS = SafeGetDecimal(reader, "fVAGMS");
                                decimal fMc = SafeGetDecimal(reader, "fMc");
                                decimal fStoneCharges = SafeGetDecimal(reader, "fStoneCharges");
                                decimal fTax = SafeGetDecimal(reader, "fTax");
                                decimal fOthers = SafeGetDecimal(reader, "fOthers");
                                decimal goldRate = SafeGetDecimal(reader, "GoldRate");
                                decimal fRate = SafeGetDecimal(reader, "fRate");

                                var result = PriceCalculator.CalculatePrice(pieceRate, netWt, fVA, fVAGMS, fRate, fMc, fOthers, fStoneCharges, fTax, goldRate);
                                decimal totalAmount = result.TotalAmount;

                                SearchItems.Add(new ListAllItem
                                {
                                    ItemCode = reader["fItemcode"]?.ToString() ?? "",
                                    ItemName = reader["fItemName"]?.ToString() ?? "",
                                    Image = reader["fimage"]?.ToString() ?? "",
                                    TotalPrice = totalAmount
                                });
                            }
                        }
                    }
                }

                return Ok(new { items = SearchItems });
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

        //----------------------------------------------Hompage NewArrivals 20 Items -------------------------------------------
        [HttpGet("NewArrivals")]
        public async Task<IActionResult> NewArrivals()
        {
            try
            {
                var items = new List<JewelleryItem>();

                string query = @"
            SELECT TOP 20
                op.Itemcode AS ItemCode,
                op.fParent,
                i.fItemName,
                COALESCE(op.FImage1, op.FImage2, op.FImage3, op.FImage4) AS FinalImage,
                op.fPiecerate,
                op.fTax,
                op.Gms AS NetWt,
                op.Gross AS GrossWt,
                op.Wastage,
                op.Mc,
                op.StnChrg AS StoneCharges,
                op.fOthers,
                op.McAmount,
                d.fRate AS GoldRate,
                op.fDate,op.fid
            FROM ITEMPURCHASEOP op
            JOIN item i ON i.fItemcode = op.Itemcode
            LEFT JOIN Division d ON d.fCode = op.fDiv
            WHERE i.fAclevel < 0
            ORDER BY op.fDate DESC;";

                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();

                    using (SqlCommand command = new SqlCommand(query, connection))
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            decimal totalAmount = 0;

                            // Read fields
                            string piecerateFlag = reader["fPiecerate"]?.ToString();
                            decimal mcAmount = SafeGetDecimal(reader, "McAmount");
                            decimal tax = SafeGetDecimal(reader, "fTax");
                            decimal netWt = SafeGetDecimal(reader, "NetWt");
                            decimal wastage = SafeGetDecimal(reader, "Wastage");
                            decimal mc = SafeGetDecimal(reader, "McAmount");
                            decimal stoneCharges = SafeGetDecimal(reader, "StoneCharges");
                            decimal fOthers = SafeGetDecimal(reader, "fOthers");
                            decimal goldRate = SafeGetDecimal(reader, "GoldRate");

                            // Calculate total price
                            if (piecerateFlag?.ToUpper() == "Y")
                            {
                                totalAmount = mcAmount + tax;
                            }
                            else
                            {
                                totalAmount = PriceCalculator.CalculatePrice(
                                    null, netWt, wastage, 0, goldRate, mc, fOthers, stoneCharges, tax, goldRate
                                ).TotalAmount;
                            }

                            items.Add(new JewelleryItem
                            {
                                ItemCode = reader["ItemCode"].ToString(),
                                fparent = reader["fParent"].ToString(),
                                Name = reader["fItemName"].ToString(),
                                Image = reader["FinalImage"]?.ToString(),
                                fID = reader["fid"]?.ToString(),
                                Price = totalAmount
                            });
                        }
                    }
                }

                return Ok(items);
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




        //-----------------------------------Selected List Items ----------------------------------------




    }
}




public class JewelleryItem
{
    [JsonPropertyName("itemCode")]
    public string ItemCode { get; set; }    
    [JsonPropertyName("fparent")]
    public string fparent { get; set; }
    [JsonPropertyName("itemName")]
    public string Name { get; set; }
    [JsonPropertyName("image")]
    public string Image { get; set; }

    [JsonPropertyName("totalPrice")]
    public decimal Price { get; set; }
    [JsonPropertyName("fID")]
    public string fID { get; set; }
}


public class ListAllItem
{
    [JsonPropertyName("itemCode")]
    public string ItemCode { get; set; }

    [JsonPropertyName("itemName")]
    public string ItemName { get; set; }    
    [JsonPropertyName("fparent")]
    public string fparent { get; set; }

    [JsonPropertyName("image")]
    public string Image { get; set; }

    [JsonPropertyName("totalPrice")]
    public decimal TotalPrice { get; set; }
    
    [JsonPropertyName("isWishlist")]
    public string IsWishlist { get; set; }

}


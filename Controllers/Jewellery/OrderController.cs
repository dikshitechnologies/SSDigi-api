using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace CHITSCHEME.Controllers.Jewellery
{
    
    [Route("api/[controller]")]
    //[Authorize]
    [ApiController]
    public class OrderController : ControllerBase
    {




        [HttpPost("insert-item-transaction")]
        public async Task<IActionResult> PlaceOrderTrans([FromBody] OrderModel order)
        {
            if (order == null || order.Items == null || !order.Items.Any())
                return BadRequest("Invalid order data.");

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using (SqlTransaction tran = con.BeginTransaction())
                    {
                        try
                        {
                            string getMaxVoucherQuery = @"
                             SELECT MAX(fVouchno)
                            FROM BLEDGER
                            WHERE FBILLTYPE = 'OS'";

                            string lastVoucher = null;

                            using (SqlCommand cmd = new SqlCommand(getMaxVoucherQuery, con, tran))
                            {
                                lastVoucher = Convert.ToString(await cmd.ExecuteScalarAsync());
                            }

                            string formattedVoucher;

                            if (string.IsNullOrEmpty(lastVoucher))
                            {
                                // First voucher
                                formattedVoucher = "OS00001";
                            }
                            else
                            {
                                // Extract numeric part only
                                string numberPart = new string(lastVoucher.Where(char.IsDigit).ToArray());

                                int nextNumber = int.Parse(numberPart) + 1;

                                formattedVoucher = "OS" + nextNumber.ToString("D5");
                            }

                            double totalAmount = 0;
                            int totalQuantity = 0;

                            foreach (var item in order.Items)
                            {
                                totalQuantity += item.Quantity;
                                totalAmount += Convert.ToDouble(item.Price) * item.Quantity;

                                // 2. Fetch data from itempurchaseop for given ItemCode + fid
                                string selectQuery = @"
                                SELECT 
                                    ip.Itemcode, 
                                    ip.Qty AS fTotQty,
                                    ip.Gms AS fGms,
                                    ip.Mc AS fMcAmount,
                                    ip.StnChrg AS fStnChrg,
                                    ip.Wastage AS fWastage,
                                    ip.fPrefix, 
                                    ip.fBox, 
                                    ip.Gross AS fGross,
                                    ip.fSize, 
                                    ip.fDiv, 
                                    ip.fDescription AS fdesc,
                                    ip.fDesign, 
                                    ip.fSection, 
                                    ip.fID,
                                    d.fRate
                                FROM itempurchaseop ip
                                JOIN Division d ON d.fCode = ip.fDiv
                                WHERE ip.Itemcode = @ItemCode AND ip.FID = @FID;";

                                DataTable dt = new DataTable();
                                using (SqlCommand cmdSelect = new SqlCommand(selectQuery, con, tran))
                                {
                                    cmdSelect.Parameters.AddWithValue("@ItemCode", item.ItemCode);
                                    cmdSelect.Parameters.AddWithValue("@FID", item.fid);

                                    using (SqlDataAdapter da = new SqlDataAdapter(cmdSelect))
                                    {
                                        da.Fill(dt);
                                    }
                                }

                                if (dt.Rows.Count == 0)
                                    continue; // skip if no match found

                                // 3. Insert into itemtransactionop
                                foreach (DataRow row in dt.Rows)
                                {
                                    string insertQuery = @"
                                INSERT INTO itemtransactionop
                                (FVoucher, FItemcode, FType, fTotQty, fGms, fMcAmount, fStnChrg, fWastage, 
                                    fPrefix, fBox, fGross, fSize, fDiv, fCode, fproductId, fRate,FAMOUNT)
                                VALUES
                                (@FVOUCHER, @FItemcode, @FTYpe, @fTotQty, @FGms, @FMcAmount, @FStnChrg, @FWastage,
                                    @FPrefix, @FBox, @FGross, @FSize, @FDiv, @FCode, @productId, @fRate,@FAMOUNT)";

                                    using (SqlCommand cmdInsert = new SqlCommand(insertQuery, con, tran))
                                    {
                                        cmdInsert.Parameters.AddWithValue("@FVOUCHER", formattedVoucher);
                                        cmdInsert.Parameters.AddWithValue("@FItemcode", row["Itemcode"].ToString());
                                        cmdInsert.Parameters.AddWithValue("@FTYpe", "OS");
                                        cmdInsert.Parameters.AddWithValue("@fTotQty", item.Quantity);
                                        cmdInsert.Parameters.AddWithValue("@FGms", row["fGms"]);
                                        cmdInsert.Parameters.AddWithValue("@FMcAmount", row["fMcAmount"]);
                                        cmdInsert.Parameters.AddWithValue("@FStnChrg", row["fStnChrg"]);
                                        cmdInsert.Parameters.AddWithValue("@FWastage", row["fWastage"]);
                                        cmdInsert.Parameters.AddWithValue("@FPrefix", row["fPrefix"]);
                                        cmdInsert.Parameters.AddWithValue("@FBox", row["fBox"]);
                                        cmdInsert.Parameters.AddWithValue("@FGross", row["fGross"]);
                                        cmdInsert.Parameters.AddWithValue("@FSize", row["fSize"]);
                                        cmdInsert.Parameters.AddWithValue("@FDiv", row["fDiv"]);
                                        cmdInsert.Parameters.AddWithValue("@FCode", item.ItemCode);
                                        cmdInsert.Parameters.AddWithValue("@productId", item.fid);
                                        cmdInsert.Parameters.AddWithValue("@fRate", row["fRate"]);
                                        cmdInsert.Parameters.AddWithValue("@FAMOUNT",item.Price);

                                        await cmdInsert.ExecuteNonQueryAsync();
                                    }
                                }
                            }

                            // 4. Insert summary into BLEDGER
                            string insertBledgerQuery = @"
                            INSERT INTO BLEDGER (fCucode, fvtype, FVOUCHNO, FBILLAMT, FBILLTYPE, FVOUCHDT,FORDERSTATUS,FPAYMENTTYPE)
                            VALUES (@CustomerCode, 'OS', @FVOUCHER, @FBILLAMOUNT, 'OS', GETDATE(),@FORDERSTATUS,@FPAYMENTTYPE)";

                            using (SqlCommand cmdBledger = new SqlCommand(insertBledgerQuery, con, tran))
                            {
                                cmdBledger.Parameters.AddWithValue("@CustomerCode", order.CustomerCode);
                                cmdBledger.Parameters.AddWithValue("@FVOUCHER", formattedVoucher);
                                cmdBledger.Parameters.AddWithValue("@FBILLAMOUNT", totalAmount);
                                cmdBledger.Parameters.AddWithValue("@FORDERSTATUS", 'N');
                                if (order.PaymentMethod.ToUpper() == "COD")
                                {
                                    cmdBledger.Parameters.AddWithValue("@FPAYMENTTYPE", "N");  //N Cash on Delivery
                                }
                                else if (order.PaymentMethod.ToUpper() == "ONLINE")
                                {
                                    cmdBledger.Parameters.AddWithValue("@FPAYMENTTYPE", "Y");  //Y Have online payment
                                }
                                else
                                {
                                    cmdBledger.Parameters.AddWithValue("@FPAYMENTTYPE", "I");  // Default to N
                                }

                                await cmdBledger.ExecuteNonQueryAsync();
                            }
                            string deleteCartQuery = "DELETE FROM cartlist WHERE fCusid = @CustomerCode";
                            using (SqlCommand cmdDelete = new SqlCommand(deleteCartQuery, con, tran))
                            {
                                cmdDelete.Parameters.AddWithValue("@CustomerCode", order.CustomerCode);
                                await cmdDelete.ExecuteNonQueryAsync();
                            }
                            tran.Commit();
                            return Ok(new { Message = "Order placed successfully.", VoucherNo = formattedVoucher });
                        }
                        catch (Exception ex)
                        {
                            tran.Rollback();
                            return StatusCode(500, $"Error placing order: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Database error: {ex.Message}");
            }
        }






        [HttpGet("customerdetails/{voucherNo}")]
        public async Task<IActionResult> GetCustomerDetails(string voucherNo)
        {
            if (string.IsNullOrEmpty(voucherNo))
                return BadRequest("Voucher number is required.");

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    string query = @"
                	SELECT 
                    p.username AS Name,
                    p.Addressline AS Street,
                    p.City AS City,
                    p.STATe AS State,
                    p.PhoneNumber AS Phone,
                    p.email AS Email
                FROM BLEDGER b
                JOIN RegisterUsers p 
                    ON CAST(p.userid AS NVARCHAR(50)) = b.fCucode
                WHERE b.FVOUCHNO = @VoucherNo";
                //    string query = @"
                //	SELECT 
                //    p.facName AS Name,
                //    p.fStreet AS Street,
                //    p.fArea AS Area,
                //    p.fCity AS City,
                //    p.FSTAT AS State,
                //    p.fPhone AS Phone,
                //    p.fMail AS Email
                //FROM BLEDGER b
                //JOIN Party p ON p.fCode = b.fCucode
                //WHERE b.FVOUCHNO = @VoucherNo";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@VoucherNo", voucherNo);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var result = new
                                {
                                    Name = reader["Name"].ToString(),
                                    Street = reader["Street"].ToString(),
                                    //Area = reader["Area"].ToString(),
                                    Area = "",
                                    City = reader["City"].ToString(),
                                    State = reader["State"].ToString(),
                                    Phone = reader["Phone"].ToString(),
                                    Email = reader["Email"]?.ToString()
                                };

                                return Ok(result);
                            }
                            else
                            {
                                return NotFound("No customer found for the given voucher.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }





        [HttpGet("itemdetails/{voucherNo}")]
        public async Task<IActionResult> GetItemDetails(string voucherNo)
        {
            var itemDetails = new List<object>();

            try
            {
                using (var connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    await connection.OpenAsync();

                    var query = @"
                SELECT
                    i.fItemName,
                    ip.fDescription AS descrip,
                    it.fTotQty AS Qty,
                    it.fGms AS Weight,
                    it.FAMOUNT AS Price,
                    it.fRate AS Rate,
                    (it.FAMOUNT * (ip.ftax/100)) AS Tax, 
                     (it.FAMOUNT + (it.FAMOUNT * (ip.ftax/100))) AS Total
                FROM itemtransactionop it
                JOIN itempurchaseop ip ON ip.Itemcode = it.FItemcode AND ip.FID = it.fproductId
                JOIN item i ON i.fItemcode = it.FItemcode
                WHERE it.FVoucher = @VoucherNo";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@VoucherNo", voucherNo);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                itemDetails.Add(new
                                {
                                    ItemName = reader["fItemName"]?.ToString() ?? "",
                                    Description = reader["descrip"]?.ToString() ?? "",
                                    Quantity = reader["Qty"] != DBNull.Value ? Convert.ToDecimal(reader["Qty"]) : 0,
                                    Weight = reader["Weight"] != DBNull.Value ? Convert.ToDecimal(reader["Weight"]) : 0,
                                    Price = reader["Price"] != DBNull.Value ? Convert.ToDecimal(reader["Price"]) : 0,
                                    Rate = reader["Rate"] != DBNull.Value ? Convert.ToDecimal(reader["Rate"]) : 0,
                                    Tax = reader["Tax"] != DBNull.Value ? Convert.ToDecimal(reader["Tax"]) : 0,
                                    Total = reader["Total"] != DBNull.Value ? Convert.ToDecimal(reader["Total"]) : 0
                                });
                            }
                        }
                    }
                }

                return Ok(itemDetails);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
















        //[HttpPost("placeOrder")] 
        //public async Task<IActionResult> PlaceOrder([FromBody] OrderModel order)
        //{
        //    if (order == null || order.Items == null || !order.Items.Any())
        //        return BadRequest("Invalid order data.");




        //    using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
        //    {
        //        await conn.OpenAsync();

        //        SqlTransaction transaction = conn.BeginTransaction();

        //        try
        //        {
        //            // Insert into Orders table
        //            string insertOrderQuery = @"
        //            INSERT INTO Orders 
        //            (CustomerCode, DeliveryAddress, City, State, Pincode, PaymentMethod, OrderDate)
        //            VALUES (@CustomerCode, @DeliveryAddress, @City, @State, @Pincode, @PaymentMethod, GETDATE());
        //            SELECT SCOPE_IDENTITY();";

        //            SqlCommand cmdOrder = new SqlCommand(insertOrderQuery, conn, transaction);
        //            cmdOrder.Parameters.AddWithValue("@CustomerCode", order.CustomerCode);
        //            cmdOrder.Parameters.AddWithValue("@DeliveryAddress", order.DeliveryAddress);
        //            cmdOrder.Parameters.AddWithValue("@City", order.City);
        //            cmdOrder.Parameters.AddWithValue("@State", order.State);
        //            cmdOrder.Parameters.AddWithValue("@Pincode", order.Pincode);
        //            cmdOrder.Parameters.AddWithValue("@PaymentMethod", order.PaymentMethod);

        //            int orderId = Convert.ToInt32(await cmdOrder.ExecuteScalarAsync());

        //            // Insert items into OrderItems table
        //            foreach (var item in order.Items)
        //            {
        //                string insertItemQuery = @"
        //                INSERT INTO OrderItems (OrderID, ItemCode, Quantity, Price)
        //                VALUES (@OrderID, @ItemCode, @Quantity, @Price);";

        //                SqlCommand cmdItem = new SqlCommand(insertItemQuery, conn, transaction);
        //                cmdItem.Parameters.AddWithValue("@OrderID", orderId);
        //                cmdItem.Parameters.AddWithValue("@ItemCode", item.ItemCode);
        //                cmdItem.Parameters.AddWithValue("@Quantity", item.Quantity);
        //                cmdItem.Parameters.AddWithValue("@Price", item.Price);
        //                await cmdItem.ExecuteNonQueryAsync();



        //                string deleteCartQuery = "DELETE FROM cartlist WHERE fCusid = @CustomerCode AND fProductCode = @ItemCode";
        //                SqlCommand cmdDeleteCart = new SqlCommand(deleteCartQuery, conn, transaction);
        //                cmdDeleteCart.Parameters.AddWithValue("@CustomerCode", order.CustomerCode);
        //                cmdDeleteCart.Parameters.AddWithValue("@ItemCode", item.ItemCode);
        //                await cmdDeleteCart.ExecuteNonQueryAsync();
        //            }

        //            transaction.Commit();
        //            return Ok(new { Message = "Order placed successfully", OrderId = orderId });
        //        }
        //        catch (Exception ex)
        //        {
        //            transaction.Rollback();
        //            return StatusCode(500, new { Message = "Order failed", Error = ex.Message });
        //        }
        //    }
        //}


        [HttpGet("GetOrderReport/{customerCode}")]
        public async Task<IActionResult> GetOrderReport(string customerCode)
        {
            var orderReport = new List<object>();

            string query = @"
        SELECT 
            o.OrderID,
            o.CustomerCode,
            o.OrderDate,
            o.DeliveryStatus,
            i.ItemCode,
            item.fItemName,
            item.fImage,
            i.Quantity,
            i.Price,
            (i.Quantity * i.Price) AS TotalPrice
        FROM Orders o
        JOIN OrderItems i ON o.OrderID = i.OrderID
        JOIN Item11 item ON item.fItemcode = i.ItemCode
        WHERE o.CustomerCode = @CustomerCode
        ORDER BY o.OrderDate DESC;";

            using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@CustomerCode", customerCode);
                await conn.OpenAsync();

                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        orderReport.Add(new
                        {
                            OrderID = reader["OrderID"],
                            CustomerCode = reader["CustomerCode"],
                            OrderDate = Convert.ToDateTime(reader["OrderDate"]).ToString("yyyy-MM-dd HH:mm:ss"),
                            OrderStatus = reader["DeliveryStatus"].ToString(),
                            ItemCode = reader["ItemCode"],
                            ItemName = reader["fItemName"],
                            Image = reader["fImage"],
                            Quantity = Convert.ToInt32(reader["Quantity"]),
                            Price = Convert.ToDecimal(reader["Price"]),
                            TotalPrice = Convert.ToDecimal(reader["TotalPrice"])
                        });
                    }
                }
            }

            return Ok(new { orders = orderReport });
        }



    }
}



public class OrderModel
{
    public string CustomerCode { get; set; }

    public string DeliveryAddress { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string Pincode { get; set; }
    public string PaymentMethod { get; set; }
    public List<OrderItemModel> Items { get; set; }
}

public class OrderItemModel
{
    public string ItemCode { get; set; }
    public int Quantity { get; set; }
    public string fid { get; set; }
    public string Price { get; set; }
}

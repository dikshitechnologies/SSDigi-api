using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class OrderReportsController : ControllerBase
    {
    



        [HttpGet("PendingOrders-customer")]
        public async Task<IActionResult> GetCustomerOrdersPending(string customerCode)
        {
            if (string.IsNullOrEmpty(customerCode))
                return BadRequest("Invalid customer code.");

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    string query = @"
                SELECT 
                    B.fVouchno, 
                    I.fItemName,
                    IP.fImage1,
                    IT.fAmount,
                    B.FVOUCHDT,
                    IT.FproductID,
                    IT.fTotQty,
                    B.FPAYMENTTYPE 
                FROM BLEDGER B  
                JOIN ItemTransactionOP IT ON IT.fVoucher = B.fVouchno
                JOIN ItemPurchaseOP IP ON IP.fID = IT.FproductID
                JOIN ITEM I ON I.fItemcode = IT.fItemcode 
                WHERE B.fCucode = @CustomerCode 
                AND B.fvType = 'OS' 
                AND B.FORDERSTATUS = 'N'";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@CustomerCode", customerCode);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            var orders = new List<object>();
                            while (await reader.ReadAsync())
                            {
                                orders.Add(new
                                {
                                    VoucherNo = reader["fVouchno"]?.ToString(),
                                    ItemName = reader["fItemName"]?.ToString(),
                                    ItemImage = reader["fImage1"]?.ToString(),
                                    Amount = reader["fAmount"] != DBNull.Value ? Convert.ToDouble(reader["fAmount"]) : 0.0,
                                    VoucherDate = reader["FVOUCHDT"] != DBNull.Value ? Convert.ToDateTime(reader["FVOUCHDT"]) : (DateTime?)null,
                                    ProductId = reader["FproductID"]?.ToString(),
                                    TotalQty = reader["fTotQty"] != DBNull.Value ? Convert.ToInt32(reader["fTotQty"]) : 0,
                                    orderStatus = "Pending",
                                    paymentType = reader["FPAYMENTTYPE"]?.ToString() == "Y" ? "Online" : "COD"
                                });
                            }

                            return Ok(orders);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving orders: {ex.Message}");
            }
        }

        [HttpGet("DeliveredOrders-customer")]
        public async Task<IActionResult> GetCustomerOrdersDelivered(string customerCode)
        {
            if (string.IsNullOrEmpty(customerCode))
                return BadRequest("Invalid customer code.");

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    string query = @"
                SELECT 
                    B.fVouchno, 
                    I.fItemName,
                    IP.fImage1,
                    IT.fAmount,
                    B.FVOUCHDT,
                    IT.FproductID,
                    IT.fTotQty,
                    B.FPAYMENTTYPE
                FROM BLEDGER B  
                JOIN ItemTransactionOP IT ON IT.fVoucher = B.fVouchno
                JOIN ItemPurchaseOP IP ON IP.fID = IT.FproductID
                JOIN ITEM I ON I.fItemcode = IT.fItemcode 
                WHERE B.fCucode = @CustomerCode 
                AND B.fvType = 'OS' 
                AND B.FORDERSTATUS = 'Y'";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@CustomerCode", customerCode);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            var orders = new List<object>();
                            while (await reader.ReadAsync())
                            {
                                orders.Add(new
                                {
                                    VoucherNo = reader["fVouchno"]?.ToString(),
                                    ItemName = reader["fItemName"]?.ToString(),
                                    ItemImage = reader["fImage1"]?.ToString(),
                                    Amount = reader["fAmount"] != DBNull.Value ? Convert.ToDouble(reader["fAmount"]) : 0.0,
                                    VoucherDate = reader["FVOUCHDT"] != DBNull.Value ? Convert.ToDateTime(reader["FVOUCHDT"]) : (DateTime?)null,
                                    ProductId = reader["FproductID"]?.ToString(),
                                    TotalQty = reader["fTotQty"] != DBNull.Value ? Convert.ToInt32(reader["fTotQty"]) : 0,
                                    orderStatus = "Delivered",
                                    paymentType = reader["FPAYMENTTYPE"]?.ToString() == "Y" ? "Online" : "COD"
                                });
                            }

                            return Ok(orders);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving orders: {ex.Message}");
            }
        }

        [HttpGet("pending-orders-admin")]
        public async Task<IActionResult> GetPendingOrders(
            string? searchTerm,
            DateTime? fromDate,
            DateTime? toDate,
            string? paymentType,   // COD | ONLINE | null
            int pageNumber = 1,
            int pageSize = 50)
        {
            try
            {
                var results = new List<object>();

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

      
                    string query = @"
            
 SELECT 
    P.UserName, 
    p.PhoneNumber,
    B.fVouchno, 
    I.fItemName, 
    IP.fImage1, 
    IT.fAmount, 
    B.FVOUCHDT, 
    IT.FproductID, 
    IT.fTotQty,
    B.FPAYMENTTYPE
FROM 
    BLEDGER B  
JOIN 
    ItemTransactionOP IT ON IT.fVoucher = B.fVouchno
JOIN 
    ItemPurchaseOP IP ON IP.fID = IT.FproductID
JOIN 
    RegisterUsers P ON P.USERID = B.fCucode 
JOIN 
    ITEM I ON I.fItemcode = IT.fItemcode 
WHERE 
    B.fvType = 'OS' 
    AND B.FORDERSTATUS = 'N'

    AND (
        @PaymentType IS NULL OR
        (@PaymentType = 'COD' AND B.FPAYMENTTYPE = 'N') OR
        (@PaymentType = 'ONLINE' AND B.FPAYMENTTYPE = 'Y')
    )

    AND (
        @SearchTerm IS NULL OR
        P.USERNAME LIKE '%' + @SearchTerm + '%' OR
        I.fItemName LIKE '%' + @SearchTerm + '%' OR
        B.fVouchno LIKE '%' + @SearchTerm + '%'
    )

    AND (@FromDate IS NULL OR B.FVOUCHDT >= @FromDate)
    AND (@ToDate IS NULL OR B.FVOUCHDT < DATEADD(DAY, 1, @ToDate))

ORDER BY 
    B.FVOUCHDT DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SearchTerm", (object)searchTerm ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@FromDate", (object)fromDate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToDate", (object)toDate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@PaymentType", (object)paymentType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Offset", (pageNumber - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                results.Add(new
                                {
                                    PartyName = reader["UserName"].ToString(),
                                    fPhone = reader["PhoneNumber"].ToString(),
                                    VoucherNo = reader["fVouchno"].ToString(),
                                    ItemName = reader["fItemName"].ToString(),
                                    Image = reader["fImage1"]?.ToString(),
                                    Amount = reader["fAmount"],
                                    VoucherDate = Convert.ToDateTime(reader["FVOUCHDT"]),
                                    ProductId = reader["FproductID"],
                                    TotalQty = reader["fTotQty"],
                                    PaymentType = reader["FPAYMENTTYPE"]?.ToString() == "Y" ? "Online" : "COD"
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Data = results
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("Delivered-orders-admin")]
        public async Task<IActionResult> GetDeliveredOrders(
            string? searchTerm,
            DateTime? fromDate,
            DateTime? toDate,
            string? paymentType,   // COD | ONLINE | null
            int pageNumber = 1,
            int pageSize = 50)
        {
            try
            {
                var results = new List<object>();

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    string query = @"
           


 SELECT 
     P.UserName, 
     p.PhoneNumber,
     B.fVouchno, 
     I.fItemName, 
     IP.fImage1, 
     IT.fAmount, 
     B.FVOUCHDT, 
     IT.FproductID, 
     IT.fTotQty,
     B.FPAYMENTTYPE
 FROM 
     BLEDGER B  
 JOIN 
     ItemTransactionOP IT ON IT.fVoucher = B.fVouchno
 JOIN 
     ItemPurchaseOP IP ON IP.fID = IT.FproductID
 JOIN 
     RegisterUsers P ON P.UserID = B.fCucode 
 JOIN 
     ITEM I ON I.fItemcode = IT.fItemcode 
 WHERE 
     B.fvType = 'OS' 
     AND B.FORDERSTATUS = 'Y'

     AND (
         @PaymentType IS NULL OR
         (@PaymentType = 'COD' AND B.FPAYMENTTYPE = 'N') OR
         (@PaymentType = 'ONLINE' AND B.FPAYMENTTYPE = 'Y')
     )

     AND (
         @SearchTerm IS NULL OR
         P.UserName LIKE '%' + @SearchTerm + '%' OR
         I.fItemName LIKE '%' + @SearchTerm + '%' OR
         B.fVouchno LIKE '%' + @SearchTerm + '%'
     )

     AND (@FromDate IS NULL OR B.FVOUCHDT >= @FromDate)
     AND (@ToDate IS NULL OR B.FVOUCHDT < DATEADD(DAY, 1, @ToDate))

 ORDER BY 
     B.FVOUCHDT DESC
 OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@SearchTerm", (object)searchTerm ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@FromDate", (object)fromDate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToDate", (object)toDate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@PaymentType", (object)paymentType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Offset", (pageNumber - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                results.Add(new
                                {
                                    PartyName = reader["UserName"].ToString(),
                                    fPhone = reader["PhoneNumber"].ToString(),
                                    VoucherNo = reader["fVouchno"].ToString(),
                                    ItemName = reader["fItemName"].ToString(),
                                    Image = reader["fImage1"]?.ToString(),
                                    Amount = reader["fAmount"],
                                    VoucherDate = Convert.ToDateTime(reader["FVOUCHDT"]),
                                    ProductId = reader["FproductID"],
                                    TotalQty = reader["fTotQty"],
                                    PaymentType = reader["FPAYMENTTYPE"]?.ToString() == "Y" ? "Online" : "COD"
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Data = results
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        [HttpPut("MarkAsDelivered/{vouchNo}")]
        public async Task<IActionResult> MarkOrderAsDelivered([FromRoute] string vouchNo)
        {


            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    var query = @"
                    UPDATE BLEDGER 
                    SET FORDERSTATUS = 'Y'
                    WHERE fVouchno = @vouchNo ";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@vouchNo", vouchNo);

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            return Ok(new { success = true, message = "Order marked as Delivered." });
                        }
                        else
                        {
                            return NotFound(new { success = false, message = "Order not found or already Delivered." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }




        [HttpGet("OrderItems/{voucherNo}")]
        public async Task<IActionResult> GetOrderItemsByVoucherNo([FromRoute] string voucherNo)
        {
            var result = new List<OrderItemDetailDto>();

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    // Step 1: Check delivery status
                    string statusQuery = "SELECT FORDERSTATUS FROM BLEDGER WHERE fVouchno = @voucherNo";
                    using (SqlCommand statusCmd = new SqlCommand(statusQuery, con))
                    {
                        statusCmd.Parameters.AddWithValue("@voucherNo", voucherNo);
                        var statusResult = await statusCmd.ExecuteScalarAsync();

                        if (statusResult == null)
                        {
                            return NotFound(new { message = "Order not found." });
                        }

                        string deliveryStatus = statusResult.ToString();

                        if (deliveryStatus == "Y")
                        {
                            return BadRequest(new { message = $"Order is already delivered. Current status: {deliveryStatus}" });
                        }
                    }

                    // Step 2: Fetch order items from ItemTransactionOP, ITEM, and ItemPurchaseOP
                    string itemQuery = @"
                SELECT 
                    IT.fItemcode,
                    I.fItemName,
                    IT.fTotQty,
                    IT.fAmount,
                    IP.fImage1
                FROM ItemTransactionOP IT
                JOIN ITEM I ON I.fItemcode = IT.fItemcode
                JOIN ItemPurchaseOP IP ON IT.FproductID = IP.fID
                WHERE IT.fVoucher = @voucherNo";

                    using (SqlCommand cmd = new SqlCommand(itemQuery, con))
                    {
                        cmd.Parameters.AddWithValue("@voucherNo", voucherNo);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                result.Add(new OrderItemDetailDto
                                {
                                    ItemCode = reader["fItemcode"].ToString(),
                                    FItemName = reader["fItemName"].ToString(),
                                    Quantity = Convert.ToInt32(reader["fTotQty"]),
                                    Price = Convert.ToDecimal(reader["fAmount"]),
                                    IMAGE = reader["fImage1"] != DBNull.Value ? reader["fImage1"].ToString() : null
                                });
                            }
                        }
                    }
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }

        [HttpGet("E-catelogPaymenReport")]
        public async Task<IActionResult> GetPayments(
       int pageNumber = 1,
       int pageSize = 10,
       string search = "",
       DateTime? fromDate = null,
       DateTime? toDate = null,
       string paymentFilter = "") // COD / ONLINE / empty
        {
            var records = new List<dynamic>();

            int totalCount = 0;
            int codCount = 0;
            int onlineCount = 0;
            decimal codTotal = 0;
            decimal onlineTotal = 0;

            using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
            {
                await con.OpenAsync();

                // 🔹 COUNT QUERY
                string countQuery = @"
        SELECT COUNT(*)
  FROM bledger b
  JOIN RegisterUsers p ON p.UserID = b.fCucode
  WHERE b.fvtype = 'OS'
    AND (@fromDate IS NULL OR b.fVouchdt >= @fromDate)
    AND (@toDate IS NULL OR b.fVouchdt <= @toDate)
    AND (@paymentFilter = '' 
         OR (@paymentFilter = 'ONLINE' AND b.fpaymenttype = 'Y')
         OR (@paymentFilter = 'COD' AND (b.fpaymenttype IS NULL OR b.fpaymenttype = '')))
    AND (p.UserName LIKE '%' + @search + '%'
         OR p.PhoneNumber LIKE '%' + @search + '%')";

                using (SqlCommand cmd = new SqlCommand(countQuery, con))
                {
                    cmd.Parameters.AddWithValue("@search", search ?? "");
                    cmd.Parameters.AddWithValue("@fromDate", (object?)fromDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@toDate", (object?)toDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@paymentFilter", paymentFilter ?? "");

                    totalCount = (int)await cmd.ExecuteScalarAsync();
                }

                // 🔹 DATA QUERY
                string dataQuery = @"
SELECT 
    p.userId,
    p.UserName,
    p.PhoneNumber,
    b.fBillAmt,
    b.fVouchdt,
    CASE 
        WHEN b.fpaymenttype = 'Y' THEN 'ONLINE'
        WHEN b.fpaymenttype = 'N' THEN 'COD'
        ELSE 'COD'
    END AS PaymentType
FROM bledger b
JOIN RegisterUsers p ON p.UserID = b.fCucode
WHERE b.fvtype = 'OS'
  AND (@fromDate IS NULL OR b.fVouchdt >= @fromDate)
  AND (@toDate IS NULL OR b.fVouchdt <= @toDate)
  AND (
        @paymentFilter = ''
        OR (@paymentFilter = 'ONLINE' AND b.fpaymenttype = 'Y')
        OR (@paymentFilter = 'COD' AND (
                b.fpaymenttype = 'N'
                OR b.fpaymenttype IS NULL
                OR b.fpaymenttype = ''
           ))
      )
  AND (
        p.UserName LIKE '%' + @search + '%'
        OR p.PhoneNumber LIKE '%' + @search + '%'
      )
ORDER BY b.fVouchdt DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

                using (SqlCommand cmd = new SqlCommand(dataQuery, con))
                {
                    cmd.Parameters.AddWithValue("@search", search ?? "");
                    cmd.Parameters.AddWithValue("@fromDate", (object?)fromDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@toDate", (object?)toDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@paymentFilter", paymentFilter ?? "");
                    cmd.Parameters.AddWithValue("@offset", (pageNumber - 1) * pageSize);
                    cmd.Parameters.AddWithValue("@pageSize", pageSize);

                    using (SqlDataReader dr = await cmd.ExecuteReaderAsync())
                    {
                        while (await dr.ReadAsync())
                        {
                            decimal amt = Convert.ToDecimal(dr["fBillAmt"]);
                            string type = dr["PaymentType"].ToString();

                            if (type == "ONLINE")
                            {
                                onlineCount++;
                                onlineTotal += amt;
                            }
                            else
                            {
                                codCount++;
                                codTotal += amt;
                            }

                            records.Add(new
                            {
                                FCode = dr["userId"].ToString(),
                                Name = dr["UserName"].ToString(),
                                Phone = dr["PhoneNumber"].ToString(),
                                BillAmount = amt,
                                VoucherDate = Convert.ToDateTime(dr["fVouchdt"]),
                                PaymentType = type
                            });
                        }
                    }
                }
            }

            return Ok(new
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalCount,

                Summary = new
                {
                    COD = new { Count = codCount, TotalAmount = codTotal },
                    ONLINE = new { Count = onlineCount, TotalAmount = onlineTotal }
                },

                Data = records
            });
        }


    }
}


public class PendingOrderDto
{
    public int OrderID { get; set; }
    public string CustomerCode { get; set; }
    public string UserName { get; set; }
    public string PhoneNumber { get; set; }
    public string PaymentMethod { get; set; }
    public DateTime OrderDate { get; set; }
    public string DeliveryStatus { get; set; }
}



public class DeliveredOrderSummaryDto
{
    public int OrderID { get; set; }
    public string CustomerCode { get; set; }
    public DateTime OrderDate { get; set; }
    public string DeliveryAddress { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string Pincode { get; set; }
    public string PaymentMethod { get; set; }
    public string DeliveryStatus { get; set; }
    public int TotalItems { get; set; }
    public decimal TotalAmount { get; set; }
}




public class OrderItemDetailDto
{
    public int OrderItemID { get; set; }
    public string ItemCode { get; set; }
    public string FItemName { get; set; }
    public string IMAGE { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}





public class DeliveredItemDto
{
    public string ItemName { get; set; }
    public string ItemCode { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string DeliveryStatus { get; set; }
    public DateTime OrderDate { get; set; }
}

public class DeliveredCustomerSummaryDto
{
    public int OrderID { get; set; }
    public string CustomerCode { get; set; }
    public string UserName { get; set; }
    public DateTime OrderDate { get; set; }
    public string DeliveryAddress { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string Pincode { get; set; }
    public string PaymentMethod { get; set; }
    public string DeliveryStatus { get; set; }
    public int TotalItems { get; set; }
    public decimal TotalAmount { get; set; }
}

public class DeliveredOrderFullDto
{
    public DeliveredCustomerSummaryDto CustomerDetails { get; set; }
    public List<DeliveredItemDto> Items { get; set; }
}

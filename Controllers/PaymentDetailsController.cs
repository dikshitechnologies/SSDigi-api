using System.ComponentModel.DataAnnotations;
using System.Data;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentDetailsController : ControllerBase
    {



        //-----------------------------------------------------------payment  details ------------------------------------

        [HttpPost("paymentDetails")]
        public IActionResult AddPayment([FromBody] PaymentDetailsModel payment)
        {

            if (payment == null)
                return BadRequest(new { Message = "Invalid payment data." });

            // ✅ Proper validation checks (use ==, not =)
            if (string.IsNullOrWhiteSpace(payment.FcusCode) || payment.FcusCode == "string"  || payment.FcusCode == "")
            {
                return BadRequest(new { Message = "Customer code is required to fetch payment details." });
            }

            if (string.IsNullOrWhiteSpace(payment.FchitCode) || payment.FchitCode == "string" || payment.FchitCode == "")
            {
                return BadRequest(new { Message = "Chit code is required to fetch payment details." });
            }

            if (string.IsNullOrWhiteSpace(payment.Voucher) || payment.Voucher == "string" || payment.Voucher == "")
            {
                return BadRequest(new { Message = "Voucher  Number  is required to fetch payment details." });
            }

            if (payment.FAmount ==0 )
            {
                return BadRequest(new { Message = "FAmount is required to fetch payment details." });
            }



            using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
            {
                con.Open();

                // ✅ Step 1: Check if same record already exists
                string checkQuery = @"SELECT COUNT(*) FROM PaymentDetails 
                              WHERE FchitCode = @FchitCode 
                                AND FcusCode = @FcusCode 
                                AND FWeight = @FWeight 
                                AND FAmount = @FAmount";

                using (SqlCommand checkCmd = new SqlCommand(checkQuery, con))
                {
                    checkCmd.Parameters.AddWithValue("@FchitCode", payment.FchitCode);
                    checkCmd.Parameters.AddWithValue("@FcusCode", payment.FcusCode);
                    checkCmd.Parameters.AddWithValue("@FWeight", payment.FWeight);
                    checkCmd.Parameters.AddWithValue("@FAmount", payment.FAmount);

                    int count = Convert.ToInt32(checkCmd.ExecuteScalar());

                    if (count > 0)
                    {
                        return Conflict(new
                        {
                            Message = "Duplicate payment not allowed for same Chit, Customer, Weight, and Amount."
                        });
                    }
                }

                // ✅ Step 2: Insert record (only if no duplicate found)
                string insertQuery = @"INSERT INTO PaymentDetails (FDate, FchitCode, FcusCode, FWeight, FAmount,flag,fvoucher)
                               VALUES (@FDate, @FchitCode, @FcusCode, @FWeight, @FAmount,@flag,@fvoucher)";

                using (SqlCommand cmd = new SqlCommand(insertQuery, con))
                {
                    cmd.Parameters.AddWithValue("@FDate", DateTime.Now);
                    cmd.Parameters.AddWithValue("@FchitCode", payment.FchitCode);
                    cmd.Parameters.AddWithValue("@FcusCode", payment.FcusCode);
                    cmd.Parameters.AddWithValue("@FWeight", payment.FWeight);
                    cmd.Parameters.AddWithValue("@FAmount", payment.FAmount);
                    cmd.Parameters.AddWithValue("@flag", "N");
                    cmd.Parameters.AddWithValue("@fvoucher", payment.Voucher);
                    cmd.ExecuteNonQuery();
                }

                con.Close();
            }

            return Ok(new
            {
                Message = "Payment added successfully",
            });
        }






        //-----------------------------------------------------------admin details ------------------------------------

        [HttpGet("GetPaymentDetails")]
        public IActionResult GetPaymentDetails(
    int pageNumber = 1,
    int pageSize = 10,
    DateTime? fromDate = null,
    DateTime? toDate = null,
    string chitName = null,
    string customerName = null)
        {
            try
            {
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1) pageSize = 10;

                int offset = (pageNumber - 1) * pageSize;

                // ✅ Base query
                string query = @"
            SELECT 
                P.Id,
                p.fvoucher,
                P.FDate,
                P.FchitCode,
                ChitParty.fAcname AS ChitName,
                P.FcusCode,
                CusParty.fAcname AS CustomerName,
                P.FWeight,
                P.FAmount,
                p.flag
            FROM PaymentDetails P
            LEFT JOIN Party AS ChitParty ON ChitParty.fCode = P.FchitCode
            LEFT JOIN Party AS CusParty ON CusParty.fCode = P.FcusCode
            WHERE 1=1  and p.flag='N'
            ";

                // ✅ Add filters dynamically
                if (fromDate.HasValue)
                    query += " AND CAST(P.FDate AS DATE) >= @FromDate";
                if (toDate.HasValue)
                    query += " AND CAST(P.FDate AS DATE) <= @ToDate";
                if (!string.IsNullOrEmpty(chitName))
                    query += " AND ChitParty.fAcname LIKE '%' + @ChitName + '%'";
                if (!string.IsNullOrEmpty(customerName))
                    query += " AND CusParty.fAcname LIKE '%' + @CustomerName + '%'";

                // ✅ Order + Pagination
                query += @"
            ORDER BY P.Id asc
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY;

            -- Count query
            SELECT COUNT(*) AS TotalRecords 
            FROM PaymentDetails P
            LEFT JOIN Party AS ChitParty ON ChitParty.fCode = P.FchitCode
            LEFT JOIN Party AS CusParty ON CusParty.fCode = P.FcusCode
            WHERE 1=1
            ";

                // ✅ Duplicate same filters for count
                if (fromDate.HasValue)
                    query += " AND CAST(P.FDate AS DATE) >= @FromDate";
                if (toDate.HasValue)
                    query += " AND CAST(P.FDate AS DATE) <= @ToDate";
                if (!string.IsNullOrEmpty(chitName))
                    query += " AND ChitParty.fAcname LIKE '%' + @ChitName + '%'";
                if (!string.IsNullOrEmpty(customerName))
                    query += " AND CusParty.fAcname LIKE '%' + @CustomerName + '%'";

                DataSet ds = new DataSet();
                int totalRecords = 0;

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@Offset", offset);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);

                        if (fromDate.HasValue)
                            cmd.Parameters.AddWithValue("@FromDate", fromDate.Value);
                        if (toDate.HasValue)
                            cmd.Parameters.AddWithValue("@ToDate", toDate.Value);
                        if (!string.IsNullOrEmpty(chitName))
                            cmd.Parameters.AddWithValue("@ChitName", chitName);
                        if (!string.IsNullOrEmpty(customerName))
                            cmd.Parameters.AddWithValue("@CustomerName", customerName);

                        using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                        {
                            adapter.Fill(ds);
                        }
                    }
                }

                DataTable table = ds.Tables[0];
                if (ds.Tables.Count > 1 && ds.Tables[1].Rows.Count > 0)
                    totalRecords = Convert.ToInt32(ds.Tables[1].Rows[0]["TotalRecords"]);

                var dataList = new List<Dictionary<string, object>>();
                foreach (DataRow row in table.Rows)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (DataColumn col in table.Columns)
                    {
                        dict[col.ColumnName] = row[col];
                    }
                    dataList.Add(dict);
                }

                return Ok(new
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalRecords = totalRecords,
                    TotalPages = (int)Math.Ceiling((double)totalRecords / pageSize),
                    Data = dataList
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = "Error retrieving data", Error = ex.Message });
            }
        }


        //        [HttpGet("GetPaymentDetails")]
        //        public IActionResult GetPaymentDetails(
        //    int pageNumber = 1,
        //    int pageSize = 10,
        //    DateTime? fromDate = null,
        //    DateTime? toDate = null,
        //    string chitName = null,
        //    string customerName = null,
        //    string flag = "N",
        //    string schemeType = "All" // ✅ new parameter: can be "R", "W", or "All"
        //)
        //        {
        //            try
        //            {
        //                if (pageNumber < 1) pageNumber = 1;
        //                if (pageSize < 1) pageSize = 10;

        //                int offset = (pageNumber - 1) * pageSize;

        //                // ✅ Base query
        //                string query = @"
        //        SELECT 
        //            P.Id,
        //            P.FDate,
        //            P.FchitCode,
        //            ChitParty.fAcname AS ChitName,
        //            P.FcusCode,
        //            CusParty.fAcname AS CustomerName,
        //            P.FWeight,
        //            P.FAmount,
        //            P.flag
        //        FROM PaymentDetails P
        //        LEFT JOIN Party AS ChitParty ON ChitParty.fCode = P.FchitCode
        //        LEFT JOIN Party AS CusParty ON CusParty.fCode = P.FcusCode
        //        WHERE 1=1 
        //          AND P.flag = @Flag
        //        ";

        //                // ✅ Apply SchemeType filter dynamically
        //                if (!string.IsNullOrEmpty(schemeType) && schemeType != "All")
        //                    query += " AND ChitParty.FSchemetype = @SchemeType";
        //                else
        //                    query += " AND ChitParty.FSchemetype IN ('R','W')";

        //                // ✅ Optional filters
        //                if (fromDate.HasValue)
        //                    query += " AND CAST(P.FDate AS DATE) >= @FromDate";
        //                if (toDate.HasValue)
        //                    query += " AND CAST(P.FDate AS DATE) <= @ToDate";
        //                if (!string.IsNullOrEmpty(chitName))
        //                    query += " AND ChitParty.fAcname LIKE '%' + @ChitName + '%'";
        //                if (!string.IsNullOrEmpty(customerName))
        //                    query += " AND CusParty.fAcname LIKE '%' + @CustomerName + '%'";

        //                // ✅ Order + Pagination
        //                query += @"
        //        ORDER BY P.Id DESC
        //        OFFSET @Offset ROWS
        //        FETCH NEXT @PageSize ROWS ONLY;

        //        -- Count query
        //        SELECT COUNT(*) AS TotalRecords 
        //        FROM PaymentDetails P
        //        LEFT JOIN Party AS ChitParty ON ChitParty.fCode = P.FchitCode
        //        LEFT JOIN Party AS CusParty ON CusParty.fCode = P.FcusCode
        //        WHERE 1=1 
        //          AND P.flag = @Flag
        //        ";

        //                // ✅ Duplicate SchemeType for count query
        //                if (!string.IsNullOrEmpty(schemeType) && schemeType != "All")
        //                    query += " AND ChitParty.FSchemetype = @SchemeType";
        //                else
        //                    query += " AND ChitParty.FSchemetype IN ('R','W')";

        //                // ✅ Apply optional filters for count
        //                if (fromDate.HasValue)
        //                    query += " AND CAST(P.FDate AS DATE) >= @FromDate";
        //                if (toDate.HasValue)
        //                    query += " AND CAST(P.FDate AS DATE) <= @ToDate";
        //                if (!string.IsNullOrEmpty(chitName))
        //                    query += " AND ChitParty.fAcname LIKE '%' + @ChitName + '%'";
        //                if (!string.IsNullOrEmpty(customerName))
        //                    query += " AND CusParty.fAcname LIKE '%' + @CustomerName + '%'";

        //                DataSet ds = new DataSet();
        //                int totalRecords = 0;

        //                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
        //                {
        //                    using (SqlCommand cmd = new SqlCommand(query, con))
        //                    {
        //                        cmd.Parameters.AddWithValue("@Offset", offset);
        //                        cmd.Parameters.AddWithValue("@PageSize", pageSize);
        //                        cmd.Parameters.AddWithValue("@Flag", flag);

        //                        if (!string.IsNullOrEmpty(schemeType) && schemeType != "All")
        //                            cmd.Parameters.AddWithValue("@SchemeType", schemeType);

        //                        if (fromDate.HasValue)
        //                            cmd.Parameters.AddWithValue("@FromDate", fromDate.Value);
        //                        if (toDate.HasValue)
        //                            cmd.Parameters.AddWithValue("@ToDate", toDate.Value);
        //                        if (!string.IsNullOrEmpty(chitName))
        //                            cmd.Parameters.AddWithValue("@ChitName", chitName);
        //                        if (!string.IsNullOrEmpty(customerName))
        //                            cmd.Parameters.AddWithValue("@CustomerName", customerName);

        //                        using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
        //                        {
        //                            adapter.Fill(ds);
        //                        }
        //                    }
        //                }

        //                DataTable table = ds.Tables[0];
        //                if (ds.Tables.Count > 1 && ds.Tables[1].Rows.Count > 0)
        //                    totalRecords = Convert.ToInt32(ds.Tables[1].Rows[0]["TotalRecords"]);

        //                var dataList = new List<Dictionary<string, object>>();
        //                foreach (DataRow row in table.Rows)
        //                {
        //                    var dict = new Dictionary<string, object>();
        //                    foreach (DataColumn col in table.Columns)
        //                    {
        //                        dict[col.ColumnName] = row[col];
        //                    }
        //                    dataList.Add(dict);
        //                }

        //                return Ok(new
        //                {
        //                    PageNumber = pageNumber,
        //                    PageSize = pageSize,
        //                    TotalRecords = totalRecords,
        //                    TotalPages = (int)Math.Ceiling((double)totalRecords / pageSize),
        //                    FlagFilter = flag,
        //                    SchemeTypeFilter = schemeType,
        //                    Data = dataList
        //                });
        //            }
        //            catch (Exception ex)
        //            {
        //                return BadRequest(new { Message = "Error retrieving data", Error = ex.Message });
        //            }
        //        }








        //-----------------------------------------------------------customer details ------------------------------------





        [HttpGet("GetCustomerPaymentDetails")]
        public IActionResult GetCustomerPaymentDetails(
    [FromQuery] int pageNumber = 1,
    [FromQuery] int pageSize = 10,
    [FromQuery] DateTime? fromDate = null,
    [FromQuery] DateTime? toDate = null,
    [FromQuery][Required] string customercode = null, // ✅ Required parameter
    [FromQuery] string flag = "N"
)
        {
            try
            {
                // ✅ Validate required parameter
                if (string.IsNullOrWhiteSpace(customercode))
                {
                    return BadRequest(new { Message = "Customer code is required to fetch payment details." });
                }

                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1) pageSize = 10;

                int offset = (pageNumber - 1) * pageSize;

                // ✅ Base query
                string query = @"
            SELECT 
                P.Id,
                P.FDate,
                P.FchitCode,
                P.FcusCode,
                CusParty.fAcname AS CustomerName,
                P.FWeight,
                P.FAmount,
                P.flag
            FROM PaymentDetails P
            LEFT JOIN Party AS CusParty ON CusParty.fCode = P.FcusCode
            WHERE 1=1
        ";

                // ✅ Add filters dynamically
                query += " AND P.FcusCode = @FcusCode"; // ← Filter by exact customer code

                if (!string.IsNullOrEmpty(flag))
                    query += " AND P.flag = @Flag";
                if (fromDate.HasValue)
                    query += " AND CAST(P.FDate AS DATE) >= @FromDate";
                if (toDate.HasValue)
                    query += " AND CAST(P.FDate AS DATE) <= @ToDate";

                // ✅ Pagination query
                query += @"
            ORDER BY P.Id DESC
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY;

            -- Count query
            SELECT COUNT(*) AS TotalRecords
            FROM PaymentDetails P
            LEFT JOIN Party AS CusParty ON CusParty.fCode = P.FcusCode
            WHERE P.FcusCode = @FcusCode
        ";

                if (!string.IsNullOrEmpty(flag))
                    query += " AND P.flag = @Flag";
                if (fromDate.HasValue)
                    query += " AND CAST(P.FDate AS DATE) >= @FromDate";
                if (toDate.HasValue)
                    query += " AND CAST(P.FDate AS DATE) <= @ToDate";

                // ✅ Execute query
                DataSet ds = new DataSet();
                int totalRecords = 0;

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@FcusCode", customercode);
                        cmd.Parameters.AddWithValue("@Offset", offset);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);
                        cmd.Parameters.AddWithValue("@Flag", flag);

                        if (fromDate.HasValue)
                            cmd.Parameters.AddWithValue("@FromDate", fromDate.Value);
                        if (toDate.HasValue)
                            cmd.Parameters.AddWithValue("@ToDate", toDate.Value);

                        using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                        {
                            adapter.Fill(ds);
                        }
                    }
                }

                // ✅ Parse results
                DataTable table = ds.Tables[0];
                if (ds.Tables.Count > 1 && ds.Tables[1].Rows.Count > 0)
                    totalRecords = Convert.ToInt32(ds.Tables[1].Rows[0]["TotalRecords"]);

                var dataList = new List<Dictionary<string, object>>();
                foreach (DataRow row in table.Rows)
                {
                    var dict = new Dictionary<string, object>();
                    foreach (DataColumn col in table.Columns)
                        dict[col.ColumnName] = row[col];
                    dataList.Add(dict);
                }

                // ✅ Return formatted response
                return Ok(new
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalRecords = totalRecords,
                    TotalPages = (int)Math.Ceiling((double)totalRecords / pageSize),
                    CustomerCode = customercode,
                    FlagFilter = flag,
                    Data = dataList
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = "Error retrieving customer payment details", Error = ex.Message });
            }
        }


        //-----------------------------------------------------------customer  delete details ------------------------------------

        [HttpDelete("DeletePaymentDetails")]
        public IActionResult DeletePaymentDetails([FromBody] List<string> ids)
        {
            try
            {
                if (ids == null || ids.Count == 0)
                    return BadRequest(new { Message = "No IDs provided." });

                // Build dynamic IN clause safely
                var idParams = string.Join(", ", ids.Select((id, index) => $"@Id{index}"));

                string query = $"DELETE FROM PaymentDetails WHERE fvoucher IN ({idParams})";
                string query1 = $"DELETE FROM Ledger WHERE fvrno IN ({idParams})";
                string query2 = $"DELETE FROM Bledger WHERE fVouchno IN ({idParams})";

                int rowsAffected = 0;

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection())) 
                {
                    con.Open();
                    using (SqlTransaction tran = con.BeginTransaction())
                    {
                        try
                        {
                            foreach (var id in ids)
                            {
                                // 🔹 Step 1: Get fvoucher value for this PaymentDetails record
                                string getVoucherQuery = "SELECT fvoucher FROM PaymentDetails WHERE Id = @Id";
                                string fvoucher = id;

                               

                                if (!string.IsNullOrEmpty(fvoucher))
                                {
                                    // 🔹 Step 2: Delete from ledger
                                    using (SqlCommand ledgerCmd = new SqlCommand("DELETE FROM ledger WHERE fvrno = @fvoucher", con, tran))
                                    {
                                        ledgerCmd.Parameters.AddWithValue("@fvoucher", fvoucher);
                                        rowsAffected +=ledgerCmd.ExecuteNonQuery();
                                    }

                                    // 🔹 Step 3: Delete from bledger
                                    using (SqlCommand bledgerCmd = new SqlCommand("DELETE FROM bledger WHERE fVouchno = @fvoucher", con, tran))
                                    {
                                        bledgerCmd.Parameters.AddWithValue("@fvoucher", fvoucher);
                                        rowsAffected += bledgerCmd.ExecuteNonQuery();
                                    }

                                    // 🔹 Step 4: Delete from PaymentDetails
                                    using (SqlCommand payCmd = new SqlCommand("DELETE FROM PaymentDetails WHERE fvoucher = @fvoucher", con, tran))
                                    {
                                        payCmd.Parameters.AddWithValue("@fvoucher", fvoucher);
                                        rowsAffected += payCmd.ExecuteNonQuery();
                                    }
                                }
                            }

                            tran.Commit();
                        }
                        catch (Exception ex)
                        {
                            tran.Rollback();
                            return BadRequest(new { Message = "Transaction failed.", Error = ex.Message });
                        }
                    }
                }

                if (rowsAffected > 0)
                {
                    return Ok(new { Message = "Payment details and related records deleted successfully." });
                }
                else
                {
                    return NotFound(new { Message = "No matching records found to delete." });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = "Error deleting payment details.", Error = ex.Message });
            }
        }




        //-----------------------------------------------------------update  details ------------------------------------


        [HttpPut("UpdatePaymentDetails")]
        public IActionResult UpdatePaymentDetails([FromBody] List<PaymentDetails> payments)
        {
            try
            {
                if (payments == null || payments.Count == 0)
                    return BadRequest(new { Message = "No payment data provided." });

                int updatedCount = 0;

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    con.Open();

                    foreach (var payment in payments)
                    {
                        if (payment.Id <= 0)
                            continue;

                        string query = @"
                    UPDATE PaymentDetails
                    SET 
                        FDate = ISNULL(@FDate, GETDATE()),
                      
                        Flag = @Flag
                    WHERE Id = @Id;
                ";

                        using (SqlCommand cmd = new SqlCommand(query, con))
                        {
                            cmd.Parameters.AddWithValue("@Id", payment.Id);
                            cmd.Parameters.AddWithValue("@FDate", DateTime.Now );
                            cmd.Parameters.AddWithValue("@Flag", "Y");

                            updatedCount += cmd.ExecuteNonQuery();
                        }
                    }
                }

                if (updatedCount > 0)
                {
                    return Ok(new
                    {
                        Message =" payment  updated successfully.",
                        UpdatedCount = updatedCount
                    });
                }
                else
                {
                    return NotFound(new { Message = "No matching payment records found to update." });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = "Error updating payment details", Error = ex.Message });
            }
        }

        public class PaymentDetails
        {
            public int Id { get; set; }

            //// Nullable — if not provided, backend will use current date
            ////public DateTime? FDate { get; set; }

            //// Chit code (like scheme or plan)
            //public string FchitCode { get; set; }

            //// Customer code (linked to Party table)
            //public string FcusCode { get; set; }

            //// Amount of payment
            //public decimal fAmount { get; set; }

            //// Weight (optional, depending on your business logic)
            //public decimal FWeight { get; set; }
        }


        public class PaymentDetailsModel
        {
            //public DateTime FDate { get; set; }
            public string FchitCode { get; set; }
            public string FcusCode { get; set; }
            public decimal FWeight { get; set; }
            public decimal FAmount { get; set; }
            public string Voucher { get; set; }
        }


    }
}

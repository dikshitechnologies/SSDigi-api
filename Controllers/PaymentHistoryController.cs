using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using JEWELLBISREACT.DBConnection;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentHistoryController : ControllerBase
    {
            // ---------------------- ADMIN REPORT --------------------------
            [HttpGet("adminReport")]
            public async Task<IActionResult> GetPaymentsAdmin([FromQuery] PaymentFilter filter)
            {
                List<PaymentRecordDto> payments = new();

                using SqlConnection conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                //string sql = @"
                //SELECT 
                //    P.fAcname,
                //    PR.RazorpayOrderId,
                //    PR.RazorpayPaymentId,
                //    PR.RazorpaySignature,
                //    PR.Amount,
                //    PR.Status,
                //    PR.CONTACT,
                //    PR.PaymentTime,
                //    PR.VerificationTime,
                //    PR.FpaymentType
                //FROM PaymentRecords PR
                //JOIN party P ON PR.UserId = P.fCode
                //WHERE 1 = 1 ";
                string sql = @"
                 SELECT 
                     P.UserName,
                     PR.RazorpayOrderId,
                     PR.RazorpayPaymentId,
                     PR.RazorpaySignature,
                     PR.Amount,
                     PR.Status,
                     PR.CONTACT,
                     PR.PaymentTime,
                     PR.VerificationTime,
                     PR.FpaymentType
                 FROM PaymentRecords PR
                 JOIN RegisterUsers P ON PR.UserId = P.UserID
                 WHERE 1 = 1  ";

                // --- FPAYMENTTYPE ---
                if (!string.IsNullOrEmpty(filter.FpaymentType))
                    sql += " AND PR.FpaymentType = @FpaymentType ";

                // --- OTHER FILTERS ---
                if (!string.IsNullOrEmpty(filter.Name))
                    sql += " AND P.UserName LIKE @Name ";
                if (!string.IsNullOrEmpty(filter.Status))
                    sql += " AND PR.Status = @Status ";
                if (!string.IsNullOrEmpty(filter.OrderId))
                    sql += " AND PR.RazorpayOrderId LIKE @OrderId ";
                if (filter.Amount.HasValue)
                    sql += " AND PR.Amount = @Amount ";
            // --- DATE FILTERS ---
                if (filter.FromDate.HasValue)
                    sql += " AND PR.PaymentTime >= @FromDate ";

                if (filter.ToDate.HasValue)
                    sql += " AND PR.PaymentTime < DATEADD(DAY, 1, @ToDate) ";

            sql += " ORDER BY PR.Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY ";

                using SqlCommand cmd = new SqlCommand(sql, conn);

                cmd.Parameters.AddWithValue("@Name", $"%{filter.Name}%");
                cmd.Parameters.AddWithValue("@Status", (object?)filter.Status ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@OrderId", $"%{filter.OrderId}%");
                cmd.Parameters.AddWithValue("@Amount", (object?)filter.Amount ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FromDate", (object?)filter.FromDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ToDate", (object?)filter.ToDate ?? DBNull.Value);

                if (!string.IsNullOrEmpty(filter.FpaymentType))
                    cmd.Parameters.AddWithValue("@FpaymentType", filter.FpaymentType);

                cmd.Parameters.AddWithValue("@Offset", (filter.Page - 1) * filter.PageSize);
                cmd.Parameters.AddWithValue("@PageSize", filter.PageSize);

                using SqlDataReader reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    payments.Add(ReadPayment(reader));
                }

                return Ok(payments);
            }

        // ---------------------- CUSTOMER REPORT --------------------------
        [HttpGet("customerHistory")]
        public async Task<IActionResult> GetPaymentsCustomer([FromQuery] PaymentFilterCustomer filter)
        {
            List<PaymentRecordDto> payments = new();

            using SqlConnection conn = new SqlConnection(DBHelper.GetConnection());
            await conn.OpenAsync();

            //string sql = @"
            //SELECT 
            //    P.fAcname,
            //    PR.RazorpayOrderId,
            //    PR.RazorpayPaymentId,
            //    PR.RazorpaySignature,
            //    PR.Amount,
            //    PR.Status,
            //    PR.CONTACT,
            //    PR.PaymentTime,
            //    PR.VerificationTime,
            //    PR.FpaymentType
            //FROM PaymentRecords PR
            //JOIN party P ON PR.UserId = P.fCode
            //WHERE 1 = 1 ";
            string sql = @"
            SELECT 
            P.userName,
            PR.RazorpayOrderId,
            PR.RazorpayPaymentId,
            PR.RazorpaySignature,
            PR.Amount,
            PR.Status,
            PR.CONTACT,
            PR.PaymentTime,
            PR.VerificationTime,
            PR.FpaymentType
        FROM PaymentRecords PR
        JOIN RegisterUsers P ON PR.UserId = P.userId
        WHERE 1 = 1 ";

            // User filter
            if (!string.IsNullOrEmpty(filter.UserId))
                sql += " AND PR.UserId = @UserId ";

            // FPAYMENTTYPE
            if (!string.IsNullOrEmpty(filter.FpaymentType))
                sql += " AND PR.FpaymentType = @FpaymentType ";
            else
                sql += " AND (PR.FpaymentType = 'Y' OR PR.FpaymentType = 'N') ";

            sql += " ORDER BY PR.Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY ";

            using SqlCommand cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@UserId", filter.UserId ?? (object)DBNull.Value);
            if (!string.IsNullOrEmpty(filter.FpaymentType))
                cmd.Parameters.AddWithValue("@FpaymentType", filter.FpaymentType);

            cmd.Parameters.AddWithValue("@Offset", (filter.Page - 1) * filter.PageSize);
            cmd.Parameters.AddWithValue("@PageSize", filter.PageSize);

            using SqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                payments.Add(ReadPayment(reader));
            }

            return Ok(payments);
        }


        // ---------------------- Helper Reader --------------------------
        private PaymentRecordDto ReadPayment(SqlDataReader reader)
        {
            return new PaymentRecordDto
            {
                fAcname = reader["userName"]?.ToString(),
                RazorpayOrderId = reader["RazorpayOrderId"]?.ToString(),
                RazorpayPaymentId = reader["RazorpayPaymentId"]?.ToString(),
                RazorpaySignature = reader["RazorpaySignature"]?.ToString(),
                Amount = reader["Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Amount"]) : 0,
                Status = reader["Status"]?.ToString(),
                CONTACT = reader["CONTACT"]?.ToString(),
                PaymentTime = reader["PaymentTime"] != DBNull.Value ? Convert.ToDateTime(reader["PaymentTime"]) : null,
                VerificationTime = reader["VerificationTime"] != DBNull.Value ? Convert.ToDateTime(reader["VerificationTime"]) : null,
                FpaymentType = reader["FpaymentType"]?.ToString()
            };
        }
    }
}

public class PaymentRecordDto
{
    public string fAcname { get; set; }
    public string RazorpayOrderId { get; set; }
    public string RazorpayPaymentId { get; set; }
    public string RazorpaySignature { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; }
    public string CONTACT { get; set; }
    public DateTime? PaymentTime { get; set; }
    public DateTime? VerificationTime { get; set; }
    public string FpaymentType { get; set; }  // Y/N
}

public class PaymentFilter
{
    public string? Name { get; set; }
    public string? Status { get; set; }
    public string? OrderId { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? FpaymentType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PaymentFilterCustomer
{
    public string? UserId { get; set; }
    public string? FpaymentType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

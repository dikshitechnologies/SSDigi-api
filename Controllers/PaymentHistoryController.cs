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

            string sql = @"
                SELECT 
                    Name,
                    OrderId,
                    TransactionId,
                    ResponseCode,
                    ResponseMessage,
                    Amount,
                    Phone,
                    PaymentDatetime,
                    CreatedAt,
                    PaymentMode,
                    PaymentChannel,
                    UserId
                FROM OmniPaymentRecords
                WHERE 1 = 1 ";

            // --- FPAYMENTTYPE (PaymentMode) ---
            if (!string.IsNullOrEmpty(filter.FpaymentType))
                sql += " AND PaymentMode = @FpaymentType ";

            // --- OTHER FILTERS ---
            if (!string.IsNullOrEmpty(filter.Name))
                sql += " AND Name LIKE @Name ";
            if (!string.IsNullOrEmpty(filter.Status))
                sql += " AND ResponseMessage = @Status ";
            if (!string.IsNullOrEmpty(filter.OrderId))
                sql += " AND OrderId LIKE @OrderId ";
            if (filter.Amount.HasValue)
                sql += " AND Amount = @Amount ";
            if (!string.IsNullOrEmpty(filter.UserId))
                sql += " AND UserId = @UserId ";

            // --- DATE FILTERS ---
            if (filter.FromDate.HasValue)
                sql += " AND PaymentDatetime >= @FromDate ";
            if (filter.ToDate.HasValue)
                sql += " AND PaymentDatetime < DATEADD(DAY, 1, @ToDate) ";

            sql += " ORDER BY Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY ";

            using SqlCommand cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@Name", $"%{filter.Name}%");
            cmd.Parameters.AddWithValue("@Status", (object?)filter.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OrderId", $"%{filter.OrderId}%");
            cmd.Parameters.AddWithValue("@Amount", (object?)filter.Amount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FromDate", (object?)filter.FromDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToDate", (object?)filter.ToDate ?? DBNull.Value);

            if (!string.IsNullOrEmpty(filter.FpaymentType))
                cmd.Parameters.AddWithValue("@FpaymentType", filter.FpaymentType);

            if (!string.IsNullOrEmpty(filter.UserId))
                cmd.Parameters.AddWithValue("@UserId", filter.UserId);

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

            string sql = @"
                SELECT 
                    Name,
                    OrderId,
                    TransactionId,
                    ResponseCode,
                    ResponseMessage,
                    Amount,
                    Phone,
                    PaymentDatetime,
                    CreatedAt,
                    PaymentMode,
                    PaymentChannel,
                    UserId
                FROM OmniPaymentRecords
                WHERE 1 = 1 ";

            // --- USER FILTER (by UserId) ---
            if (!string.IsNullOrEmpty(filter.UserId))
                sql += " AND UserId = @UserId ";

            // --- FPAYMENTTYPE (PaymentMode) ---
            if (!string.IsNullOrEmpty(filter.FpaymentType))
                sql += " AND PaymentMode = @FpaymentType ";

            sql += " ORDER BY Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY ";

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
                fAcname          = reader["Name"]?.ToString(),
                RazorpayOrderId  = reader["OrderId"]?.ToString(),
                RazorpayPaymentId = reader["TransactionId"]?.ToString(),
                RazorpaySignature = reader["ResponseCode"]?.ToString(),
                Amount           = reader["Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Amount"]) : 0,
                Status           = reader["ResponseMessage"]?.ToString(),
                CONTACT          = reader["Phone"]?.ToString(),
                PaymentTime      = reader["PaymentDatetime"] != DBNull.Value ? Convert.ToDateTime(reader["PaymentDatetime"]) : null,
                VerificationTime = reader["CreatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedAt"]) : null,
                FpaymentType     = reader["PaymentMode"]?.ToString(),
                UserId           = reader["UserId"]?.ToString()
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
    public string? UserId { get; set; }
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
    public string? UserId { get; set; }
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

using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController] 
    public class ReportController : ControllerBase
    {
        [HttpGet("GetChitReport")]
        public async Task<IActionResult> GetChitReport(
     DateTime? fromDate = null,
     DateTime? toDate = null,
     string? customerCode = null,
     string? customerName = null,
     int page = 1,
     int pageSize = 10)
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 10;

                int offset = (page - 1) * pageSize;

                List<object> result = new();
                int totalRecords = 0;
                decimal totalWeight = 0;
                decimal totalAmount = 0;

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // Total count query
                    string countQuery = @"
            SELECT COUNT(*)
            FROM BLEDGER B
            LEFT JOIN PARTY P
                ON P.FCODE = B.FCUCODE
            WHERE B.fbilltype='CT'

            AND (
                @FromDate IS NULL
                OR @ToDate IS NULL
                OR B.fVouchdt BETWEEN @FromDate AND @ToDate
            )

            AND (
                @CustomerCode IS NULL
                OR @CustomerCode=''
                OR B.FCUCODE=@CustomerCode
            )

            AND (
                @CustomerName IS NULL
                OR @CustomerName=''
                OR P.FACNAME LIKE '%' + @CustomerName + '%'
            )";
                    using (SqlCommand countCmd = new SqlCommand(countQuery, conn))
                    {
                        countCmd.Parameters.AddWithValue(
                            "@FromDate",
                            fromDate ?? (object)DBNull.Value);

                        countCmd.Parameters.AddWithValue(
                            "@ToDate",
                            toDate ?? (object)DBNull.Value);

                        countCmd.Parameters.AddWithValue(
                            "@CustomerCode",
                            string.IsNullOrWhiteSpace(customerCode)
                                ? DBNull.Value
                                : customerCode);

                        countCmd.Parameters.AddWithValue(
                            "@CustomerName",
                            string.IsNullOrWhiteSpace(customerName)
                                ? DBNull.Value
                                : customerName);

                        totalRecords = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                    }
                    string totalQuery = @"
SELECT
    ISNULL(SUM(B.FWT), 0) AS TotalWeight,
    ISNULL(SUM(B.FBILLAMT), 0) AS TotalAmount
FROM BLEDGER B
LEFT JOIN PARTY P
    ON P.FCODE = B.FCUCODE
WHERE B.fbilltype='CT'

AND (
    @FromDate IS NULL
    OR @ToDate IS NULL
    OR B.fVouchdt BETWEEN @FromDate AND @ToDate
)

AND (
    @CustomerCode IS NULL
    OR @CustomerCode=''
    OR B.FCUCODE=@CustomerCode
)

AND (
    @CustomerName IS NULL
    OR @CustomerName=''
    OR P.FACNAME LIKE '%' + @CustomerName + '%'
)";

                    using (SqlCommand totalCmd = new SqlCommand(totalQuery, conn))
                    {
                        totalCmd.Parameters.AddWithValue(
                            "@FromDate",
                            fromDate ?? (object)DBNull.Value);

                        totalCmd.Parameters.AddWithValue(
                            "@ToDate",
                            toDate ?? (object)DBNull.Value);

                        totalCmd.Parameters.AddWithValue(
                            "@CustomerCode",
                            string.IsNullOrWhiteSpace(customerCode)
                                ? DBNull.Value
                                : customerCode);

                        totalCmd.Parameters.AddWithValue(
                            "@CustomerName",
                            string.IsNullOrWhiteSpace(customerName)
                                ? DBNull.Value
                                : customerName);

                        using SqlDataReader reader = await totalCmd.ExecuteReaderAsync();

                        if (await reader.ReadAsync())
                        {
                            totalWeight = reader["TotalWeight"] == DBNull.Value
                                ? 0
                                : Math.Round(Convert.ToDecimal(reader["TotalWeight"]), 3);

                            totalAmount = reader["TotalAmount"] == DBNull.Value
                                ? 0
                                : Convert.ToDecimal(reader["TotalAmount"]);
                        }
                    }

                    // Data query with pagination
                    string query = @"
            SELECT
                B.fcucode,
                P.FACNAME,
                B.FWT,
                B.FBILLAMT,
                B.FONLINE,
                B.FVOUCHNO,
                B.fVouchdt
            FROM BLEDGER B
            LEFT JOIN PARTY P
                ON P.FCODE = B.FCUCODE
            WHERE B.fbilltype='CT'

            AND (
                @FromDate IS NULL
                OR @ToDate IS NULL
                OR B.fVouchdt BETWEEN @FromDate AND @ToDate
            )

            AND (
                @CustomerCode IS NULL
                OR @CustomerCode=''
                OR B.FCUCODE=@CustomerCode
            )

            AND (
                @CustomerName IS NULL
                OR @CustomerName=''
                OR P.FACNAME LIKE '%' + @CustomerName + '%'
            )

            ORDER BY B.fVouchdt DESC
            OFFSET @offset ROWS
            FETCH NEXT @pageSize ROWS ONLY";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue(
                            "@FromDate",
                            fromDate ?? (object)DBNull.Value);

                        cmd.Parameters.AddWithValue(
                            "@ToDate",
                            toDate ?? (object)DBNull.Value);

                        cmd.Parameters.AddWithValue(
                            "@CustomerCode",
                            string.IsNullOrWhiteSpace(customerCode)
                            ? DBNull.Value
                            : customerCode);

                        cmd.Parameters.AddWithValue(
                            "@CustomerName",
                            string.IsNullOrWhiteSpace(customerName)
                            ? DBNull.Value
                            : customerName);

                        cmd.Parameters.AddWithValue("@offset", offset);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);


                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                result.Add(new
                                {
                                    CustomerCode = reader["fcucode"]?.ToString(),
                                    CustomerName = reader["FACNAME"]?.ToString(),

                                    Weight = decimal.TryParse(
                                        reader["FWT"]?.ToString(),
                                        out decimal wt) ? wt : 0,

                                    BillAmount = decimal.TryParse(
                                        reader["FBILLAMT"]?.ToString(),
                                        out decimal bill) ? bill : 0,
                                    type = reader["FONLINE"]?.ToString(),
                                    VoucherNo = reader["FVOUCHNO"]?.ToString(),
                                    VoucherDate = reader["fVouchdt"]
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    page,
                    pageSize,
                    totalRecords,
                    totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),

                    totalWeight,
                    totalAmount,

                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
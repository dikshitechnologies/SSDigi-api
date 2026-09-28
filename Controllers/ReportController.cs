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
                if (page < 1)
                    page = 1;

                if (pageSize < 1)
                    pageSize = 10;

                int offset = (page - 1) * pageSize;

                List<object> result = new();

                int totalRecords = 0;
                decimal totalWeight = 0;
                decimal totalAmount = 0;

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // ============================================================
                    // 1. TOTAL RECORD COUNT
                    // ============================================================

                    string countQuery = @"
                SELECT COUNT(*)
                FROM BLEDGER B
                LEFT JOIN PARTY P
                    ON P.FCODE = B.FCUCODE

                WHERE B.FBILLTYPE = 'CT'

                AND (
                    @FromDate IS NULL
                    OR @ToDate IS NULL
                    OR B.FVOUCHDT BETWEEN @FromDate AND @ToDate
                )

                AND (
                    @CustomerCode IS NULL
                    OR @CustomerCode = ''
                    OR B.FCUCODE = @CustomerCode
                )

                AND (
                    @CustomerName IS NULL
                    OR @CustomerName = ''
                    OR P.FACNAME LIKE '%' + @CustomerName + '%'
                )";

                    using (SqlCommand countCmd = new SqlCommand(countQuery, conn))
                    {
                        countCmd.Parameters.AddWithValue(
                            "@FromDate",
                            fromDate ?? (object)DBNull.Value
                        );

                        countCmd.Parameters.AddWithValue(
                            "@ToDate",
                            toDate ?? (object)DBNull.Value
                        );

                        countCmd.Parameters.AddWithValue(
                            "@CustomerCode",
                            string.IsNullOrWhiteSpace(customerCode)
                                ? DBNull.Value
                                : customerCode
                        );

                        countCmd.Parameters.AddWithValue(
                            "@CustomerName",
                            string.IsNullOrWhiteSpace(customerName)
                                ? DBNull.Value
                                : customerName
                        );

                        totalRecords = Convert.ToInt32(
                            await countCmd.ExecuteScalarAsync()
                        );
                    }


                    // ============================================================
                    // 2. TOTAL WEIGHT & TOTAL AMOUNT
                    // ============================================================

                    string totalQuery = @"
                SELECT
                    ISNULL(SUM(B.FWT), 0) AS TotalWeight,
                    ISNULL(SUM(B.FBILLAMT), 0) AS TotalAmount

                FROM BLEDGER B

                LEFT JOIN PARTY P
                    ON P.FCODE = B.FCUCODE

                WHERE B.FBILLTYPE = 'CT'

                AND (
                    @FromDate IS NULL
                    OR @ToDate IS NULL
                    OR B.FVOUCHDT BETWEEN @FromDate AND @ToDate
                )

                AND (
                    @CustomerCode IS NULL
                    OR @CustomerCode = ''
                    OR B.FCUCODE = @CustomerCode
                )

                AND (
                    @CustomerName IS NULL
                    OR @CustomerName = ''
                    OR P.FACNAME LIKE '%' + @CustomerName + '%'
                )";

                    using (SqlCommand totalCmd = new SqlCommand(totalQuery, conn))
                    {
                        totalCmd.Parameters.AddWithValue(
                            "@FromDate",
                            fromDate ?? (object)DBNull.Value
                        );

                        totalCmd.Parameters.AddWithValue(
                            "@ToDate",
                            toDate ?? (object)DBNull.Value
                        );

                        totalCmd.Parameters.AddWithValue(
                            "@CustomerCode",
                            string.IsNullOrWhiteSpace(customerCode)
                                ? DBNull.Value
                                : customerCode
                        );

                        totalCmd.Parameters.AddWithValue(
                            "@CustomerName",
                            string.IsNullOrWhiteSpace(customerName)
                                ? DBNull.Value
                                : customerName
                        );

                        using SqlDataReader reader =
                            await totalCmd.ExecuteReaderAsync();

                        if (await reader.ReadAsync())
                        {
                            totalWeight =
                                reader["TotalWeight"] == DBNull.Value
                                    ? 0
                                    : Math.Round(
                                        Convert.ToDecimal(reader["TotalWeight"]),
                                        3
                                    );

                            totalAmount =
                                reader["TotalAmount"] == DBNull.Value
                                    ? 0
                                    : Convert.ToDecimal(
                                        reader["TotalAmount"]
                                    );
                        }
                    }


                    // ============================================================
                    // 3. PAGINATED DATA
                    // ============================================================

                    string query = @"
                SELECT
                    P.FID AS ID,
                    B.FCUCODE,
                    P.FACNAME,
                    B.FWT,
                    B.FBILLAMT,
                    B.FONLINE,

                    B.FCASH,
                    B.FCARD,
                    B.FUPI,
                    B.FNEFT,

                    B.FVOUCHNO,
                    B.FVOUCHDT,

                    CASE
                        WHEN ISNULL(B.FCASH, 0) > 0
                            THEN 'CASH'

                        WHEN ISNULL(B.FCARD, 0) > 0
                            THEN 'CARD'

                        WHEN ISNULL(B.FUPI, 0) > 0
                            THEN 'UPI'

                        WHEN ISNULL(B.FNEFT, 0) > 0
                            THEN 'NEFT'

                        ELSE 'UNKNOWN'
                    END AS PayMode

                FROM BLEDGER B

                LEFT JOIN PARTY P
                    ON P.FCODE = B.FCUCODE

                WHERE B.FBILLTYPE = 'CT'

                AND (
                    @FromDate IS NULL
                    OR @ToDate IS NULL
                    OR B.FVOUCHDT BETWEEN @FromDate AND @ToDate
                )

                AND (
                    @CustomerCode IS NULL
                    OR @CustomerCode = ''
                    OR B.FCUCODE = @CustomerCode
                )

                AND (
                    @CustomerName IS NULL
                    OR @CustomerName = ''
                    OR P.FACNAME LIKE '%' + @CustomerName + '%'
                )

                ORDER BY B.FVOUCHDT DESC

                OFFSET @offset ROWS
                FETCH NEXT @pageSize ROWS ONLY";


                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        // --------------------------------------------------------
                        // DATE FILTER
                        // --------------------------------------------------------

                        cmd.Parameters.AddWithValue(
                            "@FromDate",
                            fromDate ?? (object)DBNull.Value
                        );

                        cmd.Parameters.AddWithValue(
                            "@ToDate",
                            toDate ?? (object)DBNull.Value
                        );


                        // --------------------------------------------------------
                        // CUSTOMER CODE
                        // --------------------------------------------------------

                        cmd.Parameters.AddWithValue(
                            "@CustomerCode",
                            string.IsNullOrWhiteSpace(customerCode)
                                ? DBNull.Value
                                : customerCode
                        );


                        // --------------------------------------------------------
                        // CUSTOMER NAME
                        // --------------------------------------------------------

                        cmd.Parameters.AddWithValue(
                            "@CustomerName",
                            string.IsNullOrWhiteSpace(customerName)
                                ? DBNull.Value
                                : customerName
                        );


                        // --------------------------------------------------------
                        // PAGINATION
                        // --------------------------------------------------------

                        cmd.Parameters.AddWithValue(
                            "@offset",
                            offset
                        );

                        cmd.Parameters.AddWithValue(
                            "@pageSize",
                            pageSize
                        );


                        using SqlDataReader reader =
                            await cmd.ExecuteReaderAsync();

                        while (await reader.ReadAsync())
                        {
                            // ====================================================
                            // WEIGHT
                            // ====================================================

                            decimal weight = 0;

                            if (reader["FWT"] != DBNull.Value)
                            {
                                decimal.TryParse(
                                    reader["FWT"].ToString(),
                                    out weight
                                );
                            }


                            // ====================================================
                            // BILL AMOUNT
                            // ====================================================

                            decimal billAmount = 0;

                            if (reader["FBILLAMT"] != DBNull.Value)
                            {
                                decimal.TryParse(
                                    reader["FBILLAMT"].ToString(),
                                    out billAmount
                                );
                            }


                            // ====================================================
                            // RESULT
                            // ====================================================

                            result.Add(new
                            {
                                ID = reader["ID"] == DBNull.Value
                                ? ""
                                : reader["ID"].ToString(),
                                CustomerCode =
                                    reader["FCUCODE"] == DBNull.Value
                                        ? ""
                                        : reader["FCUCODE"].ToString(),

                                CustomerName =
                                    reader["FACNAME"] == DBNull.Value
                                        ? ""
                                        : reader["FACNAME"].ToString(),

                                Weight = weight,

                                BillAmount = billAmount,

                                PayMode =
                                    reader["PayMode"] == DBNull.Value
                                        ? "UNKNOWN"
                                        : reader["PayMode"].ToString(),

                                Type =
                                    reader["FONLINE"] == DBNull.Value
                                        ? ""
                                        : reader["FONLINE"].ToString(),

                                VoucherNo =
                                    reader["FVOUCHNO"] == DBNull.Value
                                        ? ""
                                        : reader["FVOUCHNO"].ToString(),

                                VoucherDate =
                                    reader["FVOUCHDT"] == DBNull.Value
                                        ? null
                                        : reader["FVOUCHDT"]
                            });
                        }
                    }
                }


                // ================================================================
                // 4. RESPONSE
                // ================================================================

                return Ok(new
                {
                    page,
                    pageSize,

                    totalRecords,

                    totalPages =
                        (int)Math.Ceiling(
                            totalRecords / (double)pageSize
                        ),

                    totalWeight,

                    totalAmount,

                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    status = false,
                    message = ex.Message
                });
            }
        }
    }
}
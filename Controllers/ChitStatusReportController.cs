using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChitStatusReportController : ControllerBase
    {
        [HttpGet("GetChitStatusReport")]
        public async Task<IActionResult> GetChitStatusReport(
            [FromQuery] string phoneNo,
            [FromQuery] string? schemeCode = null)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();

                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var query = @"
WITH RankedSchemes AS
(
    SELECT
        P.FCODE,
        P.FACNAME,
        P.FPHONE,
        P.FAMOUNT,
        P.FCOMPCODE,
        P.FDATE,
        CASE
        WHEN PARENT.FCODE = '00103'
            THEN DATEADD(MONTH, P.FDUE, P.FDATE)

        WHEN PARENT.FCODE IN ('03026','03247','03248')
            THEN DATEADD(DAY, 330, P.FDATE)

        ELSE P.FDATE
        END AS MaturityDate,
        P.FDUE,
        P.FID AS SCHEMECODE,

        CASE
            WHEN
            (
                P.FSHOW = '1'
                OR
                (
                    P.FSCHEMETYPE NOT IN ('WT','AT','W')
                    AND P.FDIGITYPE NOT IN ('DS','AT','WT')
                )
            )
            THEN 'Active'
            ELSE 'Inactive'
        END AS ActiveStatus,

        ISNULL(L.MaxDue,0) AS PaidDue,

        ISNULL(T.TotalAmount,0)     AS TotalAmount,
        ISNULL(T.TotalWeight,0)     AS TotalWeight,
        ISNULL(T.TotalBenefitAmt,0) AS TotalBenefitAmt,
        ISNULL(T.TotalBenefitWt,0)  AS TotalBenefitWt,

        PARENT.FACNAME AS SCHEMENAME

    FROM PARTY P

    LEFT JOIN
    (
        SELECT
            FID,
            MAX(FDUE) AS MaxDue
        FROM LEDGER
        WHERE FCRDB='CR'
          AND FTYPE='CT'
        GROUP BY FID
    ) L
        ON P.FID=L.FID

    LEFT JOIN
    (
        SELECT
            L.FID,

            SUM(ISNULL(L.FVRAMOUNT,0)) AS TotalAmount,

            SUM(
                CASE
                    WHEN ISNUMERIC(B.FWT)=1
                    THEN CAST(B.FWT AS DECIMAL(18,3))
                    ELSE 0
                END
            ) AS TotalWeight,

            SUM(
                CASE
                    WHEN ISNUMERIC(B.FBAMT)=1
                    THEN CAST(B.FBAMT AS DECIMAL(18,2))
                    ELSE 0
                END
            ) AS TotalBenefitAmt,

            SUM(
                CASE
                    WHEN ISNUMERIC(B.FBWT)=1
                    THEN CAST(B.FBWT AS DECIMAL(18,3))
                    ELSE 0
                END
            ) AS TotalBenefitWt
    
        FROM LEDGER L
        INNER JOIN BLEDGER B
            ON B.FVOUCHNO=L.FVRNO

        WHERE
            L.FCRDB='CR'
            AND L.FTYPE='CT'

        GROUP BY L.FID

    ) T
        ON P.FID=T.FID

    LEFT JOIN PARTY PARENT
        ON PARENT.FPARENT=LEFT(P.FPARENT,LEN(P.FPARENT)-5)

    WHERE
        P.FPHONE=@PhoneNo
        AND P.FPARENT LIKE '0000100044%'
        AND (@SchemeCode IS NULL OR @SchemeCode='' OR P.FID=@SchemeCode)
)

SELECT
    FCODE,
    FACNAME,
    FPHONE,
    FAMOUNT,
    FCOMPCODE,
    FDATE,
    MaturityDate,
    FDUE,
    SCHEMECODE,
    PaidDue,
    TotalAmount,
    TotalWeight,
    TotalBenefitAmt,
    TotalBenefitWt,
    SCHEMENAME,
    ActiveStatus
FROM RankedSchemes
ORDER BY FACNAME;";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@PhoneNo", phoneNo);
                command.Parameters.AddWithValue("@SchemeCode",
                    string.IsNullOrWhiteSpace(schemeCode) ? DBNull.Value : schemeCode);

                using var reader = await command.ExecuteReaderAsync();

                var list = new List<object>();

                while (await reader.ReadAsync())
                {
                    list.Add(new
                    {
                        FCode = reader["FCODE"]?.ToString(),
                        FACNAME = reader["FACNAME"]?.ToString(),
                        FPHONE = reader["FPHONE"]?.ToString(),
                        FAMOUNT = Convert.ToDecimal(reader["FAMOUNT"]),
                        FCOMPCODE = reader["FCOMPCODE"]?.ToString(),
                        FDATE = Convert.ToDateTime(reader["FDATE"]).ToString("yyyy-MM-dd"),
                        MaturityDate = reader["MaturityDate"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(reader["MaturityDate"]).ToString("yyyy-MM-dd"),
                        FDUE = Convert.ToInt32(reader["FDUE"]),
                        SCHEMECODE = reader["SCHEMECODE"]?.ToString(),
                        PaidDue = Convert.ToInt32(reader["PaidDue"]),
                        TotalAmount = Convert.ToDecimal(reader["TotalAmount"]).ToString("0.00"),
                        TotalWeight = Convert.ToDecimal(reader["TotalWeight"]).ToString("0.000"),
                        TotalBenefitAmt = Convert.ToDecimal(reader["TotalBenefitAmt"]).ToString("0.00"),
                        TotalBenefitWt = Convert.ToDecimal(reader["TotalBenefitWt"]).ToString("0.000"),
                        SCHEMENAME = reader["SCHEMENAME"]?.ToString(),
                        ActiveStatus = reader["ActiveStatus"]?.ToString()
                    });
                }

                return Ok(new
                {
                    Success = true,
                    Count = list.Count,
                    Data = list
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }
    }
}
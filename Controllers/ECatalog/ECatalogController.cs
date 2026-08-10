using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.ECatalog
{
    [Route("api/[controller]")]
    [ApiController]
    public class ECatalogController : ControllerBase
    {
        // ===================================================================
        // GET  api/ECatalog/GetBillNo
        // Returns the next OP voucher number e.g. OP000064AA
        // ===================================================================
        [HttpGet("GetBillNo")]
        public IActionResult GetBillNo()
        {
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                con.Open();

                string companyPrefix = Convert.ToString(
                    new SqlCommand("SELECT ISNULL(fBarPrefix,'') FROM Company", con).ExecuteScalar()) ?? "";

                string maxVoucher = Convert.ToString(
                    new SqlCommand("SELECT ISNULL(MAX(Voucher),'') FROM ItemPurchaseOP", con).ExecuteScalar()) ?? "";

                int nextNo = 1;
                if (!string.IsNullOrWhiteSpace(maxVoucher) && maxVoucher.Length >= 8 &&
                    int.TryParse(maxVoucher.Substring(2, 6), out int no))
                    nextNo = no + 1;

                return Ok(new { Status = true, BillNo = $"OP{nextNo:000000}{companyPrefix}" });
            }
            catch (Exception ex) { return BadRequest(new { Status = false, Message = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/GetPrefix
        // Returns the next available 4-letter prefix code (MOTHERLAND encoding)
        // ===================================================================
        [HttpGet("GetPrefix")]
        public IActionResult GetPrefix()
        {
            const string CharSet = "MOTHERLAND";
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                con.Open();

                string barPrefix = Convert.ToString(
                    new SqlCommand("SELECT TOP 1 ISNULL(fBarPrefix,'') FROM Company", con).ExecuteScalar()) ?? "";

                var cmd = new SqlCommand(@"
                    SELECT TOP 1 fPrefix FROM ItemPurchaseOP
                    WHERE fPrefix LIKE @Prefix + '%'
                    ORDER BY fPrefix DESC", con);
                cmd.Parameters.AddWithValue("@Prefix", barPrefix);
                string lastPrefix = Convert.ToString(cmd.ExecuteScalar()) ?? "";

                string nextPrefix;
                if (string.IsNullOrEmpty(lastPrefix))
                {
                    nextPrefix = barPrefix + "MMMM";
                }
                else
                {
                    string code  = lastPrefix.Substring(barPrefix.Length);
                    long   value = Decode(code, CharSet);
                    do
                    {
                        value++;
                        nextPrefix = barPrefix + Encode(value, code.Length, CharSet);
                        var chk = new SqlCommand("SELECT COUNT(*) FROM ItemPurchaseOP WHERE fPrefix=@P", con);
                        chk.Parameters.AddWithValue("@P", nextPrefix);
                        if (Convert.ToInt32(chk.ExecuteScalar()) == 0) break;
                    } while (true);
                }

                return Ok(new { Status = true, Prefix = nextPrefix });
            }
            catch (Exception ex) { return BadRequest(new { Status = false, Message = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/PrefixCheck?prefix=AADMMH&compcode=001
        // Step-1: validates barcode exists in ITEMPURCHASE (stock).
        // Step-2: checks it has NOT already been uploaded to ITEMPURCHASEOP.
        // Returns { Valid, AlreadyUploaded, Message }
        // ===================================================================
        [HttpGet("PrefixCheck")]
        public async Task<IActionResult> PrefixCheck([FromQuery] string prefix, [FromQuery] string compcode = "001")
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return BadRequest(new { message = "Prefix is required." });

            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();

                // 1. Is the prefix in stock (ITEMPURCHASE or ITEMTRANSACTION)?
                const string stockQuery = @"
SELECT TOP 1 1
FROM (
    SELECT P.FPREFIX FROM ITEMPURCHASE P
    JOIN ITEM I ON I.FITEMCODE = P.ITEMCODE
    WHERE P.FCOMPCODE = @comp AND P.FPREFIX = @prefix AND I.fMan = 'N'
    UNION ALL
    SELECT T.FPREFIX FROM ITEMTRANSACTION T
    JOIN ITEM I ON I.FITEMCODE = T.fItemcode
    WHERE T.FCOMPCODE = @comp AND T.FPREFIX = @prefix AND I.fMan = 'N'
) X";

                using var stockCmd = new SqlCommand(stockQuery, con);
                stockCmd.Parameters.AddWithValue("@comp",   compcode);
                stockCmd.Parameters.AddWithValue("@prefix", prefix);
                bool inStock = await stockCmd.ExecuteScalarAsync() != null;

                if (!inStock)
                    return Ok(new { Valid = false, AlreadyUploaded = false, Message = "Invalid Stock – prefix not found." });

                // 2. Already uploaded?
                using var dupCmd = new SqlCommand(
                    "SELECT TOP 1 1 FROM ITEMPURCHASEOP WHERE FPREFIX=@p AND FCOMPCODE=@c", con);
                dupCmd.Parameters.AddWithValue("@p", prefix);
                dupCmd.Parameters.AddWithValue("@c", compcode);
                bool alreadyUploaded = await dupCmd.ExecuteScalarAsync() != null;

                if (alreadyUploaded)
                    return Ok(new { Valid = true, AlreadyUploaded = true, Message = "Barcode already inserted." });

                return Ok(new { Valid = true, AlreadyUploaded = false, Message = "OK" });
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (Exception ex)       { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/FetchByPrefix?prefix=AADMMH
        // Fills all textboxes from ITEMPURCHASE joined tables.
        // Equivalent to FetchToTextBoxes() in WinForms.
        // ===================================================================
        [HttpGet("FetchByPrefix")]
        public async Task<IActionResult> FetchByPrefix([FromQuery] string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return BadRequest(new { message = "Prefix is required." });

            const string query = @"
WITH LatestFbox AS (
    SELECT
        CASE WHEN VOUCHER LIKE 'DC%' THEN 'DC' WHEN VOUCHER LIKE 'KT%' THEN 'KT' END AS Prefix,
        FBOX,
        ROW_NUMBER() OVER (
            PARTITION BY CASE WHEN VOUCHER LIKE 'DC%' THEN 'DC' WHEN VOUCHER LIKE 'KT%' THEN 'KT' END
            ORDER BY VOUCHER DESC) AS rn
    FROM ITEMPURCHASE WHERE FPREFIX = @prefix
)
SELECT TOP 1
    P.Itemcode, I.FITEMNAME AS ItemName,
    P.FHUID, P.FDESIGN, D.FNAME AS DesignName,
    P.FSECTION, S.FNAME AS SectionName,
    P.FSIZE, Z.FSIZE AS SizeName,
    P.FDIV, V.fName AS DivisionName,
    LF.FBOX AS CounterCode, C.FBOX AS BoxName,
    B.FCUCODE AS SupplierCode, A.FACNAME AS SupplierName,
    P.QTY, P.GROSS, P.STNWT, P.Gms,
    P.WASTAGE, P.MC, P.STNCHRG, P.FOTHERS
FROM ITEMPURCHASE P
LEFT JOIN ITEM I     ON I.FITEMCODE = P.ITEMCODE
LEFT JOIN DESIGN D   ON D.FCODE     = P.FDESIGN
LEFT JOIN SECTION S  ON S.fCode     = P.fSection
LEFT JOIN SIZE Z     ON Z.FCODE     = P.FSIZE
LEFT JOIN DIVISION V ON V.FCODE     = P.FDIV
LEFT JOIN BOX C      ON C.FCODE     = P.FBOX
LEFT JOIN BLEDGER B  ON B.FVOUCHNO  = P.Voucher
LEFT JOIN PARTY A    ON A.FCODE     = B.FCUCODE
CROSS JOIN (
    SELECT TOP 1 FBOX FROM LatestFbox WHERE rn = 1
    ORDER BY CASE Prefix WHEN 'DC' THEN 1 WHEN 'KT' THEN 2 END
) LF
WHERE P.FPREFIX = @prefix
  AND (B.FBILLTYPE = 'DC' OR B.FBILLTYPE IS NULL)";

            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@prefix", prefix);
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return NotFound(new { message = "No record found for this prefix." });

                return Ok(new
                {
                    ItemCode     = reader["Itemcode"]?.ToString(),
                    ItemName     = reader["ItemName"]?.ToString(),
                    HUID         = reader["FHUID"]?.ToString(),
                    DesignCode   = reader["FDESIGN"]?.ToString(),
                    DesignName   = reader["DesignName"]?.ToString(),
                    SectionCode  = reader["FSECTION"]?.ToString(),
                    SectionName  = reader["SectionName"]?.ToString(),
                    SizeCode     = reader["FSIZE"]?.ToString(),
                    SizeName     = reader["SizeName"]?.ToString(),
                    DivisionCode = reader["FDIV"]?.ToString(),
                    DivisionName = reader["DivisionName"]?.ToString(),
                    CounterCode  = reader["CounterCode"]?.ToString(),
                    BoxName      = reader["BoxName"]?.ToString(),
                    SupplierCode = reader["SupplierCode"]?.ToString(),
                    SupplierName = reader["SupplierName"]?.ToString(),
                    Qty          = reader["QTY"]  == DBNull.Value ? 0 : Convert.ToDecimal(reader["QTY"]),
                    Gross        = reader["GROSS"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["GROSS"]),
                    StnWt        = reader["STNWT"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["STNWT"]),
                    Gms          = reader["Gms"]   == DBNull.Value ? 0 : Convert.ToDecimal(reader["Gms"]),
                    Wastage      = reader["WASTAGE"]  == DBNull.Value ? 0 : Convert.ToDecimal(reader["WASTAGE"]),
                    MC           = reader["MC"]       == DBNull.Value ? 0 : Convert.ToDecimal(reader["MC"]),
                    StnChrg      = reader["STNCHRG"]  == DBNull.Value ? 0 : Convert.ToDecimal(reader["STNCHRG"]),
                    Others       = reader["FOTHERS"]  == DBNull.Value ? 0 : Convert.ToDecimal(reader["FOTHERS"]),
                });
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (Exception ex)       { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/GetParentInfo?itemcode=00146
        // Returns CParentCode, MetalType (CParent Name), PieceRate, GST.
        // Equivalent to ParentShow() in WinForms.
        // ===================================================================
        [HttpGet("GetParentInfo")]
        public async Task<IActionResult> GetParentInfo([FromQuery] string itemcode)
        {
            if (string.IsNullOrWhiteSpace(itemcode))
                return BadRequest(new { message = "Itemcode is required." });

            const string query = @"
SELECT DISTINCT
    fItemname AS ItemName,
    fItemCode AS ItemCode,
    LEFT(FPARENT, LEN(FPARENT) - 5)  AS SParentCode,
    (SELECT TOP 1 fItemname FROM Item Parent WHERE Parent.fParent = LEFT(Child.FPARENT, LEN(Child.FPARENT) - 5))
        AS SParentName,
    LEFT(FPARENT, LEN(FPARENT) - 10) AS CParentCode,
    (SELECT TOP 1 fItemname FROM Item Parent WHERE Parent.fParent = LEFT(Child.FPARENT, LEN(Child.FPARENT) - 10))
        AS CParentName,
    fShow AS Status, fTax AS Tax, fPiecerate AS PR, Flag AS CFlag
FROM Item AS Child
WHERE fAclevel < '0' AND FITEMCODE = @code
ORDER BY fItemCode DESC";

            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@code", itemcode.Trim());
                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return NotFound(new { message = "No parent info found for item: " + itemcode });

                return Ok(new
                {
                    CParentCode = reader["CParentCode"]?.ToString(),
                    MetalType   = reader["CParentName"]?.ToString(),
                    PieceRate   = reader["PR"]?.ToString(),
                    GST         = reader["Tax"]?.ToString(),
                });
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (Exception ex)       { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/SearchItems?search=chain
        // Typeahead search for item name — replaces SearchItemData() in WinForms.
        // ===================================================================
        [HttpGet("SearchItems")]
        public async Task<IActionResult> SearchItems([FromQuery] string search = "")
        {
            const string query = @"
SELECT DISTINCT fItemname AS ItemName, fItemCode AS ItemCode
FROM Item
WHERE fACLEVEL < '0' AND FITEMCODE > '00101'
  AND fItemname LIKE @search
ORDER BY fItemname";

            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@search", search.Trim() + "%");
                using var reader = await cmd.ExecuteReaderAsync();

                var list = new List<object>();
                while (await reader.ReadAsync())
                    list.Add(new { ItemName = reader["ItemName"]?.ToString(), ItemCode = reader["ItemCode"]?.ToString() });

                return Ok(list);
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (Exception ex)       { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/SearchDesign?search=floral
        // ===================================================================
        [HttpGet("SearchDesign")]
        public async Task<IActionResult> SearchDesign([FromQuery] string search = "")
        {
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT DISTINCT fName AS DesignName, fCode AS DesignCode FROM Design WHERE fName LIKE @s ORDER BY fName", con);
                cmd.Parameters.AddWithValue("@s", search.Trim() + "%");
                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<object>();
                while (await reader.ReadAsync())
                    list.Add(new { DesignName = reader["DesignName"]?.ToString(), DesignCode = reader["DesignCode"]?.ToString() });
                return Ok(list);
            }
            catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/SearchSection?search=necklace
        // ===================================================================
        [HttpGet("SearchSection")]
        public async Task<IActionResult> SearchSection([FromQuery] string search = "")
        {
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT DISTINCT fName AS SectionName, fCode AS SectionCode FROM Section WHERE fName LIKE @s ORDER BY fName", con);
                cmd.Parameters.AddWithValue("@s", search.Trim() + "%");
                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<object>();
                while (await reader.ReadAsync())
                    list.Add(new { SectionName = reader["SectionName"]?.ToString(), SectionCode = reader["SectionCode"]?.ToString() });
                return Ok(list);
            }
            catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/SearchSize?search=6
        // ===================================================================
        [HttpGet("SearchSize")]
        public async Task<IActionResult> SearchSize([FromQuery] string search = "")
        {
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT DISTINCT fSize AS SizeName, fCode AS SizeCode FROM Size WHERE fSize LIKE @s ORDER BY fSize", con);
                cmd.Parameters.AddWithValue("@s", search.Trim() + "%");
                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<object>();
                while (await reader.ReadAsync())
                    list.Add(new { SizeName = reader["SizeName"]?.ToString(), SizeCode = reader["SizeCode"]?.ToString() });
                return Ok(list);
            }
            catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/SearchCounter?search=gold
        // ===================================================================
        [HttpGet("SearchCounter")]
        public async Task<IActionResult> SearchCounter([FromQuery] string search = "")
        {
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT DISTINCT fBox AS BoxName, fCode AS BoxCode FROM Box WHERE fBox LIKE @s ORDER BY fBox", con);
                cmd.Parameters.AddWithValue("@s", search.Trim() + "%");
                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<object>();
                while (await reader.ReadAsync())
                    list.Add(new { BoxName = reader["BoxName"]?.ToString(), BoxCode = reader["BoxCode"]?.ToString() });
                return Ok(list);
            }
            catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/SearchDivision?search=22kt
        // ===================================================================
        [HttpGet("SearchDivision")]
        public async Task<IActionResult> SearchDivision([FromQuery] string search = "")
        {
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(
                    "SELECT DISTINCT fName AS DivisionName, fCode AS DivisionCode FROM Division WHERE fName LIKE @s ORDER BY fName", con);
                cmd.Parameters.AddWithValue("@s", search.Trim() + "%");
                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<object>();
                while (await reader.ReadAsync())
                    list.Add(new { DivisionName = reader["DivisionName"]?.ToString(), DivisionCode = reader["DivisionCode"]?.ToString() });
                return Ok(list);
            }
            catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ECatalog/SearchSupplier?search=kumar
        // ===================================================================
        [HttpGet("SearchSupplier")]
        public async Task<IActionResult> SearchSupplier([FromQuery] string search = "")
        {
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(@"
SELECT DISTINCT fAcName AS SupplierName, fCode AS SupplierCode
FROM Party
WHERE fParent LIKE '%000010000700022%' AND FACLEVEL < 0
  AND fAcName LIKE @s
ORDER BY fAcName", con);
                cmd.Parameters.AddWithValue("@s", search.Trim() + "%");
                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<object>();
                while (await reader.ReadAsync())
                    list.Add(new { SupplierName = reader["SupplierName"]?.ToString(), SupplierCode = reader["SupplierCode"]?.ToString() });
                return Ok(list);
            }
            catch (Exception ex) { return StatusCode(500, new { message = ex.Message }); }
        }

        // ===================================================================
        // Private helpers – MOTHERLAND encoding
        // ===================================================================
        private static long Decode(string text, string chars)
        {
            long value = 0;
            foreach (char c in text)
                value = value * chars.Length + chars.IndexOf(c);
            return value;
        }

        private static string Encode(long value, int length, string chars)
        {
            char[] result = new char[length];
            for (int i = length - 1; i >= 0; i--)
            {
                result[i] = chars[(int)(value % chars.Length)];
                value /= chars.Length;
            }
            return new string(result);
        }
    }
}

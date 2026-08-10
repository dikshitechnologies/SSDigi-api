using System.Data;
using CHITSCHEME.Global;
using JEWELLBISREACT.DBConnection;
using JEWELLBISREACT.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace JEWELLBISREACT.Controllers.ECatalog
{
    [Route("api/[controller]")]
    [ApiController]
    public class ItemCreationController : ControllerBase
    {
        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        private bool ItemGroupNameExists(SqlConnection con, string itemName)
        {
            using (SqlCommand cmd = new SqlCommand(
                "SELECT 1 FROM item WHERE fItemName = @fItemName", con))
            {
                cmd.Parameters.AddWithValue("@fItemName", itemName);
                return cmd.ExecuteScalar() != null;
            }
        }

        private bool DivisionNameExistsForUpdate(
            SqlConnection con, string itemName, string itemCode)
        {
            const string query = @"
                SELECT COUNT(1)
                FROM   item
                WHERE  UPPER(fItemName)  = @fName
                  AND  UPPER(fItemcode) <> @fCode";

            using (SqlCommand cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.Add("@fName", SqlDbType.VarChar).Value = itemName.ToUpper();
                cmd.Parameters.Add("@fCode", SqlDbType.VarChar).Value = itemCode.ToUpper();
                return (int)cmd.ExecuteScalar() > 0;
            }
        }

        // -------------------------------------------------------
        // POST  – Create item
        // -------------------------------------------------------

        [HttpPost("ItemCreationPost")]
        public async Task<IActionResult> ItemGroupCreationPostData(
            [FromBody] ItemCreation item)
        {
            if (item == null)
                return BadRequest(new { message = "ItemCreation data is required." });

            const string queryMaxFCode  = "SELECT ISNULL(MAX(fitemcode), 0) + 1 FROM item";
            const string queryParent    = "SELECT fParent   FROM item WHERE fItemName = @fItemName";
            const string queryFAclevel  = "SELECT fAclevel  FROM item WHERE fParent   = @fParent";
            const string queryBoxCode   = "SELECT fCode     FROM box  WHERE fBox      = @fBox";

            const string queryInsert = @"
                INSERT INTO item (
                    fitemcode, fItemName, fParent,  fAclevel,
                    fNosPerBox, fShort,   fTax,     fShow,
                    fPrefix,    fPieceRate, fCounter, FHSN,
                    fCostPrice, fSellPrice, fReorder, fStones,
                    fVat,       fMan,       FWastage, fMc,
                    fDivision,
                    fTaxPieceRate, Flag, fUnits, fQty, fComp
                ) VALUES (
                    @fitemcode, @fItemName, @fParent,  @fAclevel,
                    @fNosPerBox, @fShort,   @fTax,     @fShow,
                    @fPrefix,   @fPieceRate, @fCounter, @FHSN,
                    @fCostPrice, @fSellPrice, @fReorder, @fStones,
                    @fVat,       @fMan,       @FWastage, @fMc,
                    @fDivision,
                    @fTaxPieceRate, @Flag, @fUnits, @fQty, @fComp
                )";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // 1. Next item code
                    int nextFCode = 1;
                    using (SqlCommand cmd = new SqlCommand(queryMaxFCode, conn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) nextFCode = Convert.ToInt32(result);
                    }
                    string formattedFCode = nextFCode.ToString("D5");

                    // 2. Resolve fParent from GroupName
                    string fParent = null;
                    using (SqlCommand cmd = new SqlCommand(queryParent, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemName", item.GroupName);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) fParent = result.ToString();
                    }
                    if (fParent == null)
                        return NotFound(new { message = "Parent group not found for the given GroupName." });

                    // 3. Resolve fAclevel
                    int? fAclevel = null;
                    using (SqlCommand cmd = new SqlCommand(queryFAclevel, conn))
                    {
                        cmd.Parameters.AddWithValue("@fParent", fParent);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) fAclevel = Convert.ToInt32(result) + 1;
                    }
                    if (fAclevel == null)
                        return NotFound(new { message = "No parent group found for the given GroupName." });

                    // 4. Resolve counter code
                    string fCounter = null;
                    using (SqlCommand cmd = new SqlCommand(queryBoxCode, conn))
                    {
                        cmd.Parameters.AddWithValue("@fBox", item.Counter);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null)
                            fCounter = result.ToString();
                        else
                            return NotFound(new { message = "Box not found for the given Counter value." });
                    }

                    // 5. Duplicate name check
                    if (ItemGroupNameExists(conn, item.FitemName))
                        return Conflict(new { message = "Item name already exists. Please choose a different name." });

                    // 6. Build parent path and level string
                    string concatParent    = fParent + formattedFCode;
                    string faclevelCount   = "-" + (concatParent.Length / 5).ToString();

                    // 7. Insert
                    using (SqlCommand cmd = new SqlCommand(queryInsert, conn))
                    {
                        cmd.Parameters.AddWithValue("@fitemcode",    formattedFCode);
                        cmd.Parameters.AddWithValue("@fItemName",    item.FitemName.ToUpper());
                        cmd.Parameters.AddWithValue("@fParent",      concatParent);
                        cmd.Parameters.AddWithValue("@fAclevel",     faclevelCount);
                        cmd.Parameters.AddWithValue("@fNosPerBox",   "1");
                        cmd.Parameters.AddWithValue("@fShort",       item.ShortName);
                        cmd.Parameters.AddWithValue("@fTax",         item.GstNumber);
                        cmd.Parameters.AddWithValue("@fShow",        "1");
                        cmd.Parameters.AddWithValue("@fPrefix",      item.Prefix ?? "0");
                        cmd.Parameters.AddWithValue("@fPieceRate",   item.pieceRate);
                        cmd.Parameters.AddWithValue("@fCounter",     fCounter);
                        cmd.Parameters.AddWithValue("@FHSN",         item.HsnCode);
                        cmd.Parameters.AddWithValue("@fCostPrice",   "0");
                        cmd.Parameters.AddWithValue("@fSellPrice",   "0");
                        cmd.Parameters.AddWithValue("@fReorder",     "0");
                        cmd.Parameters.AddWithValue("@fStones",      "N");
                        cmd.Parameters.AddWithValue("@fVat",         item.gst);
                        cmd.Parameters.AddWithValue("@fMan",         item.manualprefix);
                        cmd.Parameters.AddWithValue("@FWastage",     item.FWastage);
                        cmd.Parameters.AddWithValue("@fMc",          item.fMc);
                        cmd.Parameters.AddWithValue("@fDivision",    item.fDivision);

                        // 5 new fields
                        cmd.Parameters.AddWithValue("@fTaxPieceRate", item.fTaxPieceRate ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Flag",          item.Flag          ?? "Y");
                        cmd.Parameters.AddWithValue("@fUnits",        item.fUnits        ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@fQty",          item.fQty          ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@fComp",         item.fComp         ?? (object)DBNull.Value);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                            return StatusCode(201, new { message = "Item inserted successfully." });

                        return StatusCode(500, new { message = "Insert failed. No records were affected." });
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error.", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // PUT  – Update item
        // -------------------------------------------------------

        [HttpPut("ItemCreationUpdate")]
        public async Task<IActionResult> ItemGroupCreationUpdateData(
            [FromBody] ItemCreation item)
        {
            if (item == null)
                return BadRequest(new { message = "ItemCreation data is required." });

            const string queryBoxCode  = "SELECT fCode    FROM box  WHERE fBox      = @fBox";
            const string queryParent   = "SELECT fParent  FROM item WHERE fItemName = @fItemName";
            const string queryFAclevel = "SELECT fAclevel FROM item WHERE fParent   = @fParent";

            const string queryUpdate = @"
                UPDATE item SET
                    fItemName     = @fItemName,
                    fParent       = @fParent,
                    fAclevel      = @fAclevel,
                    fShort        = @fShort,
                    fTax          = @fTax,
                    fPrefix       = @fPrefix,
                    fPieceRate    = @fPieceRate,
                    fCounter      = @fCounter,
                    FHSN          = @FHSN,
                    fVat          = @fVat,
                    fMan          = @fMan,
                    fDivision     = @fDivision,
                    FWastage      = @FWastage,
                    fMc           = @fMc,
                    fTaxPieceRate = @fTaxPieceRate,
                    Flag          = @Flag,
                    fUnits        = @fUnits,
                    fQty          = @fQty,
                    fComp         = @fComp
                WHERE fitemcode = @fitemcode";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    if (DivisionNameExistsForUpdate(conn, item.FitemName, item.FitemCode))
                        return Conflict(new { message = "Item name already exists. Please choose a different name." });

                    // Resolve fParent
                    string fParent = null;
                    using (SqlCommand cmd = new SqlCommand(queryParent, conn))
                    {
                        cmd.Parameters.Add("@fItemName", SqlDbType.VarChar).Value = item.GroupName;
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) fParent = result.ToString();
                    }
                    if (fParent == null)
                        return NotFound(new { message = "Parent group not found for the given GroupName." });

                    // Resolve fAclevel
                    int? fAclevel = null;
                    using (SqlCommand cmd = new SqlCommand(queryFAclevel, conn))
                    {
                        cmd.Parameters.Add("@fParent", SqlDbType.VarChar).Value = fParent;
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) fAclevel = Convert.ToInt32(result) + 1;
                    }
                    if (fAclevel == null)
                        return NotFound(new { message = "No parent group found for the given GroupName." });

                    // Resolve counter
                    string fCounter = null;
                    using (SqlCommand cmd = new SqlCommand(queryBoxCode, conn))
                    {
                        cmd.Parameters.AddWithValue("@fBox", item.Counter);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null)
                            fCounter = result.ToString();
                        else
                            return NotFound(new { message = "Box not found for the given Counter value." });
                    }

                    string concatParent  = fParent + item.FitemCode;
                    string faclevelCount = "-" + (concatParent.Length / 5).ToString();

                    using (SqlCommand cmd = new SqlCommand(queryUpdate, conn))
                    {
                        cmd.Parameters.AddWithValue("@fitemcode",    item.FitemCode);
                        cmd.Parameters.AddWithValue("@fItemName",    item.FitemName.ToUpper());
                        cmd.Parameters.AddWithValue("@fParent",      concatParent);
                        cmd.Parameters.AddWithValue("@fAclevel",     faclevelCount);
                        cmd.Parameters.AddWithValue("@fShort",       item.ShortName);
                        cmd.Parameters.AddWithValue("@fTax",         item.GstNumber);
                        cmd.Parameters.AddWithValue("@fPrefix",      item.Prefix ?? "0");
                        cmd.Parameters.AddWithValue("@fPieceRate",   item.pieceRate);
                        cmd.Parameters.AddWithValue("@fCounter",     fCounter);
                        cmd.Parameters.AddWithValue("@FHSN",         item.HsnCode);
                        cmd.Parameters.AddWithValue("@fVat",         item.gst);
                        cmd.Parameters.AddWithValue("@fMan",         item.manualprefix);
                        cmd.Parameters.AddWithValue("@fMc",          item.fMc          ?? "");
                        cmd.Parameters.AddWithValue("@FWastage",     item.FWastage      ?? "");
                        cmd.Parameters.AddWithValue("@fDivision",    item.fDivision     ?? "");

                        // 5 new fields
                        cmd.Parameters.AddWithValue("@fTaxPieceRate", item.fTaxPieceRate ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Flag",          item.Flag          ?? "Y");
                        cmd.Parameters.AddWithValue("@fUnits",        item.fUnits        ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@fQty",          item.fQty          ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@fComp",         item.fComp         ?? (object)DBNull.Value);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                            return Ok(new { message = "Item updated successfully." });

                        return NotFound(new { message = "Item not found for update." });
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error.", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // DELETE  – Delete item
        // -------------------------------------------------------

        [HttpDelete("ItemCreationDelete/{fitemcode}")]
        public async Task<IActionResult> DeleteItem([FromRoute] string fitemcode)
        {
            if (string.IsNullOrWhiteSpace(fitemcode))
                return BadRequest(new { message = "fitemcode is required." });

            var checker = new CheckIfValueExists();
            bool usedInTx       = await checker.DoesValueExist("ITEMTRANSACTION", "FITEMCODE", fitemcode);
            bool usedInPurchase = await checker.DoesValueExist("ITEMPURCHASE",    "ITEMCODE",  fitemcode);

            if (usedInTx || usedInPurchase)
                return Conflict(new { message = "Item is used in related tables and cannot be deleted." });

            const string queryDelete = "DELETE FROM item WHERE fitemcode = @fitemcode";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(queryDelete, conn))
                    {
                        cmd.Parameters.AddWithValue("@fitemcode", fitemcode);
                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                            return Ok(new { message = "Item deleted successfully." });

                        return NotFound(new { message = "Item not found." });
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error.", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET  – Dropdown list with pagination & search
        // GET /api/ItemCreation/GetItemCreationdropdowslist?page=1&pageSize=10&search=chain
        // -------------------------------------------------------

        [HttpGet("GetItemCreationdropdowslist")]
        public async Task<IActionResult> GetledgerCreationdropDowslist(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string search = "")
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            string whereClause = string.IsNullOrWhiteSpace(search)
                ? "WHERE i.fAclevel < 0"
                : "WHERE i.fAclevel < 0 AND UPPER(i.fItemName) LIKE @search";

            string countQuery = $@"
                SELECT COUNT(1)
                FROM   item i
                {whereClause}";

            string dataQuery = $@"
                SELECT i.fItemcode, i.fItemName, i.fParent,
                       i.ftax, i.fPieceRate, i.fShow, i.Flag
                FROM   item i
                {whereClause}
                ORDER  BY i.fItemName ASC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            try
            {
                int total = 0;
                var list  = new List<object>();

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // total count
                    using (SqlCommand cmd = new SqlCommand(countQuery, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.ToUpper() + "%");

                        total = (int)await cmd.ExecuteScalarAsync();
                    }

                    // paged data
                    using (SqlCommand cmd = new SqlCommand(dataQuery, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.ToUpper() + "%");

                        cmd.Parameters.AddWithValue("@offset",   (page - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string fParentCode  = reader["fParent"]?.ToString();

                                string parentName  = string.IsNullOrEmpty(fParentCode)
                                    ? "No Parent"
                                    : await GetParentNameAsync(fParentCode);

                                list.Add(new
                                {
                                    fItemcode     = reader["fItemcode"].ToString(),
                                    fItemName     = reader["fItemName"].ToString(),
                                    fParent       = parentName,
                                    ftax          = reader["ftax"].ToString(),
                                    PieceRate     = reader["fPieceRate"].ToString(),
                                    fShow         = reader["fShow"].ToString(),
                                    Flag          = reader["Flag"].ToString(),
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    totalRecords = total,
                    page,
                    pageSize,
                    totalPages  = (int)Math.Ceiling((double)total / pageSize),
                    data        = list
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Database error", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET – Single item by fitemcode (for edit/view form)
        // GET /api/ItemCreation/GetItem/{fitemcode}
        // -------------------------------------------------------

        [HttpGet("GetItem/{fitemcode}")]
        public async Task<IActionResult> GetItemByCode([FromRoute] string fitemcode)
        {
            if (string.IsNullOrWhiteSpace(fitemcode))
                return BadRequest(new { message = "fitemcode is required." });

            const string query = @"
                SELECT
                    i.fItemcode,
                    i.fItemName,
                    i.ftax          AS GstNumber,
                    i.fPieceRate    AS PieceRate,
                    i.Flag          AS Availability,
                    i.fParent,
                    i.fShow,
                    pg.fItemName    AS ParentGroupName
                FROM item i
                LEFT JOIN item pg
                    ON pg.fParent = LEFT(i.fParent, LEN(i.fParent) - 5)
                WHERE i.fItemcode = @fitemcode";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@fitemcode", fitemcode);
                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return Ok(new
                                {
                                    fItemcode       = reader["fItemcode"].ToString(),
                                    fItemName       = reader["fItemName"].ToString(),
                                    GstNumber       = reader["GstNumber"].ToString(),
                                    PieceRate       = reader["PieceRate"].ToString(),
                                    Availability    = reader["Availability"].ToString(),
                                    ParentGroupName = reader["ParentGroupName"].ToString(),
                                });
                            }
                            return NotFound(new { message = "Item not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // POST – Simplified item create (matches the Item Creation form)
        // Fields: ItemName, GroupName (parent), GstNumber, PieceRate, Availability
        // POST /api/ItemCreation/ItemCreate
        // -------------------------------------------------------

        [HttpPost("ItemCreate")]
        public async Task<IActionResult> ItemCreate([FromBody] ItemCreateSimpleRequest req)
        {
            if (req == null)
                return BadRequest(new { message = "Request body is required." });

            if (string.IsNullOrWhiteSpace(req.ItemName))
                return BadRequest(new { message = "Item Name is required." });

            if (string.IsNullOrWhiteSpace(req.GroupName))
                return BadRequest(new { message = "Item Parent Group is required." });

            const string queryMaxCode  = "SELECT ISNULL(MAX(CAST(fitemcode AS INT)), 0) + 1 FROM item";
            const string queryParent   = "SELECT fParent FROM item WHERE fItemName = @fItemName";

            const string queryInsert = @"
                INSERT INTO item (
                    fitemcode, fItemName, fParent, fAclevel,
                    fNosPerBox, fTax, fShow, fPieceRate,
                    fCostPrice, fSellPrice, fReorder, fStones,
                    fVat, fMan, Flag
                ) VALUES (
                    @fitemcode, @fItemName, @fParent, @fAclevel,
                    @fNosPerBox, @fTax, @fShow, @fPieceRate,
                    @fCostPrice, @fSellPrice, @fReorder, @fStones,
                    @fVat, @fMan, @Flag
                )";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // Duplicate check
                    if (ItemGroupNameExists(conn, req.ItemName))
                        return Conflict(new { message = $"Item '{req.ItemName}' already exists." });

                    // Next item code
                    int nextCode = 1;
                    using (SqlCommand cmd = new SqlCommand(queryMaxCode, conn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) nextCode = Convert.ToInt32(result);
                    }
                    string formattedCode = nextCode.ToString("D5");

                    // Resolve fParent from GroupName
                    string fParent = null;
                    using (SqlCommand cmd = new SqlCommand(queryParent, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemName", req.GroupName);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) fParent = result.ToString();
                    }
                    if (fParent == null)
                        return NotFound(new { message = $"Parent group '{req.GroupName}' not found." });

                    string concatParent  = fParent + formattedCode;
                    string faclevelCount = "-" + (concatParent.Length / 5).ToString();

                    // Normalise piece rate to Y / N
                    string pieceRateDb = (req.PieceRate ?? "NO")
                        .Trim().ToUpper() is "YES" or "Y" ? "Y" : "N";

                    // Availability: 1 = available, 0 = not
                    string availability = (req.Availability ?? "1").Trim();

                    using (SqlCommand cmd = new SqlCommand(queryInsert, conn))
                    {
                        cmd.Parameters.AddWithValue("@fitemcode",  formattedCode);
                        cmd.Parameters.AddWithValue("@fItemName",  req.ItemName.Trim().ToUpper());
                        cmd.Parameters.AddWithValue("@fParent",    concatParent);
                        cmd.Parameters.AddWithValue("@fAclevel",   faclevelCount);
                        cmd.Parameters.AddWithValue("@fNosPerBox", "1");
                        cmd.Parameters.AddWithValue("@fTax",       req.GstNumber?.Trim() ?? "0");
                        cmd.Parameters.AddWithValue("@fShow",      availability);
                        cmd.Parameters.AddWithValue("@fPieceRate", pieceRateDb);
                        cmd.Parameters.AddWithValue("@fCostPrice", "0");
                        cmd.Parameters.AddWithValue("@fSellPrice", "0");
                        cmd.Parameters.AddWithValue("@fReorder",   "0");
                        cmd.Parameters.AddWithValue("@fStones",    "N");
                        cmd.Parameters.AddWithValue("@fVat",       string.IsNullOrWhiteSpace(req.GstNumber) ? "N" : "Y");
                        cmd.Parameters.AddWithValue("@fMan",       "N");
                        cmd.Parameters.AddWithValue("@Flag",       "Y");

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                            return StatusCode(201, new
                            {
                                message   = $"'{req.ItemName}' saved successfully.",
                                fItemcode = formattedCode
                            });

                        return StatusCode(500, new { message = "Insert failed." });
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error.", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // PUT – Simplified item update (matches the Item Creation form)
        // PUT /api/ItemCreation/ItemUpdate/{fitemcode}
        // -------------------------------------------------------

        [HttpPut("ItemUpdate/{fitemcode}")]
        public async Task<IActionResult> ItemUpdate(
            [FromRoute] string fitemcode,
            [FromBody] ItemCreateSimpleRequest req)
        {
            if (string.IsNullOrWhiteSpace(fitemcode))
                return BadRequest(new { message = "fitemcode is required." });

            if (req == null)
                return BadRequest(new { message = "Request body is required." });

            if (string.IsNullOrWhiteSpace(req.ItemName))
                return BadRequest(new { message = "Item Name is required." });

            if (string.IsNullOrWhiteSpace(req.GroupName))
                return BadRequest(new { message = "Item Parent Group is required." });

            const string queryParent = "SELECT fParent FROM item WHERE fItemName = @fItemName";

            const string queryUpdate = @"
                UPDATE item SET
                    fItemName  = @fItemName,
                    fParent    = @fParent,
                    fAclevel   = @fAclevel,
                    fTax       = @fTax,
                    fPieceRate = @fPieceRate,
                    fVat       = @fVat,
                    fShow      = @fShow
                WHERE fitemcode = @fitemcode";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // Duplicate name check (exclude self)
                    if (DivisionNameExistsForUpdate(conn, req.ItemName, fitemcode))
                        return Conflict(new { message = $"Item name '{req.ItemName}' already exists." });

                    // Resolve fParent from GroupName
                    string fParent = null;
                    using (SqlCommand cmd = new SqlCommand(queryParent, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemName", req.GroupName);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) fParent = result.ToString();
                    }
                    if (fParent == null)
                        return NotFound(new { message = $"Parent group '{req.GroupName}' not found." });

                    string concatParent  = fParent + fitemcode;
                    string faclevelCount = "-" + (concatParent.Length / 5).ToString();

                    string pieceRateDb = (req.PieceRate ?? "NO")
                        .Trim().ToUpper() is "YES" or "Y" ? "Y" : "N";

                    string availability = (req.Availability ?? "1").Trim();

                    using (SqlCommand cmd = new SqlCommand(queryUpdate, conn))
                    {
                        cmd.Parameters.AddWithValue("@fitemcode",  fitemcode);
                        cmd.Parameters.AddWithValue("@fItemName",  req.ItemName.Trim().ToUpper());
                        cmd.Parameters.AddWithValue("@fParent",    concatParent);
                        cmd.Parameters.AddWithValue("@fAclevel",   faclevelCount);
                        cmd.Parameters.AddWithValue("@fTax",       req.GstNumber?.Trim() ?? "0");
                        cmd.Parameters.AddWithValue("@fPieceRate", pieceRateDb);
                        cmd.Parameters.AddWithValue("@fVat",       string.IsNullOrWhiteSpace(req.GstNumber) ? "N" : "Y");
                        cmd.Parameters.AddWithValue("@fShow",      availability);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                            return Ok(new { message = $"'{req.ItemName}' updated successfully." });

                        return NotFound(new { message = "Item not found for update." });
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error.", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // DELETE – Delete item by fitemcode (simple form endpoint)
        // DELETE /api/ItemCreation/ItemDelete/{fitemcode}
        // -------------------------------------------------------

        [HttpDelete("ItemDelete/{fitemcode}")]
        public async Task<IActionResult> ItemDelete([FromRoute] string fitemcode)
        {
            if (string.IsNullOrWhiteSpace(fitemcode))
                return BadRequest(new { message = "fitemcode is required." });

            // Check references in related tables
            const string checkQuery = @"
                SELECT 1 FROM WORKORDERITEM  WHERE fItemcode = @fItemCode
                UNION
                SELECT 1 FROM ISSUEITEM      WHERE fItemcode = @fItemCode
                UNION
                SELECT 1 FROM RECIVEITEM     WHERE fItemcode = @fItemCode
                UNION
                SELECT 1 FROM DELIVERYITEM   WHERE fItemcode = @fItemCode";

            const string deleteQuery = "DELETE FROM item WHERE fitemcode = @fitemcode";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // Reference check
                    using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@fItemCode", fitemcode);
                        var refExists = await checkCmd.ExecuteScalarAsync();
                        if (refExists != null)
                            return Conflict(new
                            {
                                message = "This item is referenced in other tables and cannot be deleted."
                            });
                    }

                    // Delete
                    using (SqlCommand deleteCmd = new SqlCommand(deleteQuery, conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@fitemcode", fitemcode);
                        int rows = await deleteCmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                            return Ok(new { message = "Item deleted successfully." });

                        return NotFound(new { message = "Item not found." });
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error.", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET – Parent group list for dropdown (Item Parent Group field)
        // GET /api/ItemCreation/GetParentGroups?search=silver
        // -------------------------------------------------------

        [HttpGet("GetParentGroups")]
        public async Task<IActionResult> GetParentGroups([FromQuery] string search = "")
        {
            string whereClause = string.IsNullOrWhiteSpace(search)
                ? "WHERE fAclevel > 0 AND fAclevel < 3 AND Flag = 'Y'"
                : "WHERE fAclevel > 0 AND fAclevel < 3 AND Flag = 'Y' AND UPPER(fItemName) LIKE @search";

            string query = $@"
                SELECT DISTINCT
                    fItemName  AS GroupName,
                    fItemCode  AS ItemCode,
                    fParent    AS ParentCode,
                    fAclevel   AS LevelCode,
                    fShow      AS Status
                FROM item
                {whereClause}
                ORDER BY fItemName ASC";

            try
            {
                var groups = new List<object>();
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.Trim().ToUpper() + "%");

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                groups.Add(new
                                {
                                    GroupName  = reader["GroupName"].ToString(),
                                    ItemCode   = reader["ItemCode"].ToString(),
                                    ParentCode = reader["ParentCode"].ToString(),
                                    LevelCode  = reader["LevelCode"].ToString(),
                                    Status     = reader["Status"].ToString(),
                                });
                            }
                        }
                    }
                }
                return Ok(groups);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET  – Max prefix
        // -------------------------------------------------------

        [HttpGet("GetMaxPrefix")]
        public async Task<IActionResult> GetMaxPrefix()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();
                    const string query = "SELECT ISNULL(MAX(CAST(fPrefix AS INT)), 0) FROM item";
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        int max = result != null ? Convert.ToInt32(result) : 0;
                        string formatted = (max + 1).ToString("D2");
                        return Ok(new { prefix = formatted });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // Static helpers
        // -------------------------------------------------------

        public static async Task<string> GetParentNameAsync(string fParentCode)
        {
            if (string.IsNullOrEmpty(fParentCode) || fParentCode.Length <= 5)
                return "No Parent";

            string modifiedCode = fParentCode.Substring(0, fParentCode.Length - 5);

            using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
            {
                await conn.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(
                    "SELECT fItemName FROM item WHERE fParent = @fCode", conn))
                {
                    cmd.Parameters.AddWithValue("@fCode", modifiedCode);
                    object result = await cmd.ExecuteScalarAsync();
                    return result != null ? result.ToString() : "No Parent";
                }
            }
        }

        public static async Task<string> GetcountertNameAsync(string counter)
        {
            using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
            {
                await conn.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(
                    "SELECT fBox FROM BOX WHERE fcode = @fcode", conn))
                {
                    cmd.Parameters.AddWithValue("@fcode", counter);
                    object result = await cmd.ExecuteScalarAsync();
                    return result != null ? result.ToString() : "";
                }
            }
        }
    }

    // -------------------------------------------------------
    // Request model – Simplified Item Creation form
    // Matches fields visible in the Item Creation screen:
    //   Item Name, Item Parent Group, GST (%), Piece Rate, Availability
    // -------------------------------------------------------

    public class ItemCreateSimpleRequest
    {
        /// <summary>Item Name — e.g. "Silver Rings"</summary>
        public string ItemName { get; set; }

        /// <summary>Item Parent Group name — e.g. "Silver Ornaments"</summary>
        public string GroupName { get; set; }

        /// <summary>GST percentage — e.g. "3"</summary>
        public string GstNumber { get; set; }

        /// <summary>Piece Rate — "YES" or "NO" (Space Bar toggles in the form)</summary>
        public string PieceRate { get; set; }

        /// <summary>Availability toggle — "1" = available, "0" = not available</summary>
        public string Availability { get; set; }
    }
}

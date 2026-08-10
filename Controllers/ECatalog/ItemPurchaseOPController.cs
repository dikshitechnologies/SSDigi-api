using JEWELLBISREACT.DBConnection;
using JEWELLBISREACT.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.ECatalog
{
    [Route("api/[controller]")]
    [ApiController]
    public class ItemPurchaseOPController : ControllerBase
    {
        private static readonly string[] AllowedExt = { ".jpg", ".jpeg", ".png", ".webp" };

        // ── Save one image to wwwroot/uploads ───────────────────────────────
        private async Task<string?> SaveImageAsync(IFormFile? file, string itemcode, int slot)
        {
            if (file == null || file.Length == 0) return null;

            string ext = Path.GetExtension(file.FileName).ToLower();
            if (!AllowedExt.Contains(ext))
                throw new InvalidOperationException($"Unsupported file type for image{slot}.");
            if (file.Length > 20 * 1024 * 1024)
                throw new InvalidOperationException($"image{slot} exceeds 20 MB limit.");

            string folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            foreach (var e in AllowedExt)
            {
                string old = Path.Combine(folder, $"Item_{itemcode}__{slot}{e}");
                if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
            }

            string fileName = $"Item_{itemcode}__{slot}{ext}";
            using var stream = new FileStream(Path.Combine(folder, fileName), FileMode.Create);
            await file.CopyToAsync(stream);
            return fileName;
        }

        // ── Resolve fCompCode from Company table ────────────────────────────
        private static async Task<string> GetCompCodeAsync()
        {
            using var con = new SqlConnection(DBHelper.GetConnection());
            await con.OpenAsync();
            using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(fCompCode,'001') FROM Company", con);
            var r = await cmd.ExecuteScalarAsync();
            return r?.ToString() ?? "001";
        }

        // ===================================================================
        // POST  api/ItemPurchaseOP/Save
        // Pure INSERT. Images → NULL if not uploaded.
        // Image1-4 fields in the form are IGNORED on POST.
        //
        // Form fields  →  DB column
        // RefNo        →  Voucher
        // Barcode      →  fPrefix
        // ItemCode     →  Itemcode
        // HuidNo       →  FHUID, fCertificate
        // DesignCode   →  fDesign
        // SectionCode  →  fSection
        // SizeCode     →  fSize
        // CounterCode  →  fBox
        // DivisionCode →  fDiv
        // Parent       →  fParent
        // Pcs          →  Qty
        // GrossWt      →  Gross
        // LessWt       →  StnWt
        // NetWt        →  Gms
        // VA           →  Wastage
        // Making       →  McAmount
        // StoneChg     →  StnChrg
        // Others       →  fOthers
        // ShortNarr    →  Narration
        // LongNarr     →  fDescription
        // InOutStock   →  fInOutStock
        // image1-4     →  uploaded files → fImage1-4
        // ===================================================================
        [HttpPost("Save")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Save(
            [FromForm] ItemPurchaseOPModel req,
            IFormFile? image1,
            IFormFile? image2,
            IFormFile? image3,
            IFormFile? image4)
        {
            if (req == null)                            return BadRequest(new { message = "Request data is required." });
            if (string.IsNullOrWhiteSpace(req.RefNo))  return BadRequest(new { message = "RefNo (Voucher) is required." });
            if (string.IsNullOrWhiteSpace(req.ItemCode)) return BadRequest(new { message = "ItemCode is required." });
            if (string.IsNullOrWhiteSpace(req.Barcode)) return BadRequest(new { message = "Barcode (Prefix) is required." });

            try
            {
                // 1. Duplicate barcode check
                using (var con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using var chk = new SqlCommand(
                        "SELECT COUNT(1) FROM ItemPurchaseOP WHERE fPrefix = @p", con);
                    chk.Parameters.AddWithValue("@p", req.Barcode.Trim());
                    if ((int)(await chk.ExecuteScalarAsync())! > 0)
                        return Conflict(new { message = "This barcode already exists.", Barcode = req.Barcode });
                }

                // 2. Save images (null → stored as NULL in DB)
                string? img1 = await SaveImageAsync(image1, req.ItemCode, 1);
                string? img2 = await SaveImageAsync(image2, req.ItemCode, 2);
                string? img3 = await SaveImageAsync(image3, req.ItemCode, 3);
                string? img4 = await SaveImageAsync(image4, req.ItemCode, 4);

                // 3. Server-side lookups
                string fCompCode = await GetCompCodeAsync();
                // fRefNo = numeric part of voucher: OP000063AA → 000063
                string fRefNo = req.RefNo.Length >= 8 ? req.RefNo.Substring(2, 6) : req.RefNo;
                // fTax and fCategories from item table
                string fTax        = "";
                string fCategories = "ITEMS";
                using (var con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    // fTax
                    using var cmd = new SqlCommand("SELECT ISNULL(fTax,'') FROM item WHERE fitemcode=@c", con);
                    cmd.Parameters.AddWithValue("@c", req.ItemCode);
                    fTax = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";

                    // fCategories: fparent is NCHAR(15) storing parentcode+itemcode concatenated
                    // actual parent = LEFT(fparent, 10); match against LEFT(fparent,10) of other rows
                    using var catCmd = new SqlCommand(
                        @"DECLARE @par NVARCHAR(50)
                          SELECT @par = LTRIM(RTRIM(LEFT(fparent, 10)))
                          FROM item
                          WHERE LTRIM(RTRIM(fitemcode)) = @c2

                          SELECT TOP 1 ISNULL(LTRIM(RTRIM(fitemname)),'ITEMS')
                          FROM item
                          WHERE LTRIM(RTRIM(fparent)) = @par
                            AND faclevel > 0", con);
                    catCmd.Parameters.AddWithValue("@c2", req.ItemCode.Trim());
                    fCategories = (await catCmd.ExecuteScalarAsync())?.ToString() ?? "ITEMS";
                }

                // 4. INSERT
                const string sql = @"
INSERT INTO ItemPurchaseOP (
    Voucher,Type,Itemcode,Goodown,fTax,Qty,Gms,Wastage,StnChrg,
    code,McAmount,Narration,fFlag,fPrefix,fBox,StnWt,Gross,
    fDiv,fMtType,fSize,fDate,fCompCode,fRefNo,fDesign,fSection,
    FHUID,fDescription,fInOutStock,fCertificate,fParent,fOthers,
    fImage1,fImage2,fImage3,fImage4,fMetalType,fCategories,fPiecerate
) VALUES (
    @Voucher,@Type,@Itemcode,@Goodown,@fTax,@Qty,@Gms,@Wastage,@StnChrg,
    @code,@McAmount,@Narration,@fFlag,@fPrefix,@fBox,@StnWt,@Gross,
    @fDiv,@fMtType,@fSize,@fDate,@fCompCode,@fRefNo,@fDesign,@fSection,
    @FHUID,@fDescription,@fInOutStock,@fCertificate,@fParent,@fOthers,
    @fImage1,@fImage2,@fImage3,@fImage4,@fMetalType,@fCategories,@fPiecerate
)";
                using (var con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using var ins = new SqlCommand(sql, con);
                    ins.Parameters.AddWithValue("@Voucher",      req.RefNo);
                    ins.Parameters.AddWithValue("@Type",         "ON");
                    ins.Parameters.AddWithValue("@Itemcode",     req.ItemCode);
                    ins.Parameters.AddWithValue("@Goodown",      "001");
                    ins.Parameters.AddWithValue("@fTax",         fTax);
                    ins.Parameters.AddWithValue("@Qty",          req.Pcs);
                    ins.Parameters.AddWithValue("@Gms",          req.NetWt);
                    ins.Parameters.AddWithValue("@Wastage",      req.VA);
                    ins.Parameters.AddWithValue("@StnChrg",      req.StoneChg);
                    ins.Parameters.AddWithValue("@code",         DBNull.Value);
                    ins.Parameters.AddWithValue("@McAmount",     req.Making);
                    ins.Parameters.AddWithValue("@Narration",    req.ShortNarr ?? "");
                    ins.Parameters.AddWithValue("@fFlag",        "Y");
                    ins.Parameters.AddWithValue("@fPrefix",      req.Barcode);
                    ins.Parameters.AddWithValue("@fBox",         req.CounterCode ?? "");
                    ins.Parameters.AddWithValue("@StnWt",        req.LessWt);
                    ins.Parameters.AddWithValue("@Gross",        req.GrossWt);
                    ins.Parameters.AddWithValue("@fDiv",         req.DivisionCode ?? "");
                    ins.Parameters.AddWithValue("@fMtType",      "ITEMS");
                    ins.Parameters.AddWithValue("@fSize",        req.SizeCode ?? "");
                    ins.Parameters.AddWithValue("@fDate",        DateTime.Today);
                    ins.Parameters.AddWithValue("@fCompCode",    fCompCode);
                    ins.Parameters.AddWithValue("@fRefNo",       fRefNo);
                    ins.Parameters.AddWithValue("@fDesign",      req.DesignCode ?? "");
                    ins.Parameters.AddWithValue("@fSection",     req.SectionCode ?? "");
                    ins.Parameters.AddWithValue("@FHUID",        req.HuidNo ?? "");
                    ins.Parameters.AddWithValue("@fDescription", req.LongNarr ?? "");
                    ins.Parameters.AddWithValue("@fInOutStock",  req.InOutStock ?? "InStock");
                    ins.Parameters.AddWithValue("@fCertificate", req.HuidNo ?? "");
                    ins.Parameters.AddWithValue("@fParent",      req.Parent ?? "");
                    ins.Parameters.AddWithValue("@fOthers",      req.Others == 0 ? (object)DBNull.Value : req.Others);
                    ins.Parameters.AddWithValue("@fImage1",      (object?)img1 ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@fImage2",      (object?)img2 ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@fImage3",      (object?)img3 ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@fImage4",      (object?)img4 ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@fMetalType",   "ITEMS");
                    ins.Parameters.AddWithValue("@fCategories",  fCategories);
                    ins.Parameters.AddWithValue("@fPiecerate",   "N");

                    int rows = await ins.ExecuteNonQueryAsync();
                    if (rows > 0)
                        return StatusCode(201, new { message = "Record saved successfully.", RefNo = req.RefNo });
                    return StatusCode(500, new { message = "Insert failed. No rows affected." });
                }
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (InvalidOperationException ioEx) { return BadRequest(new { message = ioEx.Message }); }
            catch (Exception ex) { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // PUT  api/ItemPurchaseOP/Update
        // Same field mapping as POST. Image1-4 = existing filenames to preserve;
        // send new file in image1-4 multipart parts to replace.
        // ===================================================================
        [HttpPut("Update")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Update(
            [FromForm] ItemPurchaseOPModel req,
            IFormFile? image1,
            IFormFile? image2,
            IFormFile? image3,
            IFormFile? image4)
        {
            if (req == null)                           return BadRequest(new { message = "Request data is required." });
            if (string.IsNullOrWhiteSpace(req.RefNo)) return BadRequest(new { message = "RefNo (Voucher) is required." });

            try
            {
                // New upload wins; fallback to existing filename sent in Image1-4
                string img1 = (await SaveImageAsync(image1, req.ItemCode ?? "", 1)) ?? req.Image1 ?? "";
                string img2 = (await SaveImageAsync(image2, req.ItemCode ?? "", 2)) ?? req.Image2 ?? "";
                string img3 = (await SaveImageAsync(image3, req.ItemCode ?? "", 3)) ?? req.Image3 ?? "";
                string img4 = (await SaveImageAsync(image4, req.ItemCode ?? "", 4)) ?? req.Image4 ?? "";

                // fRefNo from voucher
                string fRefNo = (req.RefNo?.Length ?? 0) >= 8 ? req.RefNo!.Substring(2, 6) : req.RefNo ?? "";

                const string sql = @"
UPDATE ItemPurchaseOP SET
    Itemcode=@Itemcode, fTax=@fTax, Qty=@Qty, Gms=@Gms, Wastage=@Wastage,
    StnChrg=@StnChrg, code=@code, McAmount=@McAmount, Narration=@Narration,
    fPrefix=@fPrefix, fBox=@fBox, StnWt=@StnWt, Gross=@Gross,
    fDiv=@fDiv, fMtType=@fMtType, fSize=@fSize, fDate=@fDate,
    fRefNo=@fRefNo, fDesign=@fDesign, fSection=@fSection, FHUID=@FHUID,
    fDescription=@fDescription, fInOutStock=@fInOutStock, fCertificate=@fCertificate,
    fParent=@fParent, fOthers=@fOthers,
    fImage1=@fImage1, fImage2=@fImage2, fImage3=@fImage3, fImage4=@fImage4,
    fMetalType=@fMetalType, fCategories=@fCategories, fPiecerate=@fPiecerate
WHERE Voucher = @Voucher";

                // fTax and fCategories from item table
                string fTax        = "";
                string fCategories = "ITEMS";
                if (!string.IsNullOrWhiteSpace(req.ItemCode))
                {
                    using var con0 = new SqlConnection(DBHelper.GetConnection());
                    await con0.OpenAsync();

                    // fTax
                    using var cmd0 = new SqlCommand("SELECT ISNULL(fTax,'') FROM item WHERE fitemcode=@c", con0);
                    cmd0.Parameters.AddWithValue("@c", req.ItemCode);
                    fTax = (await cmd0.ExecuteScalarAsync())?.ToString() ?? "";

                    // fCategories: fparent is NCHAR(15) storing parentcode+itemcode concatenated
                    // actual parent = LEFT(fparent, 10); match against LEFT(fparent,10) of other rows
                    using var catCmd0 = new SqlCommand(
                        @"DECLARE @par NVARCHAR(50)
                          SELECT @par = LTRIM(RTRIM(LEFT(fparent, 10)))
                          FROM item
                          WHERE LTRIM(RTRIM(fitemcode)) = @c2

                          SELECT TOP 1 ISNULL(LTRIM(RTRIM(fitemname)),'ITEMS')
                          FROM item
                          WHERE LTRIM(RTRIM(fparent)) = @par
                            AND faclevel > 0", con0);
                    catCmd0.Parameters.AddWithValue("@c2", req.ItemCode.Trim());
                    fCategories = (await catCmd0.ExecuteScalarAsync())?.ToString() ?? "ITEMS";
                }

                using (var con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();
                    using var cmd = new SqlCommand(sql, con);
                    cmd.Parameters.AddWithValue("@Voucher",      req.RefNo);
                    cmd.Parameters.AddWithValue("@Itemcode",     req.ItemCode ?? "");
                    cmd.Parameters.AddWithValue("@fTax",         fTax);
                    cmd.Parameters.AddWithValue("@Qty",          req.Pcs);
                    cmd.Parameters.AddWithValue("@Gms",          req.NetWt);
                    cmd.Parameters.AddWithValue("@Wastage",      req.VA);
                    cmd.Parameters.AddWithValue("@StnChrg",      req.StoneChg);
                    cmd.Parameters.AddWithValue("@code",         DBNull.Value);
                    cmd.Parameters.AddWithValue("@McAmount",     req.Making);
                    cmd.Parameters.AddWithValue("@Narration",    req.ShortNarr ?? "");
                    cmd.Parameters.AddWithValue("@fPrefix",      req.Barcode ?? "");
                    cmd.Parameters.AddWithValue("@fBox",         req.CounterCode ?? "");
                    cmd.Parameters.AddWithValue("@StnWt",        req.LessWt);
                    cmd.Parameters.AddWithValue("@Gross",        req.GrossWt);
                    cmd.Parameters.AddWithValue("@fDiv",         req.DivisionCode ?? "");
                    cmd.Parameters.AddWithValue("@fMtType",      "ITEMS");
                    cmd.Parameters.AddWithValue("@fSize",        req.SizeCode ?? "");
                    cmd.Parameters.AddWithValue("@fDate",        DateTime.Today);
                    cmd.Parameters.AddWithValue("@fRefNo",       fRefNo);
                    cmd.Parameters.AddWithValue("@fDesign",      req.DesignCode ?? "");
                    cmd.Parameters.AddWithValue("@fSection",     req.SectionCode ?? "");
                    cmd.Parameters.AddWithValue("@FHUID",        req.HuidNo ?? "");
                    cmd.Parameters.AddWithValue("@fDescription", req.LongNarr ?? "");
                    cmd.Parameters.AddWithValue("@fInOutStock",  req.InOutStock ?? "InStock");
                    cmd.Parameters.AddWithValue("@fCertificate", req.HuidNo ?? "");
                    cmd.Parameters.AddWithValue("@fParent",      req.Parent ?? "");
                    cmd.Parameters.AddWithValue("@fOthers",      req.Others == 0 ? (object)DBNull.Value : req.Others);
                    cmd.Parameters.AddWithValue("@fImage1",      string.IsNullOrEmpty(img1) ? (object)DBNull.Value : img1);
                    cmd.Parameters.AddWithValue("@fImage2",      string.IsNullOrEmpty(img2) ? (object)DBNull.Value : img2);
                    cmd.Parameters.AddWithValue("@fImage3",      string.IsNullOrEmpty(img3) ? (object)DBNull.Value : img3);
                    cmd.Parameters.AddWithValue("@fImage4",      string.IsNullOrEmpty(img4) ? (object)DBNull.Value : img4);
                    cmd.Parameters.AddWithValue("@fMetalType",   "ITEMS");
                    cmd.Parameters.AddWithValue("@fCategories",  fCategories);
                    cmd.Parameters.AddWithValue("@fPiecerate",   "N");

                    int rows = await cmd.ExecuteNonQueryAsync();
                    if (rows > 0) return Ok(new { message = "Record updated successfully.", RefNo = req.RefNo });
                    return NotFound(new { message = "No record found for the given RefNo." });
                }
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (InvalidOperationException ioEx) { return BadRequest(new { message = ioEx.Message }); }
            catch (Exception ex) { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // DELETE  api/ItemPurchaseOP/Delete/{voucher}
        // ===================================================================
        [HttpDelete("Delete/{voucher}")]
        public async Task<IActionResult> Delete([FromRoute] string voucher)
        {
            if (string.IsNullOrWhiteSpace(voucher))
                return BadRequest(new { message = "Voucher is required." });
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand("DELETE FROM ItemPurchaseOP WHERE Voucher=@v", con);
                cmd.Parameters.AddWithValue("@v", voucher);
                int rows = await cmd.ExecuteNonQueryAsync();
                if (rows > 0) return Ok(new { message = "Record deleted.", Voucher = voucher });
                return NotFound(new { message = "No record found." });
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (Exception ex)       { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ItemPurchaseOP/GetByVoucher/{voucher}
        // Returns full record with all joined lookup names.
        // Response field names match the same labels as the POST payload.
        // ===================================================================
        [HttpGet("GetByVoucher/{voucher}")]
        public async Task<IActionResult> GetByVoucher([FromRoute] string voucher)
        {
            if (string.IsNullOrWhiteSpace(voucher))
                return BadRequest(new { message = "Voucher is required." });

            const string query = @"
SELECT
    P.VOUCHER        AS RefNo,
    P.FPREFIX        AS Barcode,
    P.Itemcode       AS ItemCode,
    ISNULL(I.FITEMNAME,'')  AS ItemName,
    P.FHUID          AS HuidNo,
    P.FDESIGN        AS DesignCode,
    ISNULL(D.FNAME,'')      AS DesignName,
    P.FSECTION       AS SectionCode,
    ISNULL(S.FNAME,'')      AS SectionName,
    P.FSIZE          AS SizeCode,
    ISNULL(Z.FSIZE,'')      AS SizeName,
    P.FDIV           AS DivisionCode,
    ISNULL(V.fName,'')      AS DivisionName,
    P.FBOX           AS CounterCode,
    ISNULL(C.FBOX,'')       AS BoxName,
    P.QTY            AS Pcs,
    P.GROSS          AS GrossWt,
    P.STNWT          AS LessWt,
    P.Gms            AS NetWt,
    P.WASTAGE        AS VA,
    P.MCAMOUNT       AS Making,
    P.STNCHRG        AS StoneChg,
    P.FOTHERS        AS Others,
    P.FTAX           AS GST,
    P.FMTTYPE        AS MType,
    P.FDATE          AS EDate,
    P.FCOMPCODE      AS CompCode,
    P.FREFNO         AS RefNoInner,
    P.Narration      AS ShortNarr,
    P.FDESCRIPTION   AS LongNarr,
    P.FINOUTSTOCK    AS InOutStock,
    P.FCERTIFICATE   AS Certificate,
    P.FPARENT        AS Parent,
    P.FIMAGE1        AS Image1,
    P.FIMAGE2        AS Image2,
    P.FIMAGE3        AS Image3,
    P.FIMAGE4        AS Image4,
    P.FMETALTYPE     AS MetalType,
    P.FCATEGORIES    AS Categories,
    P.FPIECERATE     AS PieceRate,
    P.FFLAG          AS Flag
FROM ITEMPURCHASEOP P
LEFT JOIN ITEM     I ON I.FITEMCODE = P.ITEMCODE
LEFT JOIN DESIGN   D ON D.FCODE     = P.FDESIGN
LEFT JOIN SECTION  S ON S.fCode     = P.fSection
LEFT JOIN SIZE     Z ON Z.FCODE     = P.FSIZE
LEFT JOIN DIVISION V ON V.FCODE     = P.FDIV
LEFT JOIN BOX      C ON C.FCODE     = P.FBOX
WHERE P.VOUCHER = @v AND P.FFlag = 'Y'";

            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(query, con);
                cmd.Parameters.AddWithValue("@v", voucher);
                using var reader = await cmd.ExecuteReaderAsync();

                var list = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    list.Add(row);
                }

                if (list.Count == 0) return NotFound(new { message = "No records found." });
                return Ok(list.Count == 1 ? (object)list[0] : list);
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (Exception ex)       { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // GET  api/ItemPurchaseOP/GetVoucherList
        // Paginated + searchable list for the update/delete combo dropdown.
        // Query params: page (default 1), pageSize (default 20), search (optional)
        // Search matches Voucher, fPrefix (Barcode), or ItemName.
        // ===================================================================
        [HttpGet("GetVoucherList")]
        public async Task<IActionResult> GetVoucherList(
            [FromQuery] int    page     = 1,
            [FromQuery] int    pageSize = 20,
            [FromQuery] string search   = "")
        {
            if (page < 1)     page     = 1;
            if (pageSize < 1) pageSize = 20;

            string where = string.IsNullOrWhiteSpace(search)
                ? "WHERE P.FFlag = 'Y'"
                : @"WHERE P.FFlag = 'Y'
                    AND (P.VOUCHER  LIKE @s
                      OR P.FPREFIX LIKE @s
                      OR I.FITEMNAME LIKE @s)";

            string countSql = $@"
SELECT COUNT(DISTINCT P.VOUCHER)
FROM ITEMPURCHASEOP P
LEFT JOIN ITEM I ON I.FITEMCODE = P.ITEMCODE
{where}";

            string dataSql = $@"
SELECT DISTINCT
    P.VOUCHER       AS RefNo,
    P.FPREFIX       AS Barcode,
    ISNULL(I.FITEMNAME,'') AS ItemName,
    P.FDATE         AS EDate
FROM ITEMPURCHASEOP P
LEFT JOIN ITEM I ON I.FITEMCODE = P.ITEMCODE
{where}
ORDER BY P.VOUCHER DESC
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            try
            {
                int total = 0;
                var list  = new List<object>();

                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();

                using (var cmd = new SqlCommand(countSql, con))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        cmd.Parameters.AddWithValue("@s", "%" + search.Trim() + "%");
                    total = (int)(await cmd.ExecuteScalarAsync())!;
                }

                using (var cmd = new SqlCommand(dataSql, con))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        cmd.Parameters.AddWithValue("@s", "%" + search.Trim() + "%");
                    cmd.Parameters.AddWithValue("@offset",   (page - 1) * pageSize);
                    cmd.Parameters.AddWithValue("@pageSize", pageSize);

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        list.Add(new
                        {
                            RefNo    = reader["RefNo"]?.ToString(),
                            Barcode  = reader["Barcode"]?.ToString(),
                            ItemName = reader["ItemName"]?.ToString(),
                            EDate    = reader["EDate"] == DBNull.Value ? null : (DateTime?)reader.GetDateTime(reader.GetOrdinal("EDate")),
                        });
                    }
                }

                return Ok(new
                {
                    totalRecords = total,
                    page,
                    pageSize,
                    totalPages   = (int)Math.Ceiling((double)total / pageSize),
                    data         = list
                });
            }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (Exception ex)       { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }

        // ===================================================================
        // PATCH  api/ItemPurchaseOP/UpdateImages/{voucher}
        // Upload/replace images only. Send existing filename in Image1-4 to keep.
        // ===================================================================
        [HttpPatch("UpdateImages/{voucher}")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateImages(
            [FromRoute] string   voucher,
            [FromForm]  string?  ItemCode,
            [FromForm]  string?  Image1,
            [FromForm]  string?  Image2,
            [FromForm]  string?  Image3,
            [FromForm]  string?  Image4,
            IFormFile?  image1,
            IFormFile?  image2,
            IFormFile?  image3,
            IFormFile?  image4)
        {
            if (string.IsNullOrWhiteSpace(voucher))  return BadRequest(new { message = "Voucher is required." });
            if (string.IsNullOrWhiteSpace(ItemCode)) return BadRequest(new { message = "ItemCode is required." });

            try
            {
                string img1 = (await SaveImageAsync(image1, ItemCode, 1)) ?? Image1 ?? "";
                string img2 = (await SaveImageAsync(image2, ItemCode, 2)) ?? Image2 ?? "";
                string img3 = (await SaveImageAsync(image3, ItemCode, 3)) ?? Image3 ?? "";
                string img4 = (await SaveImageAsync(image4, ItemCode, 4)) ?? Image4 ?? "";

                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var cmd = new SqlCommand(
                    "UPDATE ItemPurchaseOP SET fImage1=@i1,fImage2=@i2,fImage3=@i3,fImage4=@i4 WHERE Voucher=@v", con);
                cmd.Parameters.AddWithValue("@v",  voucher);
                cmd.Parameters.AddWithValue("@i1", string.IsNullOrEmpty(img1) ? (object)DBNull.Value : img1);
                cmd.Parameters.AddWithValue("@i2", string.IsNullOrEmpty(img2) ? (object)DBNull.Value : img2);
                cmd.Parameters.AddWithValue("@i3", string.IsNullOrEmpty(img3) ? (object)DBNull.Value : img3);
                cmd.Parameters.AddWithValue("@i4", string.IsNullOrEmpty(img4) ? (object)DBNull.Value : img4);

                int rows = await cmd.ExecuteNonQueryAsync();
                if (rows > 0) return Ok(new { message = "Images updated.", Image1 = img1, Image2 = img2, Image3 = img3, Image4 = img4 });
                return NotFound(new { message = "Voucher not found." });
            }
            catch (InvalidOperationException ioEx) { return BadRequest(new { message = ioEx.Message }); }
            catch (SqlException sqlEx) { return StatusCode(500, new { message = "Database error.", error = sqlEx.Message }); }
            catch (Exception ex)       { return StatusCode(500, new { message = "Internal server error.", error = ex.Message }); }
        }
    }
}

using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.New_Update
{
    [Route("api/[controller]")]
    [ApiController]
    public class CustomOrderController : ControllerBase
    {
        // ─────────────────────────────────────────────────────────────────────────
        // POST  api/CustomOrder/save
        //
        // IsNew = true  → generate next VouchNo (O00001 …) and INSERT
        // IsNew = false → DELETE existing rows with same VouchNo, then INSERT again
        //
        // Images (Image1–Image5) are multipart form-data files.
        // Saved as  wwwroot/CustomOrder/{VouchNo}_1.jpg  etc.
        // Only the file name is stored in DB.
        // Default Status = "N"
        // ─────────────────────────────────────────────────────────────────────────
        [HttpPost("SaveCustomOrder")]
        public async Task<IActionResult> SaveCustomOrder(
            [FromForm] CustomOrderRequest req,
            IFormFile? Image1,
            IFormFile? Image2,
            IFormFile? Image3,
            IFormFile? Image4,
            IFormFile? Image5)
        {
            SqlConnection? conn = null;
            SqlTransaction? tran = null;
            var savedImagePaths = new List<string>(); // for rollback on error

            try
            {
                // ── 1. Validate ──────────────────────────────────────────────────
                if (!req.IsNew && string.IsNullOrWhiteSpace(req.VouchNo))
                    return BadRequest(new { success = false, message = "VouchNo is required when IsNew = false." });

                var imageSlots = new IFormFile?[] { Image1, Image2, Image3, Image4, Image5 };
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

                // Validate image files up-front before touching the DB
                for (int i = 0; i < 5; i++)
                {
                    var file = imageSlots[i];
                    if (file == null || file.Length == 0) continue;

                    var ext = Path.GetExtension(file.FileName).ToLower();
                    if (!allowedExtensions.Contains(ext))
                        return BadRequest(new { success = false, message = $"Image{i + 1}: unsupported file type '{ext}'." });

                    if (file.Length > 20 * 1024 * 1024)
                        return BadRequest(new { success = false, message = $"Image{i + 1} exceeds 20 MB limit." });
                }

                // ── 2. Prepare upload folder ──────────────────────────────────────
                string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "CustomOrder");
                if (!Directory.Exists(uploadFolder))
                    Directory.CreateDirectory(uploadFolder);

                // ── 3. Open DB & resolve VouchNo ──────────────────────────────────
                conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();
                tran = conn.BeginTransaction();

                string vouchNo;
                List<string> oldImageFilePaths = new();

                if (req.IsNew)
                {
                    // Generate next VouchNo from MAX in table
                    using var cmdMax = new SqlCommand("SELECT MAX(VouchNo) FROM CustomOrder", conn, tran);
                    string? lastVouch = Convert.ToString(await cmdMax.ExecuteScalarAsync());

                    if (string.IsNullOrWhiteSpace(lastVouch))
                    {
                        vouchNo = "O00001";
                    }
                    else
                    {
                        string digits = new string(lastVouch.Where(char.IsDigit).ToArray());
                        int next = int.Parse(digits) + 1;
                        vouchNo = "O" + next.ToString("D5");
                    }
                }
                else
                {
                    vouchNo = req.VouchNo!;

                    // Collect old image file names before deleting the row
                    string selectOldImgs = @"
                        SELECT Image1, Image2, Image3, Image4, Image5
                        FROM CustomOrder WHERE VouchNo = @VouchNo";

                    using (var cmdOld = new SqlCommand(selectOldImgs, conn, tran))
                    {
                        cmdOld.Parameters.AddWithValue("@VouchNo", vouchNo);
                        using var rdr = await cmdOld.ExecuteReaderAsync();
                        while (await rdr.ReadAsync())
                        {
                            for (int c = 0; c < 5; c++)
                            {
                                var v = rdr[c]?.ToString();
                                if (!string.IsNullOrWhiteSpace(v))
                                    oldImageFilePaths.Add(Path.Combine(uploadFolder, v));
                            }
                        }
                    }

                    // Delete existing record
                    using var cmdDel = new SqlCommand(
                        "DELETE FROM CustomOrder WHERE VouchNo = @VouchNo", conn, tran);
                    cmdDel.Parameters.AddWithValue("@VouchNo", vouchNo);
                    await cmdDel.ExecuteNonQueryAsync();
                }

                // ── 4. Save images to disk using VouchNo in the name ──────────────
                // Pattern: {VouchNo}_1.ext  →  O00001_1.jpg
                var imageFileNames = new string?[5];

                for (int i = 0; i < 5; i++)
                {
                    var file = imageSlots[i];
                    if (file == null || file.Length == 0) continue;

                    var ext      = Path.GetExtension(file.FileName).ToLower();
                    string fileName = $"{vouchNo}_{i + 1}{ext}";        // e.g. O00001_1.jpg
                    string fullPath = Path.Combine(uploadFolder, fileName);

                    // If a file with this name already exists (re-save scenario), remove it first
                    if (System.IO.File.Exists(fullPath))
                        System.IO.File.Delete(fullPath);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                        await file.CopyToAsync(stream);

                    imageFileNames[i] = fileName;
                    savedImagePaths.Add(fullPath);
                }

                // ── 5. INSERT new record  (Status defaults to "N") ────────────────
                string insertQuery = @"
                    INSERT INTO CustomOrder
                        (VouchNo, OrderDate, CustCode, ProductCode, WeightFrom, WeightTo,
                         PurityCode, SizeLength, Width, Quantity, PiecePair, DueDate,
                         SampleWeight, StoneWeight, Remarks,
                         Image1, Image2, Image3, Image4, Image5, Status)
                    VALUES
                        (@VouchNo, @OrderDate, @CustCode, @ProductCode, @WeightFrom, @WeightTo,
                         @PurityCode, @SizeLength, @Width, @Quantity, @PiecePair, @DueDate,
                         @SampleWeight, @StoneWeight, @Remarks,
                         @Image1, @Image2, @Image3, @Image4, @Image5, @Status)";

                using (var cmdIns = new SqlCommand(insertQuery, conn, tran))
                {
                    cmdIns.Parameters.AddWithValue("@VouchNo",      vouchNo);
                    cmdIns.Parameters.AddWithValue("@OrderDate",    req.OrderDate    ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@CustCode",     req.CustCode     ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@ProductCode",  req.ProductCode  ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@WeightFrom",   req.WeightFrom   ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@WeightTo",     req.WeightTo     ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@PurityCode",   req.PurityCode   ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@SizeLength",   req.SizeLength   ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Width",        req.Width        ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Quantity",     req.Quantity     ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@PiecePair",    req.PiecePair    ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@DueDate",      req.DueDate      ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@SampleWeight", req.SampleWeight ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@StoneWeight",  req.StoneWeight  ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Remarks",      req.Remarks      ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Image1",       imageFileNames[0] ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Image2",       imageFileNames[1] ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Image3",       imageFileNames[2] ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Image4",       imageFileNames[3] ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Image5",       imageFileNames[4] ?? (object)DBNull.Value);
                    cmdIns.Parameters.AddWithValue("@Status",       "N");   // default always N

                    await cmdIns.ExecuteNonQueryAsync();
                }

                tran.Commit();

                // ── 6. Post-commit: delete replaced old image files ───────────────
                foreach (var oldPath in oldImageFilePaths)
                {
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                return Ok(new
                {
                    success = true,
                    message = req.IsNew ? "Custom order created." : "Custom order updated.",
                    vouchNo
                });
            }
            catch (Exception ex)
            {
                tran?.Rollback();

                // Roll back any image files saved during this failed request
                foreach (var p in savedImagePaths)
                {
                    if (System.IO.File.Exists(p))
                        System.IO.File.Delete(p);
                }

                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
            finally
            {
                conn?.Close();
            }
        }


        // ─────────────────────────────────────────────────────────────────────────
        // PUT  api/CustomOrder/approve/{vouchNo}
        // Sets Status = "Y" for the given VouchNo
        // ─────────────────────────────────────────────────────────────────────────
        [HttpPut("DeliveryStatus/{vouchNo}")]
        public async Task<IActionResult> ApproveCustomOrder(string vouchNo)
        {
            if (string.IsNullOrWhiteSpace(vouchNo))
                return BadRequest(new { success = false, message = "VouchNo is required." });

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                string updateQuery = "UPDATE CustomOrder SET Status = 'Y' WHERE VouchNo = @VouchNo";

                using var cmd = new SqlCommand(updateQuery, conn);
                cmd.Parameters.AddWithValue("@VouchNo", vouchNo);

                int rows = await cmd.ExecuteNonQueryAsync();

                if (rows == 0)
                    return NotFound(new { success = false, message = $"No order found with VouchNo '{vouchNo}'." });

                return Ok(new { success = true, message = $"Order {vouchNo} approved (Status = Y)." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────────────────────
        // GET  api/CustomOrder/list
        //      ?page=1&pageSize=10
        //      &search=abc          ← searches VouchNo, OrderDate, PartyName
        //      &custCode=C001       ← exact filter on CustCode / fCode
        //      &status=N            ← exact filter on Status  (N or Y)
        // ─────────────────────────────────────────────────────────────────────────
        [HttpGet("GetCustomOrders")]
        public async Task<IActionResult> GetCustomOrders(
            [FromQuery] int     page     = 1,
            [FromQuery] int     pageSize = 10,
            [FromQuery] string? search   = null,
            [FromQuery] string? custCode = null,
            [FromQuery] string? status   = null)
        {
            if (page < 1)     page     = 1;
            if (pageSize < 1) pageSize = 10;

            int offset = (page - 1) * pageSize;

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                // Build WHERE conditions
                var conditions = new List<string>();

                if (!string.IsNullOrWhiteSpace(search))
                    conditions.Add(@"(
                        co.VouchNo             LIKE @Search OR
                        co.OrderDate           LIKE @Search OR
                        ISNULL(p.fAcName,'')   LIKE @Search OR
                        ISNULL(i.fItemName,'') LIKE @Search OR
                        ISNULL(d.fName,'')     LIKE @Search
                    )");

                if (!string.IsNullOrWhiteSpace(custCode))
                    conditions.Add("co.CustCode = @CustCode");

                if (!string.IsNullOrWhiteSpace(status))
                    conditions.Add("co.Status = @Status");

                string whereClause = conditions.Count > 0
                    ? "WHERE " + string.Join(" AND ", conditions)
                    : "";

                string joins = @"
                    FROM CustomOrder co
                    LEFT JOIN registerusers p
                        ON p.UserID = co.CustCode
                    LEFT JOIN item i
                        ON i.fItemcode = co.ProductCode
                       AND i.fparent LIKE '000010010100111%'
                       AND i.faclevel < 0
                    LEFT JOIN division d ON d.fCode = co.PurityCode";

                string countQuery = $"SELECT COUNT(*) {joins} {whereClause}";

                string dataQuery = $@"
                    SELECT
                        co.VouchNo,
                        co.VouchNo,
                        co.OrderDate,
                        co.CustCode,
                        ISNULL(p.UserName,'')   AS PartyName,
                        co.ProductCode,
                        ISNULL(i.fItemName,'') AS ProductName,
                        co.WeightFrom,
                        co.WeightTo,
                        co.PurityCode,
                        ISNULL(d.fName,'')     AS PurityName,
                        co.SizeLength,
                        co.Width,
                        co.Quantity,
                        co.PiecePair,
                        co.DueDate,
                        co.SampleWeight,
                        co.StoneWeight,
                        co.Remarks,
                        co.Image1,
                        co.Image2,
                        co.Image3,
                        co.Image4,
                        co.Image5,
                        co.Status
                    {joins}
                    {whereClause}
                    ORDER BY co.VouchNo DESC
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                int totalRecords;
                using (var cmdCount = new SqlCommand(countQuery, conn))
                {
                    AddFilterParams(cmdCount, search, custCode, status);
                    totalRecords = Convert.ToInt32(await cmdCount.ExecuteScalarAsync());
                }

                var orders = new List<object>();
                using (var cmdData = new SqlCommand(dataQuery, conn))
                {
                    AddFilterParams(cmdData, search, custCode, status);
                    cmdData.Parameters.AddWithValue("@Offset",   offset);
                    cmdData.Parameters.AddWithValue("@PageSize", pageSize);

                    using var rdr = await cmdData.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        orders.Add(new
                        {
                            CustomOrderId = rdr["VouchNo"]?.ToString(),
                            VouchNo       = rdr["VouchNo"]?.ToString(),
                            OrderDate     = rdr["OrderDate"]?.ToString(),
                            CustCode      = rdr["CustCode"]?.ToString(),
                            PartyName     = rdr["PartyName"]?.ToString(),
                            ProductCode   = rdr["ProductCode"]?.ToString(),
                            ProductName   = rdr["ProductName"]?.ToString(),
                            WeightFrom    = rdr["WeightFrom"]?.ToString(),
                            WeightTo      = rdr["WeightTo"]?.ToString(),
                            PurityCode    = rdr["PurityCode"]?.ToString(),
                            PurityName    = rdr["PurityName"]?.ToString(),
                            SizeLength    = rdr["SizeLength"]?.ToString(),
                            Width         = rdr["Width"]?.ToString(),
                            Quantity      = rdr["Quantity"]?.ToString(),
                            PiecePair     = rdr["PiecePair"]?.ToString(),
                            DueDate       = rdr["DueDate"]?.ToString(),
                            SampleWeight  = rdr["SampleWeight"]?.ToString(),
                            StoneWeight   = rdr["StoneWeight"]?.ToString(),
                            Remarks       = rdr["Remarks"]?.ToString(),
                            Image1        = rdr["Image1"]?.ToString(),
                            Image2        = rdr["Image2"]?.ToString(),
                            Image3        = rdr["Image3"]?.ToString(),
                            Image4        = rdr["Image4"]?.ToString(),
                            Image5        = rdr["Image5"]?.ToString(),
                            Status        = rdr["Status"]?.ToString()
                        });
                    }
                }

                return Ok(new
                {
                    success      = true,
                    page,
                    pageSize,
                    totalRecords,
                    totalPages   = (int)Math.Ceiling(totalRecords / (double)pageSize),
                    data         = orders
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }

        // Helper: add filter parameters consistently for both COUNT and data queries
        private static void AddFilterParams(
            SqlCommand cmd, string? search, string? custCode, string? status)
        {
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@Search",   $"%{search}%");
            if (!string.IsNullOrWhiteSpace(custCode))
                cmd.Parameters.AddWithValue("@CustCode", custCode);
            if (!string.IsNullOrWhiteSpace(status))
                cmd.Parameters.AddWithValue("@Status",   status);
        }


        // ─────────────────────────────────────────────────────────────────────────
        // GET  api/CustomOrder/product-lookup?page=1&pageSize=20&search=ring
        // ─────────────────────────────────────────────────────────────────────────
        [HttpGet("product-lookup")]
        public async Task<IActionResult> GetProductLookup(
            [FromQuery] int     page     = 1,
            [FromQuery] int     pageSize = 20,
            [FromQuery] string? search   = null)
        {
            if (page < 1)     page     = 1;
            if (pageSize < 1) pageSize = 20;
            int offset = (page - 1) * pageSize;

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                string searchFilter = string.IsNullOrWhiteSpace(search)
                    ? ""
                    : "AND (fItemcode LIKE @Search OR fItemName LIKE @Search)";

                string baseFrom = $@"
                    FROM item
                    WHERE fparent LIKE '000010010100111%'
                      AND faclevel < 0
                      {searchFilter}";

                int totalRecords;
                using (var cmdCount = new SqlCommand($"SELECT COUNT(*) {baseFrom}", conn))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        cmdCount.Parameters.AddWithValue("@Search", $"%{search}%");
                    totalRecords = Convert.ToInt32(await cmdCount.ExecuteScalarAsync());
                }

                var list = new List<object>();
                using (var cmd = new SqlCommand($@"
                    SELECT fItemcode AS ProductCode, fItemName AS ProductName
                    {baseFrom}
                    ORDER BY fItemName
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        cmd.Parameters.AddWithValue("@Search",   $"%{search}%");
                    cmd.Parameters.AddWithValue("@Offset",   offset);
                    cmd.Parameters.AddWithValue("@PageSize", pageSize);

                    using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                        list.Add(new
                        {
                            ProductCode = rdr["ProductCode"]?.ToString(),
                            ProductName = rdr["ProductName"]?.ToString()
                        });
                }

                return Ok(new
                {
                    success      = true,
                    page,
                    pageSize,
                    totalRecords,
                    totalPages   = (int)Math.Ceiling(totalRecords / (double)pageSize),
                    data         = list
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────────────────────
        // GET  api/CustomOrder/purity-lookup?page=1&pageSize=20&search=22k
        // ─────────────────────────────────────────────────────────────────────────
        [HttpGet("purity-lookup")]
        public async Task<IActionResult> GetPurityLookup(
            [FromQuery] int     page     = 1,
            [FromQuery] int     pageSize = 20,
            [FromQuery] string? search   = null)
        {
            if (page < 1)     page     = 1;
            if (pageSize < 1) pageSize = 20;
            int offset = (page - 1) * pageSize;

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                string whereClause = string.IsNullOrWhiteSpace(search)
                    ? ""
                    : "WHERE (fCode LIKE @Search OR fName LIKE @Search)";

                int totalRecords;
                using (var cmdCount = new SqlCommand($"SELECT COUNT(*) FROM division {whereClause}", conn))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        cmdCount.Parameters.AddWithValue("@Search", $"%{search}%");
                    totalRecords = Convert.ToInt32(await cmdCount.ExecuteScalarAsync());
                }

                var list = new List<object>();
                using (var cmd = new SqlCommand($@"
                    SELECT fCode AS PurityCode, fName AS PurityName
                    FROM division
                    {whereClause}
                    ORDER BY fName
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn))
                {
                    if (!string.IsNullOrWhiteSpace(search))
                        cmd.Parameters.AddWithValue("@Search",   $"%{search}%");
                    cmd.Parameters.AddWithValue("@Offset",   offset);
                    cmd.Parameters.AddWithValue("@PageSize", pageSize);

                    using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                        list.Add(new
                        {
                            PurityCode = rdr["PurityCode"]?.ToString(),
                            PurityName = rdr["PurityName"]?.ToString()
                        });
                }

                return Ok(new
                {
                    success      = true,
                    page,
                    pageSize,
                    totalRecords,
                    totalPages   = (int)Math.Ceiling(totalRecords / (double)pageSize),
                    data         = list
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }
    }
}


// ─────────────────────────────────────────────────────────────────────────────
// Request DTO
// ─────────────────────────────────────────────────────────────────────────────
public class CustomOrderRequest
{
    /// <summary>true = new record | false = delete + re-insert with same VouchNo</summary>
    public bool    IsNew        { get; set; }

    /// <summary>Required only when IsNew = false</summary>
    public string? VouchNo      { get; set; }

    public string? OrderDate    { get; set; }
    public string? CustCode     { get; set; }
    public string? ProductCode  { get; set; }
    public string? WeightFrom   { get; set; }
    public string? WeightTo     { get; set; }
    public string? PurityCode   { get; set; }
    public string? SizeLength   { get; set; }
    public string? Width        { get; set; }
    public string? Quantity     { get; set; }
    public string? PiecePair    { get; set; }
    public string? DueDate      { get; set; }
    public string? SampleWeight { get; set; }
    public string? StoneWeight  { get; set; }
    public string? Remarks      { get; set; }
    // Note: Status is NOT in the request — always saved as "N" on insert
}

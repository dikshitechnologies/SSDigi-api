using System.Data;
using JEWELLBISREACT.DBConnection;
using JEWELLBISREACT.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace JEWELLBISREACT.Controllers.ECatalog
{
    [Route("api/[controller]")]
    [ApiController]
    public class ItemGroupCreationController : ControllerBase
    {

        public class TreeNodeModel
        {
   
          
            public string fitemname { get; set; }
            public string fitemcode { get; set; }
            public string fparent { get; set; }
            public int fAclevel { get; set; }
            public string Label { get; set; }
            public List<TreeNodeModel> Children { get; set; } = new();
        }




        [HttpGet("ItemGroupCreationGet")]
        public async Task<IActionResult> ItemGroupCreationData()
        {
            var result = await BuildItemTreeAsync();
            return Ok(result);
        }


        private async Task<List<TreeNodeModel>> BuildItemTreeAsync()
        {
            var query = "SELECT fitemcode, fParent, fitemName, faclevel FROM item WHERE faclevel > 0 ORDER BY faclevel, fitemName ASC, fParent ASC";

            var nodeLookup = new Dictionary<string, TreeNodeModel>();
            var root = new TreeNodeModel { Label = "Account Creation", fitemname = "root" };
            nodeLookup["r"] = root;

            using var conn = new SqlConnection(DBHelper.GetConnection());
            await conn.OpenAsync();

            using var cmd = new SqlCommand(query, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string fcode = reader["fitemcode"].ToString().Trim();
                string fparent = reader["fParent"].ToString().Trim();
                string fname = reader["fitemName"].ToString().Trim();
                int faclevel = Convert.ToInt32(reader["faclevel"]);

                string fParentCode = reader["fParent"]?.ToString();
                string parentName = "No Parent";

                if (!string.IsNullOrEmpty(fParentCode))
                {
                    parentName = await GetParentNameAsync1(fParentCode); 
                }
                var newNode = new TreeNodeModel
                {
                    fitemcode = fcode,
                    fparent = parentName,
                    fitemname = fname,
                    fAclevel = faclevel,
                    Label = fname
                };

                string parentKey = "r";

                if (faclevel == 1)
                {
                    root.Children.Add(newNode);
                    nodeLookup[fcode] = newNode;
                }
                else
                {
                    if (faclevel == 2)
                    {
                        parentKey = fparent.Length == 5 ? "r" : fparent.Substring(Math.Max(0, fparent.Length - 10), 5).Trim();
                    }
                    else if (faclevel > 2)
                    {
                        parentKey = fparent.Substring(Math.Max(0, fparent.Length - 10), 5).Trim();
                    }

                    if (nodeLookup.ContainsKey(parentKey))
                    {
                        nodeLookup[parentKey].Children.Add(newNode);
                        nodeLookup[fcode] = newNode;
                    }
                }
            }

            return root.Children; // Return the tree
        }



        public static async Task<string> GetParentNameAsync1(string fParentCode)
        {
            if (string.IsNullOrEmpty(fParentCode) || fParentCode.Length <= 5)
            {
                return "No Parent";  // Return default if empty or less than 5 characters
            }

            // Get the last 5 characters as parent fcode
            // string modifiedParentCode = fParentCode.Substring(fParentCode.Length - 5, 5);

            string modifiedParentCode = fParentCode.Length > 5 ? fParentCode.Substring(0, 5) : fParentCode;

            string query = "SELECT fitemName FROM item WHERE fparent = @fCode";  

            using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
            {
                await conn.OpenAsync();

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@fCode", modifiedParentCode);

                    object result = await cmd.ExecuteScalarAsync();
                    return result != null ? result.ToString() : "No Parent";
                }
            }


        }


        [HttpGet("ItemgroupCreationDropdownlist")]
        public async Task<IActionResult> ledgergroupCreationDropdownlist()
        {
            string query = "SELECT fitemcode,fItemName, fParent FROM Item WHERE fAclevel > 1";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        List<dynamic> hierarchyData = new List<dynamic>();

                        while (await reader.ReadAsync())
                        {
                            string fParentCode = reader["fParent"]?.ToString();
                            string parentName = "No Parent"; // Default value

                            if (!string.IsNullOrEmpty(fParentCode))
                            {
                                parentName = await GetParentNameAsync(fParentCode);  // Now using async method
                            }

                            hierarchyData.Add(new
                            {
                                fitemcode = reader["fitemcode"].ToString(),
                                fitemname = reader["fItemName"].ToString(),
                                ParentName = parentName
                            });
                        }

                        if (hierarchyData.Count == 0)
                        {
                            return NoContent();
                        }

                        return Ok(hierarchyData);
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "Database error", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        public static async Task<string> GetParentNameAsync(string fParentCode)
        {
            if (string.IsNullOrEmpty(fParentCode) || fParentCode.Length <= 5)
            {
                return "No Parent";  // Return default if empty or less than 5 characters
            }

            // Remove last 5 digits from fParentCode
            string modifiedParentCode = fParentCode.Substring(0, fParentCode.Length - 5);

            string query = "SELECT fItemName FROM Item WHERE fParent = @fCode";  // Ensure correct query

            using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
            {
                await conn.OpenAsync();  // Open connection asynchronously
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@fCode", modifiedParentCode);

                    object result = await cmd.ExecuteScalarAsync();
                    return result != null ? result.ToString() : "No Parent";
                }
            }
        }



    


        //---------------------------------------------Duplicate Name Checking ---------------------------------
        private bool ItemNameExists(SqlConnection con, string itemname, string itemcode = null)
        {

            string query = "SELECT 1 FROM item WHERE fItemName = @fItemName";
            if (itemcode != null)
            {
                query += " AND FITEMCODE <> @FITEMCODE";
            }
             
            using (SqlCommand cmd = new SqlCommand(query, con))
            {
                cmd.Parameters.AddWithValue("@fItemName", itemname);
                if (itemcode != null)
                {
                    cmd.Parameters.AddWithValue("@FITEMCODE", itemcode);
                }

                return cmd.ExecuteScalar() != null;
            }
        }

      
        [HttpPost("ItemGroupCreationPost")]
        public async Task<IActionResult> ItemGroupCreationPostData([FromBody] ItemGroupCreation itemCreatoion)
        {
            if (itemCreatoion == null)
                return BadRequest(new { message = "Invalid data." });

            string queryMaxFCode = "SELECT ISNULL(MAX(fitemcode), 0) + 1 FROM item";
            string queryParent = "SELECT fParent FROM item WHERE fItemName = @fItemName";
            string queryFAclevel = "SELECT fAclevel FROM item WHERE fParent = @fParent";
            string queryInsert = @"INSERT INTO item (fitemcode, fItemName, fParent, fAclevel
                     ,fUnits,fCostPrice,fSellPrice,fReorder,fNosPerBox,fTax) 
                           VALUES (@fitemcode, @fItemName, @fParent, @fAclevel,@fUnits,@fCostPrice,@fSellPrice,@fReorder,@fNosPerBox,@fTax)";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();


                    if (ItemNameExists(conn, itemCreatoion.subGroup))
                    {
                        return Conflict(new { message = "Item name already exists. Please choose a different name." });
                    }
                    int nextFCode = 1;
                    using (SqlCommand cmd = new SqlCommand(queryMaxFCode, conn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null)
                        {
                            nextFCode = Convert.ToInt32(result);
                        }
                    }


                    string formattedFCode = nextFCode.ToString("D5");

                    // Get fParent based on mainGroup (fAcname)
                    string fParent = null;
                    using (SqlCommand cmd1 = new SqlCommand(queryParent, conn))
                    {
                        cmd1.Parameters.Add("@fItemName", SqlDbType.VarChar).Value = itemCreatoion.mainGroup;
                        var result = await cmd1.ExecuteScalarAsync();
                        if (result != null)
                        {
                            fParent = result.ToString();
                        }
                    }

                    if (fParent == null)
                    {
                        return NotFound(new { message = "Parent group not found for the given GroupName." });
                    }

                    // Get fAclevel and increment it
                    int? fAclevel = null;
                    using (SqlCommand cmd1 = new SqlCommand(queryFAclevel, conn))
                    {
                        cmd1.Parameters.Add("@fParent", SqlDbType.VarChar).Value = fParent;
                        var result = await cmd1.ExecuteScalarAsync();
                        if (result != null)
                        {
                            fAclevel = Convert.ToInt32(result) + 1;
                        }
                    }

                    if (fAclevel == null)
                    {
                        return NotFound(new { message = "No parent group found for the given GroupName." });
                    }
         
                    // Insert the new record with 5-digit formatted fCode
                    using (SqlCommand cmd2 = new SqlCommand(queryInsert, conn))
                    {
                        cmd2.Parameters.Add("@fitemcode", SqlDbType.VarChar).Value = formattedFCode;
                        cmd2.Parameters.Add("@fItemName", SqlDbType.VarChar).Value = itemCreatoion.subGroup.ToUpper();
                        cmd2.Parameters.Add("@fParent", SqlDbType.VarChar).Value = fParent + formattedFCode;
                        cmd2.Parameters.Add("@fAclevel", SqlDbType.Int).Value = fAclevel;
                        cmd2.Parameters.AddWithValue("@fUnits", "");
                        cmd2.Parameters.AddWithValue("@fCostPrice", "0");
                        cmd2.Parameters.AddWithValue("@fSellPrice", "0");
                        cmd2.Parameters.AddWithValue("@fReorder", "0");
                        cmd2.Parameters.AddWithValue("@fNosPerBox", "1");
                        cmd2.Parameters.AddWithValue("@fTax", "0");
                        int rowsAffected = await cmd2.ExecuteNonQueryAsync();
                        if (rowsAffected > 0)
                            return StatusCode(201, new { message = " inserted successfully.",fitemcode = formattedFCode });
                        else
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





        [HttpPut("ItemGroupCreationPut")]
        public async Task<IActionResult> ItemGroupCreationPutData([FromBody] ItemGroupCreation itemCreatoion)
        {
            if (itemCreatoion == null)
                return BadRequest(new { message = "Invalid data." });

            string queryParent = "SELECT fParent FROM item WHERE fItemName = @fItemName";
            string queryFAclevel = "SELECT fAclevel FROM item WHERE fParent = @fParent";
            string query = @"UPDATE item 
                     SET fItemName = @fItemName, fParent = @fParent, fAclevel = @fAclevel,fUnits=@fUnits,fCostPrice=@fCostPrice, fSellPrice=@fSellPrice, fReorder=@fReorder, fNosPerBox=@fNosPerBox, fTax=@fTax
                     WHERE fitemcode = @fitemcode";
            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    if (ItemNameExists(conn, itemCreatoion.subGroup,itemCreatoion.fitemCode))
                    {
                        return Conflict(new { message = "Item name already exists. Please choose a different name." });
                    }

                    string fParent = null;
                    using (SqlCommand cmd1 = new SqlCommand(queryParent, conn))
                    {
                        cmd1.Parameters.Add("@fItemName", SqlDbType.VarChar).Value = itemCreatoion.mainGroup;
                        var result = await cmd1.ExecuteScalarAsync();
                        if (result != null)
                        {
                            fParent = result.ToString();
                            //// Remove last 5 characters if length is greater than 5
                            //fParent = parentCode.Length > 5 ? parentCode.Substring(0, parentCode.Length - 5) : parentCode;
                        }
                    }


                    if (fParent == null)
                    {
                        return NotFound(new { message = "Parent group not found for the given GroupName." });
                    }




                    string fitemcode = itemCreatoion.fitemCode;

                    string concatParent = fParent + fitemcode;
                    int faclevelCount = (concatParent.Length / 5);
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.Add("@fitemcode", SqlDbType.VarChar).Value = itemCreatoion.fitemCode; 
                        cmd.Parameters.Add("@fItemName", SqlDbType.VarChar).Value = itemCreatoion.subGroup.ToUpper();
                        cmd.Parameters.Add("@fParent", SqlDbType.VarChar).Value = concatParent;
                        cmd.Parameters.Add("@fAclevel", SqlDbType.Int).Value = faclevelCount;
                        cmd.Parameters.AddWithValue("@fUnits", "");
                        cmd.Parameters.AddWithValue("@fCostPrice", "0");
                        cmd.Parameters.AddWithValue("@fSellPrice", "0");
                        cmd.Parameters.AddWithValue("@fReorder", "0");
                        cmd.Parameters.AddWithValue("@fNosPerBox", "1");
                        cmd.Parameters.AddWithValue("@fTax", "0");
                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                            return Ok(new { message = "Record updated successfully." });

                        return NotFound(new { message = "Record not found or no changes made." });
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





        [HttpDelete("ItemGroupCreationDelete/{fCode}")]
        public async Task<IActionResult> ItemGroupCreationDeleteData([FromRoute] string fCode)
        {
            if (string.IsNullOrWhiteSpace(fCode))
                return BadRequest(new { status = 400, message = "Invalid request. fCode is required." });

            string checkChildQuery = "SELECT COUNT(*) FROM item WHERE fparent LIKE @fParentLike";
            string deleteQuery = "DELETE FROM item WHERE fitemcode = @fitemcode";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    using (SqlCommand checkChildCmd = new SqlCommand(checkChildQuery, conn))
                    {
                        checkChildCmd.Parameters.AddWithValue("@fParentLike", "%" + fCode + "%");
                        int childCount = (int)await checkChildCmd.ExecuteScalarAsync();
                        if (childCount > 1)
                        {
                            return Conflict(new
                            {
                                status = 409,
                                message = $"Cannot delete. This record has child records."
                            });
                        }
                    }

                    using (SqlCommand deleteCmd = new SqlCommand(deleteQuery, conn))
                    {
                        deleteCmd.Parameters.Add(new SqlParameter("@fitemcode", SqlDbType.VarChar) { Value = fCode });
                        int rowsAffected = await deleteCmd.ExecuteNonQueryAsync();
                        if (rowsAffected > 0)
                            return Ok(new { status = 200, message = "Record deleted successfully." });
                        return NotFound(new { status = 404, message = "Record not found." });
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { status = 500, message = "Database error.", error = sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = 500, message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET – Single item group by fitemcode (for edit/view form)
        // GET /api/ItemGroupCreation/GetItemGroup/{fitemcode}
        // -------------------------------------------------------

        [HttpGet("GetItemGroup/{fitemcode}")]
        public async Task<IActionResult> GetItemGroupByCode([FromRoute] string fitemcode)
        {
            if (string.IsNullOrWhiteSpace(fitemcode))
                return BadRequest(new { message = "fitemcode is required." });

            const string query = @"
                SELECT
                    c.fItemcode,
                    c.fItemName,
                    c.fShow      AS Availability,
                    c.fImage,
                    c.Flag,
                    LEFT(c.fParent, LEN(c.fParent) - 5) AS ParentCode,
                    (SELECT TOP 1 p.fItemName
                     FROM   item p
                     WHERE  p.fItemcode = LEFT(c.fParent, LEN(c.fParent) - 5))
                     AS ParentGroupName
                FROM item c
                WHERE c.fItemcode = @fitemcode";

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
                                    Availability    = reader["Availability"].ToString(),
                                    fImage          = reader["fImage"].ToString(),
                                    Flag            = reader["Flag"].ToString(),
                                    ParentCode      = reader["ParentCode"].ToString(),
                                    ParentGroupName = reader["ParentGroupName"].ToString(),
                                });
                            }
                            return NotFound(new { message = "Item group not found." });
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
        // GET – Searchable parent group dropdown
        // GET /api/ItemGroupCreation/GetParentGroups?search=items
        // -------------------------------------------------------

        [HttpGet("GetParentGroups")]
        public async Task<IActionResult> GetParentGroupsDropdown([FromQuery] string search = "")
        {
            string whereClause = string.IsNullOrWhiteSpace(search)
                ? "WHERE ISNUMERIC(fAclevel) = 1 AND CAST(fAclevel AS INT) < 3 AND Flag = 'Y'"
                : "WHERE ISNUMERIC(fAclevel) = 1 AND CAST(fAclevel AS INT) < 3 AND Flag = 'Y' AND UPPER(fItemName) LIKE @search";

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
        // GET – Paginated list for the update/search grid
        // GET /api/ItemGroupCreation/GetItemGroupList?page=1&pageSize=20&search=silver
        // -------------------------------------------------------

        [HttpGet("GetItemGroupList")]
        public async Task<IActionResult> GetItemGroupList(
            [FromQuery] int    page     = 1,
            [FromQuery] int    pageSize = 20,
            [FromQuery] string search   = "")
        {
            if (page < 1)     page     = 1;
            if (pageSize < 1) pageSize = 20;

            string whereClause = string.IsNullOrWhiteSpace(search)
                ? @"WHERE c.fAclevel > 0 AND c.fAclevel < 3
                      AND c.fItemCode > '00001' AND c.Flag = 'Y'"
                : @"WHERE c.fAclevel > 0 AND c.fAclevel < 3
                      AND c.fItemCode > '00001' AND c.Flag = 'Y'
                      AND UPPER(c.fItemName) LIKE @search";

            string countQuery = $"SELECT COUNT(1) FROM item c {whereClause}";

            string dataQuery = $@"
                SELECT DISTINCT
                    c.fItemName  AS ItemGroupName,
                    c.fItemCode  AS ItemCode,
                    LEFT(c.fParent, LEN(c.fParent) - 5) AS ParentCode,
                    (SELECT TOP 1 p.fItemName
                     FROM   item p
                     WHERE  p.fItemcode = LEFT(c.fParent, LEN(c.fParent) - 5)) AS ParentName,
                    c.fShow      AS Availability,
                    c.fImage     AS Image,
                    c.Flag
                FROM item c
                {whereClause}
                ORDER BY c.fItemCode DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            try
            {
                int total = 0;
                var list  = new List<object>();

                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    using (SqlCommand cmd = new SqlCommand(countQuery, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.Trim().ToUpper() + "%");
                        total = (int)await cmd.ExecuteScalarAsync();
                    }

                    using (SqlCommand cmd = new SqlCommand(dataQuery, conn))
                    {
                        if (!string.IsNullOrWhiteSpace(search))
                            cmd.Parameters.AddWithValue("@search", "%" + search.Trim().ToUpper() + "%");

                        cmd.Parameters.AddWithValue("@offset",   (page - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                list.Add(new
                                {
                                    ItemGroupName = reader["ItemGroupName"].ToString(),
                                    ItemCode      = reader["ItemCode"].ToString(),
                                    ParentCode    = reader["ParentCode"].ToString(),
                                    ParentName    = reader["ParentName"].ToString(),
                                    Availability  = reader["Availability"].ToString(),
                                    Image         = reader["Image"].ToString(),
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
                    totalPages = (int)Math.Ceiling((double)total / pageSize),
                    data       = list
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // POST – Create item group with optional image upload
        // Fields: Item Group Name, Parent Group Name, Image, Availability
        // POST /api/ItemGroupCreation/ItemGroupCreate   (multipart/form-data)
        // -------------------------------------------------------

        [HttpPost("ItemGroupCreate")]
        public async Task<IActionResult> ItemGroupCreate(
            [FromForm] string     itemGroupName,
            [FromForm] string     parentGroupName,
            [FromForm] string     availability = "1",
            [FromForm] IFormFile? imageFile    = null)
        {
            if (string.IsNullOrWhiteSpace(itemGroupName))
                return BadRequest(new { message = "Item Group Name is required." });

            if (string.IsNullOrWhiteSpace(parentGroupName))
                return BadRequest(new { message = "Parent Group Name is required." });

            const string queryMaxCode = "SELECT ISNULL(MAX(CAST(fItemcode AS INT)), 0) + 1 FROM item";
            const string queryParent  = "SELECT fParent FROM item WHERE fItemName = @fItemName";

            const string queryInsert = @"
                INSERT INTO item (
                    fItemcode, fItemName, fParent, fAclevel,
                    fShow, fImage, Flag,
                    fCostPrice, fSellPrice, fReorder, fNosPerBox, fTax
                ) VALUES (
                    @fItemcode, @fItemName, @fParent, @fAclevel,
                    @fShow, @fImage, @Flag,
                    '0', '0', '0', '1', '0'
                )";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // Duplicate check
                    if (ItemNameExists(conn, itemGroupName))
                        return Conflict(new { message = $"Item Group '{itemGroupName}' already exists." });

                    // Next item code
                    int nextCode = 1;
                    using (SqlCommand cmd = new SqlCommand(queryMaxCode, conn))
                    {
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) nextCode = Convert.ToInt32(result);
                    }
                    string formattedCode = nextCode.ToString("D5");

                    // Resolve fParent from parentGroupName
                    string fParent = null;
                    using (SqlCommand cmd = new SqlCommand(queryParent, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemName", parentGroupName);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) fParent = result.ToString();
                    }
                    if (fParent == null)
                        return NotFound(new { message = $"Parent group '{parentGroupName}' not found." });

                    string concatParent  = fParent + formattedCode;
                    int    faclevelCount = concatParent.Length / 5;

                    // Handle image upload
                    string imageName = "";
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        var allowedExt = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                        string ext = Path.GetExtension(imageFile.FileName).ToLower();
                        if (!allowedExt.Contains(ext))
                            return BadRequest(new { message = "Unsupported image format. Use jpg, jpeg, png, or webp." });

                        // C_ prefix for group level ≤ 2, S_ for deeper sub-groups
                        string prefix = faclevelCount <= 2 ? "C" : "S";
                        imageName = $"{prefix}_{formattedCode}{ext}";

                        string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        if (!Directory.Exists(uploadFolder))
                            Directory.CreateDirectory(uploadFolder);

                        // Remove any previous file with the same base name
                        foreach (var e in allowedExt)
                        {
                            string old = Path.Combine(uploadFolder, $"{prefix}_{formattedCode}{e}");
                            if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                        }

                        string fullPath = Path.Combine(uploadFolder, imageName);
                        using (var stream = new FileStream(fullPath, FileMode.Create))
                            await imageFile.CopyToAsync(stream);
                    }

                    using (SqlCommand cmd = new SqlCommand(queryInsert, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemcode", formattedCode);
                        cmd.Parameters.AddWithValue("@fItemName", itemGroupName.Trim().ToUpper());
                        cmd.Parameters.AddWithValue("@fParent",   concatParent);
                        cmd.Parameters.AddWithValue("@fAclevel",  faclevelCount);
                        cmd.Parameters.AddWithValue("@fShow",     availability.Trim());
                        cmd.Parameters.AddWithValue("@fImage",    imageName);
                        cmd.Parameters.AddWithValue("@Flag",      "Y");

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                        {
                            string imageUrl = string.IsNullOrEmpty(imageName)
                                ? ""
                                : $"{Request.Scheme}://{Request.Host}/uploads/{imageName}";

                            return StatusCode(201, new
                            {
                                message   = $"'{itemGroupName}' saved successfully.",
                                fItemcode = formattedCode,
                                imageUrl
                            });
                        }
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
        // PUT – Update item group with optional image replacement
        // PUT /api/ItemGroupCreation/ItemGroupUpdate/{fitemcode}  (multipart/form-data)
        // -------------------------------------------------------

        [HttpPut("ItemGroupUpdate/{fitemcode}")]
        public async Task<IActionResult> ItemGroupUpdate(
            [FromRoute] string    fitemcode,
            [FromForm] string     itemGroupName,
            [FromForm] string     parentGroupName,
            [FromForm] string     availability = "1",
            [FromForm] IFormFile? imageFile    = null)
        {
            if (string.IsNullOrWhiteSpace(fitemcode))
                return BadRequest(new { message = "fitemcode is required." });

            if (string.IsNullOrWhiteSpace(itemGroupName))
                return BadRequest(new { message = "Item Group Name is required." });

            if (string.IsNullOrWhiteSpace(parentGroupName))
                return BadRequest(new { message = "Parent Group Name is required." });

            const string queryParent   = "SELECT fParent FROM item WHERE fItemName = @fItemName";
            const string queryOldImage = "SELECT fImage  FROM item WHERE fItemcode = @fItemcode";

            const string queryUpdate = @"
                UPDATE item SET
                    fItemName = @fItemName,
                    fParent   = @fParent,
                    fAclevel  = @fAclevel,
                    fShow     = @fShow,
                    fImage    = @fImage
                WHERE fItemcode = @fItemcode";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // Duplicate name check (exclude self)
                    if (ItemNameExists(conn, itemGroupName, fitemcode))
                        return Conflict(new { message = $"Item Group name '{itemGroupName}' already exists." });

                    // Resolve fParent
                    string fParent = null;
                    using (SqlCommand cmd = new SqlCommand(queryParent, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemName", parentGroupName);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) fParent = result.ToString();
                    }
                    if (fParent == null)
                        return NotFound(new { message = $"Parent group '{parentGroupName}' not found." });

                    string concatParent  = fParent + fitemcode;
                    int    faclevelCount = concatParent.Length / 5;

                    // Get existing image name — keep it unless a new file is provided
                    string existingImage = "";
                    using (SqlCommand cmd = new SqlCommand(queryOldImage, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemcode", fitemcode);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) existingImage = result.ToString();
                    }

                    string imageName = existingImage;

                    // Handle new image upload
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        var allowedExt = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                        string ext = Path.GetExtension(imageFile.FileName).ToLower();
                        if (!allowedExt.Contains(ext))
                            return BadRequest(new { message = "Unsupported image format. Use jpg, jpeg, png, or webp." });

                        string prefix = faclevelCount <= 2 ? "C" : "S";
                        imageName = $"{prefix}_{fitemcode}{ext}";

                        string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                        if (!Directory.Exists(uploadFolder))
                            Directory.CreateDirectory(uploadFolder);

                        // Delete old image file
                        if (!string.IsNullOrEmpty(existingImage))
                        {
                            string oldPath = Path.Combine(uploadFolder, existingImage);
                            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                        }

                        // Remove any previous file for this code across all extensions
                        foreach (var e in allowedExt)
                        {
                            string old = Path.Combine(uploadFolder, $"{prefix}_{fitemcode}{e}");
                            if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                        }

                        string fullPath = Path.Combine(uploadFolder, imageName);
                        using (var stream = new FileStream(fullPath, FileMode.Create))
                            await imageFile.CopyToAsync(stream);
                    }

                    using (SqlCommand cmd = new SqlCommand(queryUpdate, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemcode", fitemcode);
                        cmd.Parameters.AddWithValue("@fItemName", itemGroupName.Trim().ToUpper());
                        cmd.Parameters.AddWithValue("@fParent",   concatParent);
                        cmd.Parameters.AddWithValue("@fAclevel",  faclevelCount);
                        cmd.Parameters.AddWithValue("@fShow",     availability.Trim());
                        cmd.Parameters.AddWithValue("@fImage",    imageName);

                        int rows = await cmd.ExecuteNonQueryAsync();
                        if (rows > 0)
                        {
                            string imageUrl = string.IsNullOrEmpty(imageName)
                                ? ""
                                : $"{Request.Scheme}://{Request.Host}/uploads/{imageName}";

                            return Ok(new
                            {
                                message  = $"'{itemGroupName}' updated successfully.",
                                imageUrl
                            });
                        }
                        return NotFound(new { message = "Item group not found for update." });
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
        // DELETE – Delete item group + its image file
        // DELETE /api/ItemGroupCreation/ItemGroupDelete/{fitemcode}
        // -------------------------------------------------------

        [HttpDelete("ItemGroupDelete/{fitemcode}")]
        public async Task<IActionResult> ItemGroupDelete([FromRoute] string fitemcode)
        {
            if (string.IsNullOrWhiteSpace(fitemcode))
                return BadRequest(new { message = "fitemcode is required." });

            const string checkChildQuery = "SELECT COUNT(*) FROM item WHERE fParent LIKE @fParentLike";
            const string getImageQuery   = "SELECT fImage  FROM item WHERE fItemcode = @fItemcode";
            const string deleteQuery     = "DELETE FROM item WHERE fItemcode = @fItemcode";

            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();

                    // Block if child items exist
                    using (SqlCommand cmd = new SqlCommand(checkChildQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@fParentLike", "%" + fitemcode + "%");
                        int childCount = (int)await cmd.ExecuteScalarAsync();
                        if (childCount > 1)
                            return Conflict(new
                            {
                                message = "Cannot delete. This item group has child records."
                            });
                    }

                    // Get image filename before deleting the row
                    string imageName = "";
                    using (SqlCommand cmd = new SqlCommand(getImageQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemcode", fitemcode);
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null) imageName = result.ToString();
                    }

                    // Delete DB record
                    using (SqlCommand cmd = new SqlCommand(deleteQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@fItemcode", fitemcode);
                        int rows = await cmd.ExecuteNonQueryAsync();

                        if (rows > 0)
                        {
                            // Delete the image file from disk
                            if (!string.IsNullOrEmpty(imageName))
                            {
                                string uploadFolder = Path.Combine(
                                    Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                                string filePath = Path.Combine(uploadFolder, imageName);
                                if (System.IO.File.Exists(filePath))
                                    System.IO.File.Delete(filePath);
                            }

                            return Ok(new { message = "Item group deleted successfully." });
                        }
                        return NotFound(new { message = "Item group not found." });
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







    }
}

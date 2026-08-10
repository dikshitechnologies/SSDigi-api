using System.Text.Json.Serialization;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [Authorize(Roles = "Guest,User,Admin")]
    [ApiController]
    public class SubCategoryController : ControllerBase
    {
        [HttpGet("subcategory/{categoryCode}")]
        public IActionResult GetSubCategoryItems(
            [FromRoute] string categoryCode,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            List<SubCategoryItem> items = new List<SubCategoryItem>();
            int totalCount = 0;

            string connectionString = DBHelper.GetConnection();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    // Count total records for pagination
                    string countQuery = @"
                SELECT COUNT(*) 
                FROM item i
                WHERE i.fParent LIKE (SELECT fParent FROM item WHERE fitemcode = @categoryCode) + '%'
                  AND i.fAclevel < 0;";

                    using (SqlCommand countCmd = new SqlCommand(countQuery, conn))
                    {
                        countCmd.Parameters.AddWithValue("@categoryCode", categoryCode);
                        totalCount = (int)countCmd.ExecuteScalar();
                    }

                    // Main paginated query
                    string query = @"
                SELECT 
                    i.fItemcode,
                    i.fItemName,
                    COALESCE(
                        (SELECT TOP 1 
                             COALESCE(op.FIMAGE1, op.FIMAGE2, op.FIMAGE3, op.FIMAGE4) 
                         FROM ITEMPURCHASEOP op
                         WHERE op.Itemcode = i.fItemcode
                         ORDER BY op.FDATE DESC),
                        i.fImage
                    ) AS FinalImage,
                    i.FLAG,
                    (SELECT TOP 1 op.FDATE 
                     FROM ITEMPURCHASEOP op
                     WHERE op.Itemcode = i.fItemcode
                     ORDER BY op.FDATE DESC) AS LastPurchaseDate
                FROM item i
                WHERE i.fParent LIKE (SELECT fParent FROM item WHERE fitemcode = @categoryCode) + '%'
                  AND i.fAclevel < 0 and ISNULL(i.FLAG, 'Y') = 'Y'
                ORDER BY i.fItemcode
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@categoryCode", categoryCode);
                        cmd.Parameters.AddWithValue("@Offset", (pageNumber - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                items.Add(new SubCategoryItem
                                {
                                    FCode = reader["fItemcode"].ToString(),
                                    SubCategoryName = reader["fItemName"].ToString(),
                                    Image = reader["FinalImage"]?.ToString(),
                                    ItemFlag = reader["FLAG"] == DBNull.Value ? "Y" : (reader["FLAG"].ToString() == "N" ? "N" : "Y")
                                });
                            }
                        }
                    }

                    return Ok(new
                    {
                        data = items,
                        pagination = new
                        {
                            pageNumber,
                            pageSize,
                            totalRecords = totalCount,
                            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                        }
                    });
                }
                catch (SqlException)
                {
                    return StatusCode(500, new { error = "Database error occurred." });
                }
                catch (Exception)
                {
                    return StatusCode(500, new { error = "Unexpected error occurred." });
                }
            }
        }


        [HttpGet("Getsubcategory/{categoryCode}")]
        public IActionResult GetsSubCategoryItems(
            [FromRoute] string categoryCode,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            List<SubCategoryItem> items = new List<SubCategoryItem>();
            int totalCount = 0;

            string connectionString = DBHelper.GetConnection();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();

                    // Count total records for pagination
                    string countQuery = @"
                SELECT COUNT(*) 
                FROM item i
                WHERE i.fParent LIKE (SELECT fParent FROM item WHERE fitemcode = @categoryCode) + '%'
                  AND i.fAclevel < 0;";

                    using (SqlCommand countCmd = new SqlCommand(countQuery, conn))
                    {
                        countCmd.Parameters.AddWithValue("@categoryCode", categoryCode);
                        totalCount = (int)countCmd.ExecuteScalar();
                    }

                    // Main paginated query
                    string query = @"
                SELECT 
                    i.fItemcode,
                    i.fItemName,
                    COALESCE(
                        (SELECT TOP 1 
                             COALESCE(op.FIMAGE1, op.FIMAGE2, op.FIMAGE3, op.FIMAGE4) 
                         FROM ITEMPURCHASEOP op
                         WHERE op.Itemcode = i.fItemcode
                         ORDER BY op.FDATE DESC),
                        i.fImage
                    ) AS FinalImage,
                    i.FLAG,
                    (SELECT TOP 1 op.FDATE 
                     FROM ITEMPURCHASEOP op
                     WHERE op.Itemcode = i.fItemcode
                     ORDER BY op.FDATE DESC) AS LastPurchaseDate
                FROM item i
                WHERE i.fParent LIKE (SELECT fParent FROM item WHERE fitemcode = @categoryCode) + '%'
                  AND i.fAclevel < 0
                ORDER BY i.fItemcode
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@categoryCode", categoryCode);
                        cmd.Parameters.AddWithValue("@Offset", (pageNumber - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                items.Add(new SubCategoryItem
                                {
                                    FCode = reader["fItemcode"].ToString(),
                                    SubCategoryName = reader["fItemName"].ToString(),
                                    Image = reader["FinalImage"]?.ToString(),
                                    ItemFlag = reader["FLAG"] == DBNull.Value ? "Y" : (reader["FLAG"].ToString() == "N" ? "N" : "Y")
                                });
                            }
                        }
                    }

                    return Ok(new
                    {
                        data = items,
                        pagination = new
                        {
                            pageNumber,
                            pageSize,
                            totalRecords = totalCount,
                            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                        }
                    });
                }
                catch (SqlException)
                {
                    return StatusCode(500, new { error = "Database error occurred." });
                }
                catch (Exception)
                {
                    return StatusCode(500, new { error = "Unexpected error occurred." });
                }
            }
        }




        [HttpPut("UpdateSubCategory")]
        public async Task<IActionResult> UpdateSubCategory([FromForm] SubCategoryRequest model)
        {
            string connectionString = DBHelper.GetConnection();

            using SqlConnection conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            try
            {
                string imageName = null;

                // Upload Image
                if (model.Image != null && model.Image.Length > 0)
                {
                    string uploadFolder = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        "uploads"
                    );

                    if (!Directory.Exists(uploadFolder))
                        Directory.CreateDirectory(uploadFolder);

                    string extension = Path.GetExtension(model.Image.FileName);

                    imageName = $"S_{model.FCode}{extension}";

                    string filePath = Path.Combine(uploadFolder, imageName);

                    using var stream = new FileStream(filePath, FileMode.Create);
                    await model.Image.CopyToAsync(stream);
                }

                string query;

                if (!string.IsNullOrWhiteSpace(imageName))
                {
                    query = @"
                UPDATE Item
                SET
                    fImage = @Image,
                    Flag   = @Flag
                WHERE fItemCode = @Code";
                }
                else
                {
                    query = @"
                UPDATE Item
                SET
                    Flag = @Flag
                WHERE fItemCode = @Code";
                }

                using SqlCommand cmd = new SqlCommand(query, conn);

                cmd.Parameters.AddWithValue("@Code", model.FCode);
                cmd.Parameters.AddWithValue("@Flag",
                    string.IsNullOrWhiteSpace(model.ItemFlag) ? "Y" : model.ItemFlag);

                if (!string.IsNullOrWhiteSpace(imageName))
                {
                    cmd.Parameters.AddWithValue("@Image", imageName);
                }

                int rows = await cmd.ExecuteNonQueryAsync();

                if (rows == 0)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "Sub Category not found."
                    });
                }

                return Ok(new
                {
                    success = true,
                    image = imageName,
                    message = "Sub Category updated successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

    }
}

//[HttpGet("subcategory/{categoryCode}")]
//public IActionResult GetSubCategoryItems(
//  [FromRoute] string categoryCode,
//  [FromQuery] int pageNumber = 1,
//  [FromQuery] int pageSize = 20)
//{
//    List<SubCategoryItem> items = new List<SubCategoryItem>();
//    int totalCount = 0;
//    string parentCode = "";

//    string connectionString = DBHelper.GetConnection();

//    using (SqlConnection conn = new SqlConnection(connectionString))
//    {
//        try
//        {
//            conn.Open();

//            // Get parent code first
//            string parentQuery = "SELECT fParent FROM item WHERE fitemcode = @categoryCode";
//            using (SqlCommand parentCmd = new SqlCommand(parentQuery, conn))
//            {
//                parentCmd.Parameters.AddWithValue("@categoryCode", categoryCode);
//                object result = parentCmd.ExecuteScalar();
//                parentCode = result?.ToString() ?? "";
//            }

//            // Count total records for pagination
//            string countQuery = @"
//                SELECT COUNT(*) 
//                FROM item i
//                WHERE i.fParent LIKE @parentCode + '%'
//                  AND i.fAclevel < 0;";

//            using (SqlCommand countCmd = new SqlCommand(countQuery, conn))
//            {
//                countCmd.Parameters.AddWithValue("@parentCode", parentCode);
//                totalCount = (int)countCmd.ExecuteScalar();
//            }

//            // Main paginated query
//            string query = @"
//                SELECT 
//                    i.fItemcode,
//                    i.fItemName,
//                    COALESCE(
//                        (SELECT TOP 1 
//                             COALESCE(op.FIMAGE1, op.FIMAGE2, op.FIMAGE3, op.FIMAGE4) 
//                         FROM ITEMPURCHASEOP op
//                         WHERE op.Itemcode = i.fItemcode
//                         ORDER BY op.FDATE DESC),
//                        i.fImage
//                    ) AS FinalImage,
//                    (SELECT TOP 1 op.FDATE 
//                     FROM ITEMPURCHASEOP op
//                     WHERE op.Itemcode = i.fItemcode
//                     ORDER BY op.FDATE DESC) AS LastPurchaseDate
//                FROM item i
//                WHERE i.fParent LIKE @parentCode + '%'
//                  AND i.fAclevel < 0
//                ORDER BY i.fItemcode
//                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

//            using (SqlCommand cmd = new SqlCommand(query, conn))
//            {
//                cmd.Parameters.AddWithValue("@parentCode", parentCode);
//                cmd.Parameters.AddWithValue("@Offset", (pageNumber - 1) * pageSize);
//                cmd.Parameters.AddWithValue("@PageSize", pageSize);

//                using (SqlDataReader reader = cmd.ExecuteReader())
//                {
//                    while (reader.Read())
//                    {
//                        items.Add(new SubCategoryItem
//                        {
//                            FCode = reader["fItemcode"].ToString(),
//                            SubCategoryName = reader["fItemName"].ToString(),
//                            Image = reader["FinalImage"]?.ToString(),
//                        });
//                    }
//                }
//            }

//            // Insert "All" at the first position
//            items.Insert(0, new SubCategoryItem
//            {
//                FCode = "",
//                SubCategoryName = "All",
//                Image = "",
//            });

//            return Ok(new
//            {
//                data = items,
//                pagination = new
//                {
//                    pageNumber,
//                    pageSize,
//                    totalRecords = totalCount,
//                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
//                }
//            });
//        }
//        catch (SqlException)
//        {
//            return StatusCode(500, new { error = "Database error occurred." });
//        }
//        catch (Exception)
//        {
//            return StatusCode(500, new { error = "Unexpected error occurred." });
//        }
//    }
//}


public class SubCategoryItem
{
    [JsonPropertyName("code")]
    public string FCode { get; set; }

    [JsonPropertyName("name")]
    public string SubCategoryName { get; set; }

    [JsonPropertyName("image")]
    public string Image { get; set; }
    public string ItemFlag { get; set; }

}

public class SubCategoryRequest
{
    public string FCode { get; set; }

    public string ItemFlag { get; set; } = "Y";

    public IFormFile? Image { get; set; }


}
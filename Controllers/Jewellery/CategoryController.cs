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
    public class CategoryController : ControllerBase
    {

        [HttpGet("categories")]
        public IActionResult GetCategories()
        {
            string connectionString = DBHelper.GetConnection();
            List<CategoryItem> categories = new List<CategoryItem>();


            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "SELECT fItemcode,fparent, fItemName, fimage,flag FROM Item WHERE LEFT(fParent, 5) = '00001' AND fAclevel = 2 and flag ='Y'";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            categories.Add(new CategoryItem
                            {
                                FCode = reader["fItemcode"].ToString(),
                                fparent = reader["fparent"].ToString(),
                                Name = reader["fItemName"].ToString(),
                                Image = reader["fimage"]?.ToString(),
                                ItemFlag = reader["flag"]?.ToString()
                            });
                        }
                    }

                    return Ok(categories);
                }
                catch (SqlException sqlEx)
                {
                    return StatusCode(500, new
                    {
                        error = "A database error occurred."
                    });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new
                    {
                        error = "An unexpected error occurred."
                    });
                }
            }
        }
        [HttpGet("getscategories")]
        public IActionResult getscategories()
        {
            string connectionString = DBHelper.GetConnection();
            List<CategoryItem> categories = new List<CategoryItem>();


            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                try
                {
                    conn.Open();
                    string query = "SELECT fItemcode,fparent, fItemName, fimage,flag FROM Item WHERE LEFT(fParent, 5) = '00001' AND fAclevel = 2 ";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            categories.Add(new CategoryItem
                            {
                                FCode = reader["fItemcode"].ToString(),
                                fparent = reader["fparent"].ToString(),
                                Name = reader["fItemName"].ToString(),
                                Image = reader["fimage"]?.ToString(),
                                ItemFlag = string.IsNullOrWhiteSpace(reader["Flag"]?.ToString())
                                            ? "N"
                                            : reader["Flag"].ToString()
                            });
                        }
                    }

                    return Ok(categories);
                }
                catch (SqlException sqlEx)
                {
                    return StatusCode(500, new
                    {
                        error = "A database error occurred."
                    });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new
                    {
                        error = "An unexpected error occurred."
                    });
                }
            }
        }


        [HttpPut("UpdateCategory")]
        public async Task<IActionResult> UpdateCategory([FromForm] CategoryRequest model)
        {
            string connectionString = DBHelper.GetConnection();

            using SqlConnection conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            try
            {
                string imageName = null;

                // Save Image
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
                    imageName = $"C_{model.FCode}{extension}";

                    string filePath = Path.Combine(uploadFolder, imageName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.Image.CopyToAsync(stream);
                    }
                }

                string query;

                // If image uploaded, update image + status
                if (!string.IsNullOrWhiteSpace(imageName))
                {
                    query = @"
                        UPDATE Item
                        SET
                            fImage = @Image,
                            Flag   = @Flag
                        WHERE fItemCode = @Code";
                }
                // Otherwise update only status
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
                        message = "Category not found."
                    });
                }

                return Ok(new
                {
                    success = true,
                    image = imageName,
                    message = "Category updated successfully."
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



public class CategoryItem
{
    [JsonPropertyName("code")]
    public string FCode { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("fparent")]
    public string fparent { get; set; }

    [JsonPropertyName("image")]
    public string Image { get; set; }

    [JsonPropertyName("itemFlag")]
    public string ItemFlag { get; set; }
}

public class CategoryRequest
{
    public string FCode { get; set; }

    public string ItemFlag { get; set; } = "Y";

    public IFormFile? Image { get; set; }
}


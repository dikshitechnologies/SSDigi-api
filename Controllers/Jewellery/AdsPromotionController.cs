using JEWELLBISREACT.DBConnection;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdsPromotionController : ControllerBase
    {



        [HttpGet("promotionsData")]
        public async Task<IActionResult> GetPromotions()
        {
            List<Promotion> promotions = new List<Promotion>();

            string query = "SELECT TOP 1000 FPROMONAME, FIMAGE, FREMARK FROM ADPROMOTION";

            try
            {
                await using SqlConnection conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                await using SqlCommand cmd = new(query, conn);
                await using SqlDataReader reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    promotions.Add(new Promotion
                    {
                        Name = reader["FPROMONAME"]?.ToString(),
                        ImageUrl = reader["FIMAGE"]?.ToString(),
                        Remark = reader["FREMARK"]?.ToString(),

                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "error", message = $"Error: {ex.Message}" });
            }

            return Ok(promotions);
        }
        [HttpGet("GetPromotionsData")]
        public async Task<IActionResult> GetPromotionsData()
        {
            List<Promotion> promotions = new List<Promotion>();

            string query = "SELECT FPROMONAME,FIMAGE,FREMARK,FDATE,FCODE FROM ADPROMOTION";

            try
            {
                await using SqlConnection conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                await using SqlCommand cmd = new(query, conn);
                await using SqlDataReader reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    promotions.Add(new Promotion
                    {
                        Name = reader["FPROMONAME"]?.ToString(),
                        ImageUrl = reader["FIMAGE"]?.ToString(),
                        Remark = reader["FREMARK"]?.ToString(),
                        Date = reader["FDATE"]?.ToString(),
                        FCODE = reader["FCODE"]?.ToString()

                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "error", message = $"Error: {ex.Message}" });
            }

            return Ok(promotions);
        }


        //[HttpPost("PostpromotionsData")]
        //public async Task<IActionResult> SavePromotion([FromBody] Promotion promotion)
        //{
        //    if (promotion == null || string.IsNullOrEmpty(promotion.ImageUrl))
        //    {
        //        return BadRequest(new { status = "error", message = "Invalid promotion data." });
        //    }

        //    string safeFileName = "";
        //    try
        //    {

        //        if (!promotion.ImageUrl.Contains(","))
        //        {
        //            return BadRequest(new { status = "error", message = "Invalid or missing image data." });
        //        }

        //        string base64String = promotion.ImageUrl.Split(',')[1].Trim();
        //        byte[] imageBytes;
        //        try
        //        {
        //            imageBytes = Convert.FromBase64String(base64String);
        //        }
        //        catch (FormatException)
        //        {
        //            return BadRequest(new { status = "error", message = "Invalid Base64 image format." });
        //        }

        //        string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        //        string safeAdsName = Regex.Replace(promotion.Name, @"[^a-zA-Z0-9_-]", "_").Trim().ToLower();
        //        safeAdsName = safeAdsName.Length > 50 ? safeAdsName.Substring(0, 50) : safeAdsName;

        //        safeFileName = $"AD_{safeAdsName}_{timestamp}.jpg";

        //        bool isSaved = await ImageHelper.SaveBase64ImageAsync(base64String, safeFileName);
        //        if (!isSaved)
        //        {
        //            return BadRequest(new { status = "error", message = "Failed to save image locally." });
        //        }


        //        string query = "INSERT INTO ADPROMOTION (FPROMONAME, FIMAGE, FREMARK, FDATE) VALUES (@Name, @ImageName, @Remark, @Date)";

        //        await using SqlConnection conn = new(DBHelper.GetConnection());
        //        await conn.OpenAsync();

        //        await using SqlCommand cmd = new(query, conn);
        //        cmd.Parameters.AddWithValue("@Name", promotion.Name);
        //        cmd.Parameters.AddWithValue("@ImageName", safeFileName);
        //        cmd.Parameters.AddWithValue("@Remark", promotion.Remark ?? (object)DBNull.Value);
        //        cmd.Parameters.AddWithValue("@Date", DateTime.UtcNow);

        //        int rowsAffected = await cmd.ExecuteNonQueryAsync();

        //        if (rowsAffected > 0)
        //        {
        //            return Ok(new { status = "success", message = "Promotion added successfully.", imageName = safeFileName });
        //        }
        //        else
        //        {

        //            ImageHelper.DeleteImage(safeFileName);
        //            return BadRequest(new { status = "error", message = "Failed to insert promotion." });
        //        }
        //    }
        //    catch (Exception ex)
        //    {

        //        if (!string.IsNullOrEmpty(safeFileName))
        //        {
        //            ImageHelper.DeleteImage(safeFileName);
        //        }

        //        Console.WriteLine($"Error in SavePromotion: {ex.Message}");
        //        return StatusCode(500, new { status = "error", message = $"Internal Server Error: {ex.Message}" });
        //    }
        //}

        //[HttpPost("PostpromotionsData")]
        //public async Task<IActionResult> SavePromotion([FromBody] Promotion promotion)
        //{
        //    if (promotion == null || string.IsNullOrEmpty(promotion.ImageUrl))
        //    {
        //        return BadRequest(new { status = "error", message = "Invalid promotion data." });
        //    }

        //    string safeFileName = "";
        //    try
        //    {
        //        // ✅ Extract and validate Base64 image
        //        if (!promotion.ImageUrl.Contains(","))
        //        {
        //            return BadRequest(new { status = "error", message = "Invalid or missing image data." });
        //        }

        //        string base64String = promotion.ImageUrl.Split(',')[1].Trim();
        //        byte[] imageBytes;
        //        try
        //        {
        //            imageBytes = Convert.FromBase64String(base64String);
        //        }
        //        catch (FormatException)
        //        {
        //            return BadRequest(new { status = "error", message = "Invalid Base64 image format." });
        //        }

        //        // ✅ Generate a safe filename with timestamp
        //        string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        //        string safeAdsName = Regex.Replace(promotion.Name, @"[^a-zA-Z0-9_-]", "_").Trim().ToLower();
        //        safeAdsName = safeAdsName.Length > 50 ? safeAdsName.Substring(0, 50) : safeAdsName; // Limit length

        //        safeFileName = $"AD_{safeAdsName}_{timestamp}.jpg";

        //        // ✅ Upload image to web service
        //        var serviceHelper = new DikshiServiceHelper();
        //        string uploadResult = await serviceHelper.UploadImageAsync(imageBytes, safeFileName);

        //        if (uploadResult != "Success")
        //        {

        //            return BadRequest(new { status = "error", message = "Failed to upload image to web service." });
        //        }

        //        // ✅ Insert into database
        //        string query = "INSERT INTO ADPROMOTION (FPROMONAME, FIMAGE, FREMARK, FDATE) VALUES (@Name, @ImageName, @Remark, @Date)";

        //        await using SqlConnection conn = new(DBHelper.GetConnection());
        //        await conn.OpenAsync();

        //        await using SqlCommand cmd = new(query, conn);
        //        cmd.Parameters.AddWithValue("@Name", promotion.Name);
        //        cmd.Parameters.AddWithValue("@ImageName", safeFileName);
        //        cmd.Parameters.AddWithValue("@Remark", promotion.Remark ?? (object)DBNull.Value);
        //        cmd.Parameters.AddWithValue("@Date", DateTime.UtcNow);

        //        int rowsAffected = await cmd.ExecuteNonQueryAsync();

        //        if (rowsAffected > 0)
        //        {
        //            return Ok(new { status = "success", message = "Promotion added successfully.", imageName = safeFileName });
        //        }
        //        else
        //        {
        //            // ❌ If DB insert fails, delete uploaded image
        //            await serviceHelper.DeleteImageAsync(safeFileName);
        //            return BadRequest(new { status = "error", message = "Failed to insert promotion." });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        // ❌ Rollback: Delete image if something fails
        //        if (!string.IsNullOrEmpty(safeFileName))
        //        {
        //            try
        //            {
        //                var serviceHelper = new DikshiServiceHelper();
        //                await serviceHelper.DeleteImageAsync(safeFileName);
        //            }
        //            catch (Exception imgEx)
        //            {
        //                LogHelper.LogError("Failed to delete uploaded image", imgEx);
        //            }
        //        }

        //        LogHelper.LogError("Error in SavePromotion: " + ex.Message, ex);
        //        return StatusCode(500, new { status = "error", message = $"Internal Server Error: {ex.Message}" });
        //    }
        //}


        [HttpDelete("delete")]
        public async Task<IActionResult> DeletePromotion([FromQuery] string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return BadRequest(new { status = "error", message = "Invalid promotion name." });
            }

            try
            {
                string query = "DELETE FROM ADPROMOTION WHERE FPROMONAME = @Name";

                await using SqlConnection conn = new(DBHelper.GetConnection());
                await conn.OpenAsync();

                await using SqlCommand cmd = new(query, conn);
                cmd.Parameters.AddWithValue("@Name", name);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return Ok(new { status = "success", message = "Promotion deleted successfully." });
                }
                else
                {
                    return NotFound(new { status = "error", message = "Promotion not found." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "error", message = $"Error: {ex.Message}" });
            }
        }



        public class Promotion
        {
            public string Name { get; set; }
            public string ImageUrl { get; set; }
            public string Remark { get; set; }
            public string Date { get; set; }
            public string FCODE { get; set; }


        }
    }
}

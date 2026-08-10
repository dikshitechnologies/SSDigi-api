using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.New_Update
{
    [Route("api/[controller]")]
    [ApiController]
    public class OfferImgController : ControllerBase
    {
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        // -------------------------------------------------------
        // POST api/OfferImg/upload
        // Inserts a new offer image row, auto-assigns next Id,
        // saves file as wwwroot/offerimg/offer_<id>.<ext>
        // -------------------------------------------------------
        [HttpPost("upload")]
        public async Task<IActionResult> UploadOfferImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Image file is required." });

            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!AllowedExtensions.Contains(extension))
                return BadRequest(new { success = false, message = "Unsupported file type. Allowed: jpg, jpeg, png, webp." });

            if (file.Length > 20 * 1024 * 1024)
                return BadRequest(new { success = false, message = "File too large (limit: 20 MB)." });

            try
            {
                string offerFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "offerimg");
                if (!Directory.Exists(offerFolder))
                    Directory.CreateDirectory(offerFolder);

                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Insert a placeholder first to get the identity Id, then rename the file
                var insertCmd = new SqlCommand(@"
                    INSERT INTO OfferImages (ImageName, IsActive, IsMainImg, CreatedDate)
                    VALUES (@ImageName, 0, 0, @CreatedDate);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    connection);

                // Temporary name — will be updated once we know the Id
                insertCmd.Parameters.AddWithValue("@ImageName", "pending");
                insertCmd.Parameters.AddWithValue("@CreatedDate", DateTime.Now);

                int newId = (int)await insertCmd.ExecuteScalarAsync();

                string fileName = $"offer_{newId}{extension}";
                string fullPath = Path.Combine(offerFolder, fileName);

                using (var stream = new FileStream(fullPath, FileMode.Create))
                    await file.CopyToAsync(stream);

                // Update row with the real file name
                var updateCmd = new SqlCommand(
                    "UPDATE OfferImages SET ImageName = @ImageName WHERE Id = @Id",
                    connection);
                updateCmd.Parameters.AddWithValue("@ImageName", fileName);
                updateCmd.Parameters.AddWithValue("@Id", newId);
                await updateCmd.ExecuteNonQueryAsync();

                string imageUrl = $"{Request.Scheme}://{Request.Host}/offerimg/{fileName}";

                return Ok(new
                {
                    success = true,
                    message = "Offer image uploaded successfully.",
                    id      = newId,
                    fileName,
                    imageUrl
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET api/OfferImg/get-all
        // Returns all offer images with their URLs.
        // -------------------------------------------------------
        [HttpGet("get-all")]
        public async Task<IActionResult> GetAllOfferImages()
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    SELECT Id, ImageName, IsActive, IsMainImg, CreatedDate
                    FROM OfferImages
                    ORDER BY Id DESC",
                    connection);

                using var reader = await cmd.ExecuteReaderAsync();

                var list = new List<object>();
                while (await reader.ReadAsync())
                {
                    int    id          = Convert.ToInt32(reader["Id"]);
                    string imageName   = reader["ImageName"].ToString();
                    bool   isActive    = Convert.ToBoolean(reader["IsActive"]);
                    bool   isMainImg   = Convert.ToBoolean(reader["IsMainImg"]);
                    string createdDate = Convert.ToDateTime(reader["CreatedDate"])
                                                 .ToString("yyyy-MM-dd HH:mm:ss");
                    string imageUrl    = $"{Request.Scheme}://{Request.Host}/offerimg/{imageName}";

                    list.Add(new { id, imageName, isActive, isMainImg, createdDate, imageUrl });
                }

                return Ok(new { success = true, data = list });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // GET api/OfferImg/get-active
        // Returns only active offer images (for app display).
        // -------------------------------------------------------
        [HttpGet("get-active")]
        public async Task<IActionResult> GetActiveOfferImages()
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    SELECT Id, ImageName, IsActive, IsMainImg, CreatedDate
                    FROM OfferImages
                    WHERE IsActive = 1
                    ORDER BY Id DESC",
                    connection);

                using var reader = await cmd.ExecuteReaderAsync();

                var list = new List<object>();
                while (await reader.ReadAsync())
                {
                    int    id          = Convert.ToInt32(reader["Id"]);
                    string imageName   = reader["ImageName"].ToString();
                    bool   isActive    = Convert.ToBoolean(reader["IsActive"]);
                    bool   isMainImg   = Convert.ToBoolean(reader["IsMainImg"]);
                    string createdDate = Convert.ToDateTime(reader["CreatedDate"])
                                                 .ToString("yyyy-MM-dd HH:mm:ss");
                    string imageUrl    = $"{Request.Scheme}://{Request.Host}/offerimg/{imageName}";

                    list.Add(new { id, imageName, isActive, isMainImg, createdDate, imageUrl });
                }

                return Ok(new { success = true, data = list });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // PUT api/OfferImg/toggle-active/{id}
        // Flips the IsActive flag for a specific offer by Id.
        // -------------------------------------------------------
        [HttpPut("toggle-active/{id}")]
        public async Task<IActionResult> ToggleActive(int id)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    UPDATE OfferImages
                       SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END
                     WHERE Id = @Id;

                    SELECT IsActive FROM OfferImages WHERE Id = @Id;",
                    connection);

                cmd.Parameters.AddWithValue("@Id", id);

                var result = await cmd.ExecuteScalarAsync();

                if (result == null)
                    return NotFound(new { success = false, message = $"Offer image with Id {id} not found." });

                bool isActive = Convert.ToBoolean(result);

                return Ok(new
                {
                    success  = true,
                    id,
                    message  = isActive ? "Offer image is now active." : "Offer image is now inactive.",
                    isActive
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }

        // -------------------------------------------------------
        // DELETE api/OfferImg/delete/{id}
        // Removes the physical file and deletes the DB row by Id.
        // -------------------------------------------------------
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteOfferImage(int id)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var selectCmd = new SqlCommand(
                    "SELECT ImageName FROM OfferImages WHERE Id = @Id", connection);
                selectCmd.Parameters.AddWithValue("@Id", id);

                var imgNameObj = await selectCmd.ExecuteScalarAsync();
                if (imgNameObj == null)
                    return NotFound(new { success = false, message = $"Offer image with Id {id} not found." });

                string imageName   = imgNameObj.ToString();
                string offerFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "offerimg");
                string filePath    = Path.Combine(offerFolder, imageName);

                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);

                var deleteCmd = new SqlCommand(
                    "DELETE FROM OfferImages WHERE Id = @Id", connection);
                deleteCmd.Parameters.AddWithValue("@Id", id);
                await deleteCmd.ExecuteNonQueryAsync();

                return Ok(new { success = true, message = $"Offer image {id} deleted successfully." });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }
        // -------------------------------------------------------
        // PUT api/OfferImg/set-main/{id}
        // Sets IsMainImg = 1 for the given Id and resets all
        // other rows to IsMainImg = 0.
        // -------------------------------------------------------
        [HttpPut("set-main/{id}")]
        public async Task<IActionResult> SetMainImage(int id)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // First verify the row exists
                var checkCmd = new SqlCommand(
                    "SELECT COUNT(1) FROM OfferImages WHERE Id = @Id", connection);
                checkCmd.Parameters.AddWithValue("@Id", id);
                int count = (int)await checkCmd.ExecuteScalarAsync();

                if (count == 0)
                    return NotFound(new { success = false, message = $"Offer image with Id {id} not found." });

                // Reset all to 0, then set the chosen one to 1
                var updateCmd = new SqlCommand(@"
                    UPDATE OfferImages SET IsMainImg = 0;
                    UPDATE OfferImages SET IsMainImg = 1 WHERE Id = @Id;",
                    connection);
                updateCmd.Parameters.AddWithValue("@Id", id);
                await updateCmd.ExecuteNonQueryAsync();

                return Ok(new
                {
                    success    = true,
                    id,
                    message    = $"Offer image {id} is now set as the main image.",
                    isMainImg  = true
                });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { success = false, message = "Database error.", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Server error.", error = ex.Message });
            }
        }
    }
}

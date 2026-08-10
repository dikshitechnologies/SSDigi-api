using CHITSCHEME.Helpers;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class ProfileController : ControllerBase
    {
        [HttpPut("update-user-profile")]
        public async Task<IActionResult> UpdateUserProfile([FromBody] UpdateUserDto dto)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    string updateQuery = @"
                UPDATE party
                SET 
                    fStreet = @AddressLine,
                    fCity = @City,
                    farea = @State,
                    fPincode = @Pincode
                WHERE fcode = @UserID";

                    using (SqlCommand cmd = new SqlCommand(updateQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@AddressLine", dto.AddressLine ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@City", dto.City ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@State", dto.State ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Pincode", dto.Pincode ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@UserID", dto.UserID); 

                        await conn.OpenAsync();
                        int rows = await cmd.ExecuteNonQueryAsync();

                        if (rows == 0)
                            return NotFound(new { success = false, message = "User not found." });

                        return Ok(new { success = true, message = "Profile updated successfully." });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Server error while updating profile.",
                    error = ex.Message
                });
            }
        }



        [HttpPost("upload-profile-image/{userId}")]
        public async Task<IActionResult> UploadProfileImage(int userId, IFormFile file)
        {
            SqlConnection conn = null;
            SqlTransaction transaction = null;

            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("Image file is required.");

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var extension = Path.GetExtension(file.FileName).ToLower();

                if (!allowedExtensions.Contains(extension))
                    return BadRequest("Unsupported file type.");

                if (file.Length > 20 * 1024 * 1024)
                    return BadRequest("File too large (limit: 20MB).");

                string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadFolder))
                    Directory.CreateDirectory(uploadFolder);

                // Delete old files with any format
                foreach (var ext in allowedExtensions)
                {
                    string oldPath = Path.Combine(uploadFolder, $"profile_{userId}{ext}");
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                string fileName = $"profile_{userId}{extension}";
                string fullPath = Path.Combine(uploadFolder, fileName);

                // Save image file to disk
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Begin SQL transaction
                conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();
                transaction = conn.BeginTransaction();

                string updateQuery = "UPDATE party SET FImage = @fProfileImg WHERE fCode = @UserID";

                using (SqlCommand cmd = new SqlCommand(updateQuery, conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@fProfileImg", fileName);
                    cmd.Parameters.AddWithValue("@UserID", userId);

                    int rows = await cmd.ExecuteNonQueryAsync();

                    if (rows == 0)
                    {
                        // Delete image file if DB update fails
                        if (System.IO.File.Exists(fullPath))
                            System.IO.File.Delete(fullPath);

                        return NotFound(new { success = false, message = "User not found." });
                    }
                }

                // All good: commit DB
                transaction.Commit();

                return Ok(new
                {
                    success = true,
                    fileName,
                    message = "Profile image uploaded and saved successfully."
                });
            }
            catch (Exception ex)
            {
                transaction?.Rollback();

                // Delete the image file if created
                if (!string.IsNullOrEmpty(file?.FileName))
                {
                    string ext = Path.GetExtension(file.FileName).ToLower();
                    string imagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", $"profile_{userId}{ext}");
                    if (System.IO.File.Exists(imagePath))
                        System.IO.File.Delete(imagePath);
                }

                return StatusCode(500, new
                {
                    success = false,
                    message = "Error uploading profile image.",
                    error = ex.Message
                });
            }
            finally
            {
                conn?.Close();
            }
        }


        [HttpDelete("AccountDelete{userid}")]
        public async Task<IActionResult> DeleteUser([FromHeader] string authorization, string userid)
        {
            if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(new { message = "Authorization header is missing or invalid." });
            }

            var token = authorization.Substring("Bearer ".Length).Trim();
            var phone = JwtHelper.GetPhoneFromJwtToken(token);

            if (string.IsNullOrEmpty(phone))
            {
                return Unauthorized(new { message = "Invalid token." });
            }

            if (string.IsNullOrWhiteSpace(userid))
                return BadRequest(new { message = "UserId is required." });

            try
            {
                string connectionString = DBHelper.GetConnection();

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    string checkQuery = @"
                SELECT TOP 1 1
                FROM PARTY P
                LEFT JOIN PARTY PARENT 
                    ON PARENT.FPARENT = LEFT(P.FPARENT, LEN(P.FPARENT) - 5)
                WHERE P.FPHONE = @phone
                  AND P.FPARENT LIKE '0000100044%'";

                    using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@phone", phone);

                        var exists = await checkCmd.ExecuteScalarAsync();

                        if (exists != null)
                        {
                            return BadRequest(new { message = "Account cannot be deleted because it is linked to an active scheme." });
                        }
                    }

                    using (SqlTransaction transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            string deleteCartQuery = "DELETE FROM cartlist WHERE fCusid = @userid";
                            using (SqlCommand cmd = new SqlCommand(deleteCartQuery, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@userid", userid);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            string deleteWishlistQuery = "DELETE FROM Wishlist WHERE fCusCode = @userid";
                            using (SqlCommand cmd = new SqlCommand(deleteWishlistQuery, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@userid", userid);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            string deleteUserQuery = "DELETE FROM party WHERE fcode = @userid";
                            int rowsAffected;
                            using (SqlCommand cmd = new SqlCommand(deleteUserQuery, conn, transaction))
                            {
                                cmd.Parameters.AddWithValue("@userid", userid);
                                rowsAffected = await cmd.ExecuteNonQueryAsync();
                            }

                            if (rowsAffected > 0)
                            {

                                transaction.Commit();
                                return Ok(new { message = "Your account Has been Deleted Success!.." });
                            }
                            else
                            {
                                transaction.Rollback();
                                return NotFound(new { message = "User not found." });
                            }
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            return StatusCode(500, new { message = "Error while deleting user.", error = ex.Message });
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { message = "Database error.", error = ex.Message });
            }
        }

    }
}

public class UpdateUserDto
{
    [Required]
    public int UserID { get; set; } 
    public string AddressLine { get; set; }
    public string City { get; set; }
    public string State { get; set; }
    public string Pincode { get; set; }
}

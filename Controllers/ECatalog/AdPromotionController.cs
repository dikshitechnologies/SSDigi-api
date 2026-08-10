using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [Authorize(Roles = "Guest,User,Admin")]
    [ApiController]
    public class AdPromotionController : ControllerBase
    {

        [HttpGet("GetNextCode")]
        public async Task<IActionResult> GetNextCode()
        {
            string connectionString = DBHelper.GetConnection();

            using SqlConnection conn = new SqlConnection(connectionString);

            try
            {
                await conn.OpenAsync();

                string query = "SELECT ISNULL(MAX(FCODE), '000000') FROM ADPROMOTION";

                using SqlCommand cmd = new SqlCommand(query, conn);

                object result = await cmd.ExecuteScalarAsync();

                string maxCode = result?.ToString() ?? "000000";

                int nextNumber = int.Parse(maxCode) + 1;

                string nextCode = nextNumber.ToString("D6");

                return Ok(new
                {
                    success = true,
                    code = nextCode
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

        [HttpPost("SaveAdPromotion/{mode}")]
        public async Task<IActionResult> SaveAdPromotion(bool mode, [FromForm] AdPromotionModel model)
        {
            string connectionString = DBHelper.GetConnection();

            using SqlConnection conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            SqlTransaction transaction = conn.BeginTransaction();

            try
            {
                string imageName = null;

                // Save Image
                if (model.Image != null && model.Image.Length > 0)
                {
                    string uploadPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        "uploads"
                    );

                    if (!Directory.Exists(uploadPath))
                        Directory.CreateDirectory(uploadPath);

                    string extension = Path.GetExtension(model.Image.FileName);

                    imageName = $"B_{model.FCode}_1{extension}";

                    string filePath = Path.Combine(uploadPath, imageName);

                    using FileStream stream = new FileStream(filePath, FileMode.Create);
                    await model.Image.CopyToAsync(stream);
                }

                // Update
                if (!mode)
                {
                    // Delete old image
                    string oldImage = "";

                    using (SqlCommand cmd = new SqlCommand(
                        "SELECT FIMAGE FROM ADPROMOTION WHERE FCODE=@Code",
                        conn,
                        transaction))
                    {
                        cmd.Parameters.AddWithValue("@Code", model.FCode);

                        var result = await cmd.ExecuteScalarAsync();

                        if (result != null)
                            oldImage = result.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(oldImage))
                    {
                        string oldPath = Path.Combine(
                            Directory.GetCurrentDirectory(),
                            "wwwroot",
                            "uploads",                            
                            oldImage);

                        if (System.IO.File.Exists(oldPath))
                            System.IO.File.Delete(oldPath);
                    }

                    using SqlCommand deleteCmd = new SqlCommand(
                        "DELETE FROM ADPROMOTION WHERE FCODE=@Code",
                        conn,
                        transaction);

                    deleteCmd.Parameters.AddWithValue("@Code", model.FCode);

                    await deleteCmd.ExecuteNonQueryAsync();
                }

                string query = @"
                INSERT INTO ADPROMOTION
                (
                    FPROMONAME,
                    FIMAGE,
                    FREMARK,
                    FDATE,
                    FCODE
                )
                VALUES
                (
                    @Name,
                    @Image,
                    @Remark,
                    @Date,
                    @Code
                )";

                using SqlCommand insertCmd = new SqlCommand(query, conn, transaction);

                insertCmd.Parameters.AddWithValue("@Name", model.PromoName);
                insertCmd.Parameters.AddWithValue("@Image", (object?)imageName ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@Remark", model.Remark ?? "");
                insertCmd.Parameters.AddWithValue("@Date", model.Date);
                insertCmd.Parameters.AddWithValue("@Code", model.FCode);

                await insertCmd.ExecuteNonQueryAsync();

                transaction.Commit();

                return Ok(new
                {
                    success = true,
                    message = mode
                        ? "Promotion created successfully."
                        : "Promotion updated successfully."
                });
            }
            catch (Exception ex)
            {
                transaction.Rollback();

                return StatusCode(500, new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        // Delete Promotion
        [HttpDelete("Delete/{code}")]
        public async Task<IActionResult> Delete(string code)
        {
            string connectionString = DBHelper.GetConnection();

            using SqlConnection conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            try
            {
                string imageName = "";

                using (SqlCommand cmd = new SqlCommand(
                    "SELECT FIMAGE FROM ADPROMOTION WHERE FCODE=@Code",
                    conn))
                {
                    cmd.Parameters.AddWithValue("@Code", code);

                    var result = await cmd.ExecuteScalarAsync();

                    if (result != null)
                        imageName = result.ToString();
                }

                if (!string.IsNullOrWhiteSpace(imageName))
                {
                    string path = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        "uploads",                       
                        imageName);

                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }

                using SqlCommand deleteCmd = new SqlCommand(
                    "DELETE FROM ADPROMOTION WHERE FCODE=@Code",
                    conn);

                deleteCmd.Parameters.AddWithValue("@Code", code);

                await deleteCmd.ExecuteNonQueryAsync();

                return Ok(new
                {
                    success = true,
                    message = "Promotion deleted successfully."
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
    public class AdPromotionModel
    {
        public string FCode { get; set; }

        public string PromoName { get; set; }

        public string Remark { get; set; }

        public DateTime Date { get; set; }

        public IFormFile? Image { get; set; }
    }
}
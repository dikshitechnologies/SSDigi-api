using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace CHITSCHEME.Controllers.Jewellery
{

    [Route("api/[controller]")]
    [ApiController]
    public class UploadController : ControllerBase
    {
        [HttpPost("upload-image")]
        public async Task<IActionResult> UploadLargeImage(IFormFile file)
        {
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

                string baseFileName = Path.GetFileNameWithoutExtension(file.FileName);
                string fileName = $"{baseFileName}{extension}";

                string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadFolder))
                    Directory.CreateDirectory(uploadFolder);

                foreach (var ext in allowedExtensions)
                {
                    string existingFile = Path.Combine(uploadFolder, $"{baseFileName}{ext}");
                    if (System.IO.File.Exists(existingFile))
                    {
                        System.IO.File.Delete(existingFile);
                    }
                }

                string fullPath = Path.Combine(uploadFolder, fileName);
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                string imageUrl = $"{Request.Scheme}://{Request.Host}/uploads/{fileName}";
                return Ok(new { success = true, message = "Image uploaded successfully.", imageUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Image not saved due to server error.",
                    error = ex.Message
                });
            }
        }

        [HttpDelete("delete-image/{fileName}")]
        public IActionResult DeleteImage(string fileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return BadRequest("Image file name is required.");

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var extension = Path.GetExtension(fileName).ToLower();

                if (!allowedExtensions.Contains(extension))
                    return BadRequest("Unsupported file type.");

                string uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                string filePath = Path.Combine(uploadFolder, fileName);

                if (!System.IO.File.Exists(filePath))
                    return NotFound("Image file not found.");

                System.IO.File.Delete(filePath);

                return Ok(new { success = true, message = "Image deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Image could not be deleted due to a server error.",
                    error = ex.Message
                });
            }
        }

    }
}

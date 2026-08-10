using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace CHITSCHEME.Controllers.New_Update
{
    [Route("api/[controller]")]
    [ApiController]
    public class PromotionalVideosController : ControllerBase
    {
        private static readonly HttpClient _http = new();

        // -------------------------------------------------------
        // POST api/PromotionalVideos/add
        // Accepts multipart/form-data:
        //   VideoUrl      (required)  – video link
        //   Title         (optional)  – overrides auto-extracted title
        //   DisplayOrder  (optional)
        //   Thumbnail     (optional)  – IFormFile; if supplied it takes
        //                              priority over the auto-extracted thumb
        // -------------------------------------------------------
        [HttpPost("add")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AddVideo([FromForm] AddVideoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.VideoUrl))
                return BadRequest(new { success = false, message = "VideoUrl is required." });

            // Validate uploaded file (if any)
            if (request.Thumbnail is not null)
            {
                var allowed = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
                if (!allowed.Contains(request.Thumbnail.ContentType.ToLower()))
                    return BadRequest(new { success = false, message = "Thumbnail must be a JPEG, PNG, WEBP, or GIF image." });

                if (request.Thumbnail.Length > 5 * 1024 * 1024)
                    return BadRequest(new { success = false, message = "Thumbnail file size must not exceed 5 MB." });
            }

            try
            {
                // Auto-extract metadata from the video URL
                var meta = await ExtractVideoMetaAsync(request.VideoUrl.Trim());

                string title     = request.Title ?? meta.Title ?? string.Empty;
                string videoType = meta.VideoType;

                // ── Step 1: Insert row (thumbnail resolved after we have the ID) ──
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var insertCmd = new SqlCommand(@"
                    INSERT INTO PromotionalVideos
                        (Title, VideoType, VideoUrl, ThumbnailImage, DisplayOrder, IsActive, CreatedDate)
                    VALUES
                        (@Title, @VideoType, @VideoUrl, NULL, @DisplayOrder, 1, @CreatedDate);
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    connection);

                insertCmd.Parameters.AddWithValue("@Title",       (object?)title       ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@VideoType",    videoType);
                insertCmd.Parameters.AddWithValue("@VideoUrl",     request.VideoUrl.Trim());
                insertCmd.Parameters.AddWithValue("@DisplayOrder", request.DisplayOrder > 0 ? request.DisplayOrder : 1);
                insertCmd.Parameters.AddWithValue("@CreatedDate",  DateTime.Now);

                int newId = (int)(await insertCmd.ExecuteScalarAsync())!;

                // ── Step 2: Save thumbnail ────────────────────────────────────────
                string? thumbnailFileName = null;
                string thumbFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "thumbnails");

                if (request.Thumbnail is not null && request.Thumbnail.Length > 0)
                {
                    // ── Priority: use the uploaded file ───────────────────────────
                    try
                    {
                        if (!Directory.Exists(thumbFolder))
                            Directory.CreateDirectory(thumbFolder);

                        string ext = Path.GetExtension(request.Thumbnail.FileName).ToLower();
                        if (string.IsNullOrEmpty(ext)) ext = ".jpg";

                        thumbnailFileName = $"thumbnail_{newId}{ext}";
                        string thumbPath  = Path.Combine(thumbFolder, thumbnailFileName);

                        using var stream = new FileStream(thumbPath, FileMode.Create, FileAccess.Write);
                        await request.Thumbnail.CopyToAsync(stream);
                    }
                    catch
                    {
                        thumbnailFileName = null;
                    }
                }
                else if (!string.IsNullOrEmpty(meta.ThumbnailUrl))
                {
                    // ── Fallback: download auto-extracted thumbnail ────────────────
                    try
                    {
                        if (!Directory.Exists(thumbFolder))
                            Directory.CreateDirectory(thumbFolder);

                        thumbnailFileName = $"thumbnail_{newId}.jpg";
                        string thumbPath  = Path.Combine(thumbFolder, thumbnailFileName);

                        byte[] imgBytes = await _http.GetByteArrayAsync(meta.ThumbnailUrl);
                        await System.IO.File.WriteAllBytesAsync(thumbPath, imgBytes);
                    }
                    catch
                    {
                        thumbnailFileName = null;
                    }
                }

                // ── Step 3: Update row with final thumbnail & resolved title ─────
                var updateCmd = new SqlCommand(@"
                    UPDATE PromotionalVideos
                       SET ThumbnailImage = @ThumbnailImage,
                           Title          = @Title
                     WHERE Id = @Id",
                    connection);

                updateCmd.Parameters.AddWithValue("@ThumbnailImage", (object?)thumbnailFileName ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Title",          (object?)title             ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Id",             newId);
                await updateCmd.ExecuteNonQueryAsync();

                string? thumbnailUrl = thumbnailFileName is not null
                    ? $"{Request.Scheme}://{Request.Host}/thumbnails/{thumbnailFileName}"
                    : null;

                return Ok(new
                {
                    success        = true,
                    message        = "Video added successfully.",
                    id             = newId,
                    title,
                    videoType,
                    videoUrl       = request.VideoUrl.Trim(),
                    thumbnailImage = thumbnailFileName,
                    thumbnailUrl,
                    thumbnailSource = request.Thumbnail is not null ? "uploaded" : (thumbnailFileName is not null ? "auto" : "none")
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
        // GET api/PromotionalVideos/get-all
        // -------------------------------------------------------
        [HttpGet("get-all")]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    SELECT Id, Title, VideoType, VideoUrl, ThumbnailImage, DisplayOrder, IsActive, CreatedDate
                    FROM PromotionalVideos
                    ORDER BY DisplayOrder ASC, Id DESC",
                    connection);

                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<object>();

                while (await reader.ReadAsync())
                    list.Add(MapRow(reader));

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
        // GET api/PromotionalVideos/get-active
        // -------------------------------------------------------
        [HttpGet("get-active")]
        public async Task<IActionResult> GetActive()
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    SELECT Id, Title, VideoType, VideoUrl, ThumbnailImage, DisplayOrder, IsActive, CreatedDate
                    FROM PromotionalVideos
                    WHERE IsActive = 1
                    ORDER BY DisplayOrder ASC, Id DESC",
                    connection);

                using var reader = await cmd.ExecuteReaderAsync();
                var list = new List<object>();

                while (await reader.ReadAsync())
                    list.Add(MapRow(reader));

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
        // PUT api/PromotionalVideos/toggle-active/{id}
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
                    UPDATE PromotionalVideos
                       SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END
                     WHERE Id = @Id;
                    SELECT IsActive FROM PromotionalVideos WHERE Id = @Id;",
                    connection);

                cmd.Parameters.AddWithValue("@Id", id);
                var result = await cmd.ExecuteScalarAsync();

                if (result == null)
                    return NotFound(new { success = false, message = $"Video with Id {id} not found." });

                bool isActive = Convert.ToBoolean(result);
                return Ok(new { success = true, id, isActive, message = isActive ? "Video is now active." : "Video is now inactive." });
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
        // PUT api/PromotionalVideos/update-order/{id}
        // Update display order for a video.
        // -------------------------------------------------------
        [HttpPut("update-order/{id}")]
        public async Task<IActionResult> UpdateOrder(int id, [FromBody] UpdateOrderRequest request)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var cmd = new SqlCommand(@"
                    UPDATE PromotionalVideos SET DisplayOrder = @DisplayOrder WHERE Id = @Id;
                    SELECT @@ROWCOUNT;",
                    connection);

                cmd.Parameters.AddWithValue("@DisplayOrder", request.DisplayOrder);
                cmd.Parameters.AddWithValue("@Id", id);

                int rows = (int)await cmd.ExecuteScalarAsync();
                if (rows == 0)
                    return NotFound(new { success = false, message = $"Video with Id {id} not found." });

                return Ok(new { success = true, id, displayOrder = request.DisplayOrder });
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
        // DELETE api/PromotionalVideos/delete/{id}
        // Removes DB row + thumbnail file.
        // -------------------------------------------------------
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Get thumbnail name before deleting
                var selectCmd = new SqlCommand(
                    "SELECT ThumbnailImage FROM PromotionalVideos WHERE Id = @Id", connection);
                selectCmd.Parameters.AddWithValue("@Id", id);
                var thumbObj = await selectCmd.ExecuteScalarAsync();

                if (thumbObj == null)
                    return NotFound(new { success = false, message = $"Video with Id {id} not found." });

                // Delete physical thumbnail file if it exists
                if (thumbObj != DBNull.Value)
                {
                    string thumbPath = Path.Combine(
                        Directory.GetCurrentDirectory(), "wwwroot", "thumbnails", thumbObj.ToString()!);
                    if (System.IO.File.Exists(thumbPath))
                        System.IO.File.Delete(thumbPath);
                }

                var deleteCmd = new SqlCommand(
                    "DELETE FROM PromotionalVideos WHERE Id = @Id", connection);
                deleteCmd.Parameters.AddWithValue("@Id", id);
                await deleteCmd.ExecuteNonQueryAsync();

                return Ok(new { success = true, message = $"Video {id} deleted successfully." });
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

        // ═══════════════════════════════════════════════════════
        // Private helpers
        // ═══════════════════════════════════════════════════════

        private object MapRow(SqlDataReader reader)
        {
            int    id             = Convert.ToInt32(reader["Id"]);
            string videoType      = reader["VideoType"].ToString()!;
            string videoUrl       = reader["VideoUrl"].ToString()!;
            string? thumbName     = reader["ThumbnailImage"] == DBNull.Value ? null : reader["ThumbnailImage"].ToString();
            string? thumbnailUrl  = thumbName is not null
                ? $"{Request.Scheme}://{Request.Host}/thumbnails/{thumbName}"
                : null;

            return new
            {
                id,
                title          = reader["Title"] == DBNull.Value ? null : reader["Title"].ToString(),
                videoType,
                videoUrl,
                thumbnailImage = thumbName,
                thumbnailUrl,
                displayOrder   = Convert.ToInt32(reader["DisplayOrder"]),
                isActive       = Convert.ToBoolean(reader["IsActive"]),
                createdDate    = Convert.ToDateTime(reader["CreatedDate"]).ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        /// <summary>
        /// Detects video platform and fetches title + thumbnail URL.
        /// Supports YouTube, Vimeo, and generic URLs.
        /// </summary>
        private async Task<VideoMeta> ExtractVideoMetaAsync(string url)
        {
            // ── YouTube ───────────────────────────────────────────────
            string? ytId = ExtractYouTubeId(url);
            if (ytId is not null)
            {
                string? title = null;
                try
                {
                    string oEmbedUrl = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={ytId}&format=json";
                    string json      = await _http.GetStringAsync(oEmbedUrl);
                    var    obj       = JObject.Parse(json);
                    title = obj["title"]?.ToString();
                }
                catch { /* title stays null */ }

                // Try maxresdefault first, fall back to hqdefault
                string thumbnailUrl = $"https://img.youtube.com/vi/{ytId}/maxresdefault.jpg";

                return new VideoMeta
                {
                    VideoType    = "YouTube",
                    Title        = title,
                    ThumbnailUrl = thumbnailUrl
                };
            }

            // ── Vimeo ─────────────────────────────────────────────────
            string? vimeoId = ExtractVimeoId(url);
            if (vimeoId is not null)
            {
                string? title        = null;
                string? thumbnailUrl = null;
                try
                {
                    string oEmbedUrl = $"https://vimeo.com/api/oembed.json?url=https://vimeo.com/{vimeoId}";
                    string json      = await _http.GetStringAsync(oEmbedUrl);
                    var    obj       = JObject.Parse(json);
                    title        = obj["title"]?.ToString();
                    thumbnailUrl = obj["thumbnail_url"]?.ToString();
                }
                catch { /* leave null */ }

                return new VideoMeta
                {
                    VideoType    = "Vimeo",
                    Title        = title,
                    ThumbnailUrl = thumbnailUrl
                };
            }

            // ── Generic / Other ───────────────────────────────────────
            return new VideoMeta { VideoType = "Other", Title = null, ThumbnailUrl = null };
        }

        private static string? ExtractYouTubeId(string url)
        {
            // Handles: youtu.be/<id>, watch?v=<id>, shorts/<id>, embed/<id>
            var patterns = new[]
            {
                @"youtu\.be/([A-Za-z0-9_\-]{11})",
                @"[?&]v=([A-Za-z0-9_\-]{11})",
                @"youtube\.com/shorts/([A-Za-z0-9_\-]{11})",
                @"youtube\.com/embed/([A-Za-z0-9_\-]{11})"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(url, pattern);
                if (match.Success) return match.Groups[1].Value;
            }
            return null;
        }

        private static string? ExtractVimeoId(string url)
        {
            var match = Regex.Match(url, @"vimeo\.com/(?:video/)?(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private class VideoMeta
        {
            public string  VideoType    { get; set; } = "Other";
            public string? Title        { get; set; }
            public string? ThumbnailUrl { get; set; }
        }
    }

    // ───────────────────────────────────────────────────────────
    // Request models
    // ───────────────────────────────────────────────────────────
    public class AddVideoRequest
    {
        public string?    Title        { get; set; }
        public string     VideoUrl     { get; set; } = string.Empty;
        public int        DisplayOrder { get; set; } = 1;
        /// <summary>Optional manual thumbnail. Takes priority over auto-extracted thumbnail.</summary>
        public IFormFile? Thumbnail    { get; set; }
    }

    public class UpdateOrderRequest
    {
        public int DisplayOrder { get; set; }
    }
}

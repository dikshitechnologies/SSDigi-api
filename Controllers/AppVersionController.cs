using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AppVersionController : ControllerBase
    {
        // ─────────────────────────────────────────────────────────────────────
        // GET api/AppVersion/check?platform=android&versionCode=10&appType=user
        //
        // Called by the app on every startup.
        // appType = 'user' (default) or 'admin'
        //
        // Response when update available:
        // {
        //   "updateAvailable": true,
        //   "latestVersion":   "1.1.0",
        //   "latestVersionCode": 11,
        //   "mandatory":       false,
        //   "message":         "A new version is available...",
        //   "storeUrl":        "https://play.google.com/..."
        // }
        //
        // Response when already on latest:
        // {
        //   "updateAvailable":   false,
        //   "latestVersion":     "1.1.0",
        //   "latestVersionCode": 11
        // }
        // ─────────────────────────────────────────────────────────────────────
        [AllowAnonymous]
        [HttpGet("check")]
        public async Task<IActionResult> CheckVersion(
            [FromQuery] string platform,
            [FromQuery] int versionCode,
            [FromQuery] string appType = "user")
        {
            // ── Validate platform ─────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(platform))
                return BadRequest(new { message = "platform is required. Use 'android' or 'ios'." });

            platform = platform.ToLowerInvariant().Trim();
            if (platform != "android" && platform != "ios")
                return BadRequest(new { message = "platform must be 'android' or 'ios'." });

            if (versionCode <= 0)
                return BadRequest(new { message = "versionCode must be a positive integer." });

            // ── Validate appType ──────────────────────────────────────────────
            appType = (appType ?? "user").ToLowerInvariant().Trim();
            if (appType != "user" && appType != "admin")
                return BadRequest(new { message = "appType must be 'user' or 'admin'." });

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
                    SELECT TOP 1
                        Version,
                        VersionCode,
                        IsMandatory,
                        UpdateMessage,
                        StoreUrl
                    FROM dbo.AppVersion
                    WHERE Platform = @platform
                      AND AppType  = @appType
                      AND IsActive = 1
                    ORDER BY VersionCode DESC",
                    conn);

                cmd.Parameters.AddWithValue("@platform", platform);
                cmd.Parameters.AddWithValue("@appType",  appType);

                using var reader = await cmd.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                {
                    return Ok(new
                    {
                        updateAvailable = false,
                        message = "No version info found for this platform/appType."
                    });
                }

                string latestVersion     = reader["Version"].ToString();
                int    latestVersionCode = Convert.ToInt32(reader["VersionCode"]);
                bool   isMandatory       = Convert.ToBoolean(reader["IsMandatory"]);
                string updateMessage     = reader["UpdateMessage"] == DBNull.Value
                                               ? "A new version is available. Please update the app."
                                               : reader["UpdateMessage"].ToString();
                string storeUrl          = reader["StoreUrl"] == DBNull.Value
                                               ? null
                                               : reader["StoreUrl"].ToString();

                bool updateAvailable = versionCode < latestVersionCode;

                if (!updateAvailable)
                {
                    return Ok(new
                    {
                        updateAvailable   = false,
                        latestVersion,
                        latestVersionCode
                    });
                }

                return Ok(new
                {
                    updateAvailable   = true,
                    latestVersion,
                    latestVersionCode,
                    mandatory         = isMandatory,
                    message           = updateMessage,
                    storeUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Version check failed.", error = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────────────────
        // POST api/AppVersion/update
        //
        // Admin endpoint — upserts version info when a new build is released.
        // Requires JWT auth.
        //
        // Body:
        // {
        //   "platform":      "android",
        //   "appType":       "user",
        //   "version":       "1.1.0",
        //   "versionCode":   11,
        //   "isMandatory":   false,
        //   "updateMessage": "Bug fixes.",
        //   "storeUrl":      "https://play.google.com/..."
        // }
        // ─────────────────────────────────────────────────────────────────────
        [Authorize]
        [HttpPost("update")]
        public async Task<IActionResult> UpdateVersion([FromBody] UpdateVersionRequest req)
        {
            if (req == null
                || string.IsNullOrWhiteSpace(req.Platform)
                || string.IsNullOrWhiteSpace(req.Version)
                || req.VersionCode <= 0)
                return BadRequest(new { message = "Platform, Version, and VersionCode are required." });

            req.Platform = req.Platform.ToLowerInvariant().Trim();
            if (req.Platform != "android" && req.Platform != "ios")
                return BadRequest(new { message = "Platform must be 'android' or 'ios'." });

            req.AppType = (req.AppType ?? "user").ToLowerInvariant().Trim();
            if (req.AppType != "user" && req.AppType != "admin")
                return BadRequest(new { message = "AppType must be 'user' or 'admin'." });

            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                // MERGE on Platform + AppType — update if exists, insert if not
                using var cmd = new SqlCommand(@"
                    MERGE dbo.AppVersion AS target
                    USING (SELECT @platform AS Platform, @appType AS AppType) AS src
                        ON target.Platform = src.Platform
                       AND target.AppType  = src.AppType
                    WHEN MATCHED THEN
                        UPDATE SET
                            Version       = @version,
                            VersionCode   = @versionCode,
                            IsMandatory   = @mandatory,
                            UpdateMessage = @message,
                            StoreUrl      = @storeUrl,
                            IsActive      = 1,
                            UpdatedAt     = SYSUTCDATETIME()
                    WHEN NOT MATCHED THEN
                        INSERT (Platform, AppType, Version, VersionCode,
                                IsMandatory, UpdateMessage, StoreUrl, IsActive)
                        VALUES (@platform, @appType, @version, @versionCode,
                                @mandatory, @message, @storeUrl, 1);",
                    conn);

                cmd.Parameters.AddWithValue("@platform",    req.Platform);
                cmd.Parameters.AddWithValue("@appType",     req.AppType);
                cmd.Parameters.AddWithValue("@version",     req.Version);
                cmd.Parameters.AddWithValue("@versionCode", req.VersionCode);
                cmd.Parameters.AddWithValue("@mandatory",   req.IsMandatory);
                cmd.Parameters.AddWithValue("@message",     req.UpdateMessage ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@storeUrl",    req.StoreUrl      ?? (object)DBNull.Value);

                await cmd.ExecuteNonQueryAsync();

                return Ok(new
                {
                    message     = "App version updated successfully.",
                    platform    = req.Platform,
                    appType     = req.AppType,
                    version     = req.Version,
                    versionCode = req.VersionCode,
                    mandatory   = req.IsMandatory
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Version update failed.", error = ex.Message });
            }
        }


        // ─────────────────────────────────────────────────────────────────────
        // GET api/AppVersion/list
        //
        // Returns all version rows — useful for admin dashboard.
        // ─────────────────────────────────────────────────────────────────────
        [AllowAnonymous]
        [HttpGet("list")]
        public async Task<IActionResult> ListVersions()
        {
            try
            {
                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
                    SELECT Id, Platform, AppType, Version, VersionCode,
                           IsMandatory, UpdateMessage, StoreUrl, IsActive, UpdatedAt
                    FROM dbo.AppVersion
                    ORDER BY AppType, Platform",
                    conn);

                var rows = new List<object>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add(new
                    {
                        id            = reader["Id"],
                        platform      = reader["Platform"].ToString(),
                        appType       = reader["AppType"].ToString(),
                        version       = reader["Version"].ToString(),
                        versionCode   = reader["VersionCode"],
                        isMandatory   = reader["IsMandatory"],
                        updateMessage = reader["UpdateMessage"] == DBNull.Value ? null : reader["UpdateMessage"].ToString(),
                        storeUrl      = reader["StoreUrl"]      == DBNull.Value ? null : reader["StoreUrl"].ToString(),
                        isActive      = reader["IsActive"],
                        updatedAt     = reader["UpdatedAt"]     == DBNull.Value ? null : (object)reader["UpdatedAt"]
                    });
                }

                return Ok(rows);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to fetch versions.", error = ex.Message });
            }
        }


        // ── Request model ─────────────────────────────────────────────────────
        public class UpdateVersionRequest
        {
            /// <summary>'android' or 'ios'</summary>
            public string Platform { get; set; }

            /// <summary>'user' or 'admin'</summary>
            public string AppType { get; set; }

            /// <summary>Semantic version string e.g. "1.2.0"</summary>
            public string Version { get; set; }

            /// <summary>Integer build number — used for comparison</summary>
            public int VersionCode { get; set; }

            /// <summary>true = user must update before using the app</summary>
            public bool IsMandatory { get; set; }

            /// <summary>Shown to the user in the update dialog</summary>
            public string? UpdateMessage { get; set; }

            /// <summary>Play Store / App Store URL</summary>
            public string? StoreUrl { get; set; }
        }
    }
}

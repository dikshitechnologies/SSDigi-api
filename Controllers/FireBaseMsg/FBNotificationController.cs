using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.FireBaseMsg
{
    [Route("api/[controller]")]
    [ApiController]
    public class FBNotificationController : ControllerBase
    {
        private static bool _firebaseInitialized = false;
        private static readonly object _lock = new object();

        // ─── Initialize Firebase once (thread-safe) ──────────────────────────────
        private void EnsureFirebaseInitialized()
        {
            if (_firebaseInitialized) return;
            lock (_lock)
            {
                if (_firebaseInitialized) return;

                var jsonPath = Path.Combine(AppContext.BaseDirectory,
                    "ssdigi-6cfec-firebase-adminsdk-fbsvc-83bac60409.json");

                if (FirebaseApp.DefaultInstance == null)
                {
                    FirebaseApp.Create(new AppOptions
                    {
                        Credential = GoogleCredential.FromFile(jsonPath)
                    });
                }

                _firebaseInitialized = true;
            }
        }

        // ─── POST api/FBNotification/send ────────────────────────────────────────
        /// <summary>
        /// Sends a push notification to all registered devices.
        /// Accepts multipart/form-data: Title, Body, and an optional Image file.
        /// Stores the image in wwwroot/notification/ and logs the result to Notifications table.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("send")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Send(
            [FromForm] string title,
            [FromForm] string body,
            IFormFile? image)
        {
            if (string.IsNullOrWhiteSpace(title))
                return BadRequest(new { message = "Title is required." });

            if (string.IsNullOrWhiteSpace(body))
                return BadRequest(new { message = "Body is required." });

            try
            {
                EnsureFirebaseInitialized();

                // ── 1. Handle image upload ───────────────────────────────────────
                string? imageUrl = null;

                if (image != null && image.Length > 0)
                {
                    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                    var ext = Path.GetExtension(image.FileName).ToLowerInvariant();

                    if (!allowedExtensions.Contains(ext))
                        return BadRequest(new { message = "Unsupported image type. Use jpg, jpeg, png or webp." });

                    if (image.Length > 20 * 1024 * 1024)
                        return BadRequest(new { message = "Image too large. Maximum size is 20 MB." });

                    // Save to wwwroot/notification/
                    var notificationFolder = Path.Combine(
                        Directory.GetCurrentDirectory(), "wwwroot", "notification");

                    if (!Directory.Exists(notificationFolder))
                        Directory.CreateDirectory(notificationFolder);

                    // Unique filename to avoid collisions
                    var fileName = $"{Guid.NewGuid()}{ext}";
                    var fullPath = Path.Combine(notificationFolder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                        await image.CopyToAsync(stream);

                    imageUrl = $"https://app.dikshitech.com/ssdigi/notification/{fileName}";
                }

                // ── 2. Fetch all valid FCM tokens ────────────────────────────────
                var tokens = new List<string>();

                var connStr = DBHelper.GetConnection();
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                using (var cmd = new SqlCommand(
                    @"SELECT DISTINCT FcmToken
                      FROM   RegisterUsers
                      WHERE  FcmToken IS NOT NULL
                        AND  LTRIM(RTRIM(FcmToken)) <> ''", conn))
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        tokens.Add(reader["FcmToken"].ToString()!);
                }

                if (tokens.Count == 0)
                    return Ok(new { message = "No registered devices found.", sent = 0, failed = 0 });

                // ── 3. Build FCM data payload ────────────────────────────────────
                var dataPayload = new Dictionary<string, string>
                {
                    { "click_action", "FLUTTER_NOTIFICATION_CLICK" }
                };

                if (!string.IsNullOrWhiteSpace(imageUrl))
                    dataPayload["image_url"] = imageUrl;

                // ── 4. Send in batches of 500 (FCM multicast limit) ──────────────
                const int batchSize = 500;
                int totalSent = 0, totalFailed = 0;
                var failedTokens = new List<string>();

                var messaging = FirebaseMessaging.DefaultInstance;

                for (int i = 0; i < tokens.Count; i += batchSize)
                {
                    var batch = tokens.Skip(i).Take(batchSize).ToList();

                    var multicast = new MulticastMessage
                    {
                        Tokens = batch,
                        Notification = new Notification
                        {
                            Title = title,
                            Body = body,
                            ImageUrl = imageUrl
                        },
                        Data = dataPayload,
                        Android = new AndroidConfig
                        {
                            Priority = Priority.High,
                            Notification = new AndroidNotification
                            {
                                Title = title,
                                Body = body,
                                ImageUrl = imageUrl,
                                Sound = "default",
                                ChannelId = "general"
                            }
                        },
                        Apns = new ApnsConfig
                        {
                            Aps = new Aps
                            {
                                Sound = "default",
                                Badge = 1,
                                ContentAvailable = true
                            }
                        }
                    };

                    var response = await messaging.SendEachForMulticastAsync(multicast);
                    totalSent += response.SuccessCount;
                    totalFailed += response.FailureCount;

                    for (int j = 0; j < response.Responses.Count; j++)
                        if (!response.Responses[j].IsSuccess)
                            failedTokens.Add(batch[j]);
                }

                // ── 5. Null-out stale/invalid tokens ────────────────────────────
                if (failedTokens.Count > 0)
                {
                    if (conn.State != System.Data.ConnectionState.Open)
                        await conn.OpenAsync();

                    foreach (var staleToken in failedTokens)
                    {
                        using var cleanCmd = new SqlCommand(
                            "UPDATE RegisterUsers SET FcmToken = NULL WHERE FcmToken = @token", conn);
                        cleanCmd.Parameters.AddWithValue("@token", staleToken);
                        await cleanCmd.ExecuteNonQueryAsync();
                    }
                }

                // ── 6. Log to Notifications table ────────────────────────────────
                string notificationId = Guid.NewGuid().ToString();

                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();

                using (var insertLog = new SqlCommand(@"
                    INSERT INTO Notifications
                        (NotificationId, Title, Body, ImageUrl, SentAt, TotalDevices, SuccessCount, FailureCount)
                    VALUES
                        (@NotificationId, @Title, @Body, @ImageUrl, @SentAt, @TotalDevices, @SuccessCount, @FailureCount)",
                    conn))
                {
                    insertLog.Parameters.AddWithValue("@NotificationId", notificationId);
                    insertLog.Parameters.AddWithValue("@Title", title);
                    insertLog.Parameters.AddWithValue("@Body", body);
                    insertLog.Parameters.AddWithValue("@ImageUrl", (object?)imageUrl ?? DBNull.Value);
                    insertLog.Parameters.AddWithValue("@SentAt", DateTime.Now);
                    insertLog.Parameters.AddWithValue("@TotalDevices", tokens.Count.ToString());
                    insertLog.Parameters.AddWithValue("@SuccessCount", totalSent.ToString());
                    insertLog.Parameters.AddWithValue("@FailureCount", totalFailed.ToString());
                    await insertLog.ExecuteNonQueryAsync();
                }

                return Ok(new
                {
                    message = "Notification sent successfully.",
                    notificationId,
                    imageUrl,
                    totalDevices = tokens.Count,
                    sent = totalSent,
                    failed = totalFailed
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to send notification.",
                    error = ex.Message
                });
            }
        }
    }
}
using CHITSCHEME.Global;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.FireBaseMsg
{
    /// <summary>
    /// Promotional notification controller.
    ///
    /// Endpoints:
    ///   POST api/PromotionalNotification/send-now
    ///       → Manually trigger one promotional broadcast (picks a time-aware random message).
    ///         Useful for admin testing or immediate campaigns.
    ///
    ///   GET  api/PromotionalNotification/preview
    ///       → Preview which message would be sent right now without actually sending.
    ///
    /// The background scheduler (PromotionalNotificationService) runs automatically
    /// every 45 minutes without calling these endpoints.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class PromotionalNotificationController : ControllerBase
    {
        private static bool  _firebaseInitialized = false;
        private static readonly object _lock      = new();

        // ── Firebase singleton init ───────────────────────────────────────────
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

        // ── GET api/PromotionalNotification/preview ───────────────────────────
        /// <summary>
        /// Returns the message that would be sent right now based on the current hour.
        /// No notification is actually dispatched.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("preview")]
        public IActionResult Preview()
        {
            int hour     = DateTime.Now.Hour;
            var (t, b)   = PromotionalNotificationTemplates.GetRandom(hour);
            string slot  = GetSlotLabel(hour);

            return Ok(new
            {
                currentHour = hour,
                slot,
                title = t,
                body  = b,
                note  = "This is a preview only — no notification was sent."
            });
        }

        // ── POST api/PromotionalNotification/send-now ─────────────────────────
        /// <summary>
        /// Manually triggers a promotional broadcast immediately.
        /// Picks a time-aware random message and sends to all registered FCM tokens.
        /// Optionally accepts a custom title / body via form-data to override the auto message.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("send-now")]
        public async Task<IActionResult> SendNow(
            [FromForm] string? title = null,
            [FromForm] string? body  = null)
        {
            try
            {
                EnsureFirebaseInitialized();

                int  hour = DateTime.Now.Hour;
                string slot = GetSlotLabel(hour);

                // Use provided title/body, or auto-pick from templates
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
                {
                    var (autoTitle, autoBody) = PromotionalNotificationTemplates.GetRandom(hour);
                    title = autoTitle;
                    body  = autoBody;
                }

                // ── 1. Fetch all valid FCM tokens ────────────────────────────
                var tokens  = new List<string>();
                var connStr = DBHelper.GetConnection();

                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                using (var cmd = new SqlCommand(
                    @"SELECT DISTINCT FcmToken
                      FROM   RegisterUsers
                      WHERE  FcmToken IS NOT NULL
                        AND  LTRIM(RTRIM(FcmToken)) <> ''", conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        tokens.Add(reader["FcmToken"].ToString()!);
                }

                if (tokens.Count == 0)
                    return Ok(new { message = "No registered devices found.", sent = 0, failed = 0 });

                // ── 2. Send in FCM multicast batches of 500 ──────────────────
                var messaging    = FirebaseMessaging.DefaultInstance;
                var failedTokens = new List<string>();
                int totalSent    = 0;
                int totalFailed  = 0;

                const int batchSize = 500;

                for (int i = 0; i < tokens.Count; i += batchSize)
                {
                    var batch = tokens.Skip(i).Take(batchSize).ToList();

                    var multicast = new MulticastMessage
                    {
                        Tokens       = batch,
                        Notification = new Notification { Title = title, Body = body },
                        Android      = new AndroidConfig
                        {
                            Priority     = Priority.High,
                            Notification = new AndroidNotification
                            {
                                Title     = title,
                                Body      = body,
                                Sound     = "default",
                                ChannelId = "general"
                            }
                        },
                        Apns = new ApnsConfig
                        {
                            Aps = new Aps
                            {
                                Sound            = "default",
                                Badge            = 1,
                                ContentAvailable = true
                            }
                        },
                        Data = new Dictionary<string, string>
                        {
                            { "click_action", "FLUTTER_NOTIFICATION_CLICK" },
                            { "type",         "promotional" }
                        }
                    };

                    var response = await messaging.SendEachForMulticastAsync(multicast);
                    totalSent   += response.SuccessCount;
                    totalFailed += response.FailureCount;

                    for (int j = 0; j < response.Responses.Count; j++)
                        if (!response.Responses[j].IsSuccess)
                            failedTokens.Add(batch[j]);
                }

                // ── 3. Log to Notifications table ────────────────────────────
                string notificationId = Guid.NewGuid().ToString();

                using (var insertLog = new SqlCommand(@"
                    INSERT INTO Notifications
                        (NotificationId, Title, Body, ImageUrl, SentAt,
                         TotalDevices, SuccessCount, FailureCount)
                    VALUES
                        (@NotificationId, @Title, @Body, NULL, @SentAt,
                         @TotalDevices, @SuccessCount, @FailureCount)",
                    conn))
                {
                    insertLog.Parameters.AddWithValue("@NotificationId", notificationId);
                    insertLog.Parameters.AddWithValue("@Title",          title);
                    insertLog.Parameters.AddWithValue("@Body",           body);
                    insertLog.Parameters.AddWithValue("@SentAt",         DateTime.Now);
                    insertLog.Parameters.AddWithValue("@TotalDevices",   tokens.Count.ToString());
                    insertLog.Parameters.AddWithValue("@SuccessCount",   totalSent.ToString());
                    insertLog.Parameters.AddWithValue("@FailureCount",   totalFailed.ToString());
                    await insertLog.ExecuteNonQueryAsync();
                }

                // ── 4. Null-out stale tokens ─────────────────────────────────
                foreach (var stale in failedTokens)
                {
                    using var cleanCmd = new SqlCommand(
                        "UPDATE RegisterUsers SET FcmToken = NULL WHERE FcmToken = @token", conn);
                    cleanCmd.Parameters.AddWithValue("@token", stale);
                    await cleanCmd.ExecuteNonQueryAsync();
                }

                return Ok(new
                {
                    message        = "Promotional notification sent.",
                    notificationId,
                    slot,
                    title,
                    body,
                    totalDevices   = tokens.Count,
                    sent           = totalSent,
                    failed         = totalFailed
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to send promotional notification.",
                    error   = ex.Message
                });
            }
        }

        // ── Helper: human-readable slot label ───────────────────────────────
        private static string GetSlotLabel(int hour) => hour switch
        {
            >= 6  and <= 11 => "Morning (06–11)",
            >= 12 and <= 16 => "Afternoon (12–16)",
            >= 17 and <= 20 => "Evening (17–20)",
            _               => "Night (21–05)"
        };
    }
}

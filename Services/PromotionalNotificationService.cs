using CHITSCHEME.Global;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using JEWELLBISREACT.DBConnection;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Services
{
    /// <summary>
    /// Background hosted service that sends time-aware promotional push notifications
    /// to ALL registered devices every 45 minutes.
    ///
    /// Slot mapping (server local time):
    ///   Morning   (06–11) → Motivational / Gold journey messages
    ///   Afternoon (12–16) → Rate update / market messages
    ///   Evening   (17–20) → End-of-day savings reminders
    ///   Night     (21–05) → Soft night check-in messages
    ///
    /// A 25% random variety injection ensures users see Curiosity / FOMO /
    /// Fun / Offers / Savings / Friendly messages mixed in regardless of slot.
    /// </summary>
    public class PromotionalNotificationService : BackgroundService
    {
        // ── Firebase singleton guard (shared across all notification services) ──
        private static bool  _firebaseInitialized = false;
        private static readonly object _fbLock    = new();

        private readonly ILogger<PromotionalNotificationService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(45);

        public PromotionalNotificationService(
            ILogger<PromotionalNotificationService> logger)
        {
            _logger = logger;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Entry point
        // ═══════════════════════════════════════════════════════════════════════
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PromotionalNotificationService started. Interval = 45 min.");
            EnsureFirebaseInitialized();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SendPromotionalBroadcastAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in PromotionalNotificationService.");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Core broadcast — picks a time-appropriate random message and sends
        // ═══════════════════════════════════════════════════════════════════════
        private async Task SendPromotionalBroadcastAsync()
        {
            int hour = DateTime.Now.Hour;
            var (title, body) = PromotionalNotificationTemplates.GetRandom(hour);

            _logger.LogInformation(
                "PromotionalBroadcast at {Time} (hour={Hour}) | Title: {Title}",
                DateTime.Now, hour, title);

            // ── 1. Fetch all valid FCM tokens ────────────────────────────────────
            var tokens   = new List<string>();
            var connStr  = DBHelper.GetConnection();

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
            {
                _logger.LogInformation("PromotionalBroadcast: No registered devices found.");
                return;
            }

            _logger.LogInformation(
                "PromotionalBroadcast: Sending to {Count} devices.", tokens.Count);

            // ── 2. Send in FCM multicast batches of 500 ──────────────────────────
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

            // ── 3. Log to Notifications table ────────────────────────────────────
            await LogNotificationAsync(conn, title, body,
                totalDevices: tokens.Count,
                successCount: totalSent,
                failureCount: totalFailed);

            // ── 4. Null-out stale / invalid FCM tokens ───────────────────────────
            if (failedTokens.Count > 0)
            {
                foreach (var stale in failedTokens)
                {
                    using var cleanCmd = new SqlCommand(
                        "UPDATE RegisterUsers SET FcmToken = NULL WHERE FcmToken = @token", conn);
                    cleanCmd.Parameters.AddWithValue("@token", stale);
                    await cleanCmd.ExecuteNonQueryAsync();
                }
            }

            _logger.LogInformation(
                "PromotionalBroadcast done. Sent={Sent} Failed={Failed}",
                totalSent, totalFailed);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Persist send record to the Notifications table
        // ═══════════════════════════════════════════════════════════════════════
        private static async Task LogNotificationAsync(
            SqlConnection conn,
            string title, string body,
            int totalDevices, int successCount, int failureCount)
        {
            using var cmd = new SqlCommand(@"
                INSERT INTO Notifications
                    (NotificationId, Title, Body, ImageUrl, SentAt,
                     TotalDevices, SuccessCount, FailureCount)
                VALUES
                    (@NotificationId, @Title, @Body, NULL, @SentAt,
                     @TotalDevices, @SuccessCount, @FailureCount)",
                conn);

            cmd.Parameters.AddWithValue("@NotificationId", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@Title",          title);
            cmd.Parameters.AddWithValue("@Body",           body);
            cmd.Parameters.AddWithValue("@SentAt",         DateTime.Now);
            cmd.Parameters.AddWithValue("@TotalDevices",   totalDevices.ToString());
            cmd.Parameters.AddWithValue("@SuccessCount",   successCount.ToString());
            cmd.Parameters.AddWithValue("@FailureCount",   failureCount.ToString());

            await cmd.ExecuteNonQueryAsync();
        }

        // ── Firebase singleton init (shared with other services) ─────────────
        private static void EnsureFirebaseInitialized()
        {
            if (_firebaseInitialized) return;
            lock (_fbLock)
            {
                if (_firebaseInitialized) return;

                var jsonPath = Path.Combine(AppContext.BaseDirectory,
                    "pukhraj-chit-firebase-adminsdk-fbsvc-b739f8988d.json");

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
    }
}

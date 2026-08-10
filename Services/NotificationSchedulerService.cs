using CHITSCHEME.Global;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using JEWELLBISREACT.DBConnection;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Services
{
    /// <summary>
    /// Background hosted service that runs every 30 minutes.
    /// Evaluates each user's activity and sends targeted push notifications
    /// based on the use-case matrix:
    ///
    ///  FIRST_LOGIN           → Welcome notification immediately on first login (fired from login flow)
    ///  FIRST_LOGIN_NO_SCHEME → New user, logged in but no scheme after 30 mins
    ///  LOGIN_NO_SCHEME       → Existing user opened app today but has no scheme → 6 PM same day
    ///  SCHEME_NOT_REGISTERED → No scheme at all → every 7 days
    ///  INACTIVE_7_DAYS       → Last login > 7 days ago → 10 AM
    /// </summary>
    public class NotificationSchedulerService : BackgroundService
    {
        private static bool _firebaseInitialized = false;
        private static readonly object _fbLock   = new();

        private readonly ILogger<NotificationSchedulerService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

        public NotificationSchedulerService(ILogger<NotificationSchedulerService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NotificationSchedulerService started.");
            EnsureFirebaseInitialized();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunScheduledJobsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in NotificationSchedulerService.");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Main job — evaluates all users and dispatches notifications
        // ═══════════════════════════════════════════════════════════
        private async Task RunScheduledJobsAsync()
        {
            _logger.LogInformation("Scheduler run at {Time}", DateTime.Now);

            var connStr = DBHelper.GetConnection();
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var hour = DateTime.Now.Hour;

            // NOTE: FCMToken is stored in RegisterUsers, not UserActivity.
            // Join RegisterUsers on PhoneNumber = CustomerCode to get the token.

            // ── 1. New user — no scheme 30 mins after first login ──────────────
            await SendBatchAsync(conn,
                triggerCode: NotificationTemplates.TRIGGER_FIRST_LOGIN_NO_SCHEME,
                query: @"
                    SELECT ua.CustomerCode, ru.FcmToken AS FCMToken
                    FROM   UserActivity ua
                    INNER JOIN RegisterUsers ru ON ru.PhoneNumber = ua.CustomerCode
                    WHERE  ru.FcmToken IS NOT NULL
                      AND  LTRIM(RTRIM(ru.FcmToken)) <> ''
                      AND  ua.LoginCount = 1
                      AND  ua.LastLogin  <= DATEADD(MINUTE, -30, GETDATE())
                      AND  (ua.LastNotificationSent IS NULL
                            OR CAST(ua.LastNotificationSent AS DATE) < CAST(GETDATE() AS DATE))
                      AND  NOT EXISTS (
                               SELECT 1 FROM Party p
                               WHERE  p.FPHONE   = ua.CustomerCode
                                 AND  p.fparent  LIKE '0000100044%'
                                 AND  p.faclevel < 0)",
                description: "New user / no scheme after 30 mins");

            // ── 2. Existing user opened app today, no scheme → send at 6 PM ───
            if (hour >= 18 && hour < 19)
            {
                await SendBatchAsync(conn,
                    triggerCode: NotificationTemplates.TRIGGER_LOGIN_NO_SCHEME,
                    query: @"
                        SELECT ua.CustomerCode, ru.FcmToken AS FCMToken
                        FROM   UserActivity ua
                        INNER JOIN RegisterUsers ru ON ru.PhoneNumber = ua.CustomerCode
                        WHERE  ru.FcmToken IS NOT NULL
                          AND  LTRIM(RTRIM(ru.FcmToken)) <> ''
                          AND  ua.LoginCount > 1
                          AND  CAST(ua.LastLogin AS DATE) = CAST(GETDATE() AS DATE)
                          AND  (ua.LastNotificationSent IS NULL
                                OR CAST(ua.LastNotificationSent AS DATE) < CAST(GETDATE() AS DATE))
                          AND  NOT EXISTS (
                                   SELECT 1 FROM Party p
                                   WHERE  p.FPHONE   = ua.CustomerCode
                                     AND  p.fparent  LIKE '0000100044%'
                                     AND  p.faclevel < 0)",
                    description: "Login today / no scheme → 6 PM nudge");
            }

            // ── 3. Registered user, no scheme at all → every 7 days ────────────
            await SendBatchAsync(conn,
                triggerCode: NotificationTemplates.TRIGGER_SCHEME_NOT_REGISTERED,
                query: @"
                    SELECT ua.CustomerCode, ru.FcmToken AS FCMToken
                    FROM   UserActivity ua
                    INNER JOIN RegisterUsers ru ON ru.PhoneNumber = ua.CustomerCode
                    WHERE  ru.FcmToken IS NOT NULL
                      AND  LTRIM(RTRIM(ru.FcmToken)) <> ''
                      AND  (ua.LastNotificationSent IS NULL
                            OR ua.LastNotificationSent <= DATEADD(DAY, -7, GETDATE()))
                      AND  NOT EXISTS (
                               SELECT 1 FROM Party p
                               WHERE  p.FPHONE   = ua.CustomerCode
                                 AND  p.fparent  LIKE '0000100044%'
                                 AND  p.faclevel < 0)",
                description: "Registered / no scheme → every 7 days");

            // ── 4. Inactive 7 days → send at 10 AM ─────────────────────────────
            if (hour >= 10 && hour < 11)
            {
                await SendBatchAsync(conn,
                    triggerCode: NotificationTemplates.TRIGGER_INACTIVE_7_DAYS,
                    query: @"
                        SELECT ua.CustomerCode, ru.FcmToken AS FCMToken
                        FROM   UserActivity ua
                        INNER JOIN RegisterUsers ru ON ru.PhoneNumber = ua.CustomerCode
                        WHERE  ru.FcmToken IS NOT NULL
                          AND  LTRIM(RTRIM(ru.FcmToken)) <> ''
                          AND  ua.LastLogin <= DATEADD(DAY, -7, GETDATE())
                          AND  (ua.LastNotificationSent IS NULL
                                OR CAST(ua.LastNotificationSent AS DATE) < CAST(GETDATE() AS DATE))",
                    description: "Inactive 7+ days → 10 AM");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Generic batch sender
        // ═══════════════════════════════════════════════════════════
        private async Task SendBatchAsync(
            SqlConnection conn,
            string triggerCode,
            string query,
            string description)
        {
            var (title, body) = NotificationTemplates.Get(triggerCode);

            var targets = new List<(string CustomerCode, string Token)>();

            using (var cmd = new SqlCommand(query, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    string code  = reader["CustomerCode"].ToString()!;
                    string token = reader["FCMToken"].ToString()!;
                    if (!string.IsNullOrWhiteSpace(token))
                        targets.Add((code, token));
                }
            }

            if (targets.Count == 0)
            {
                _logger.LogInformation("[{Trigger}] No targets found.", triggerCode);
                return;
            }

            _logger.LogInformation("[{Trigger}] Sending to {Count} users — {Desc}", triggerCode, targets.Count, description);

            var messaging    = FirebaseMessaging.DefaultInstance;
            var failedTokens = new List<string>();

            // FCM multicast limit is 500
            const int batchSize = 500;
            int totalSent = 0, totalFailed = 0;

            var tokenList = targets.Select(t => t.Token).ToList();

            for (int i = 0; i < tokenList.Count; i += batchSize)
            {
                var batch = tokenList.Skip(i).Take(batchSize).ToList();

                var multicast = new MulticastMessage
                {
                    Tokens = batch,
                    Notification = new Notification { Title = title, Body = body },
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            Title    = title,
                            Body     = body,
                            Sound    = "default",
                            ChannelId = "general"
                        }
                    },
                    Apns = new ApnsConfig
                    {
                        Aps = new Aps { Sound = "default", Badge = 1 }
                    }
                };

                var response = await messaging.SendEachForMulticastAsync(multicast);
                totalSent   += response.SuccessCount;
                totalFailed += response.FailureCount;

                for (int j = 0; j < response.Responses.Count; j++)
                    if (!response.Responses[j].IsSuccess)
                        failedTokens.Add(batch[j]);
            }

            // ── Update LastNotificationSent for successfully notified users ───
            var sentCodes = targets
                .Where(t => !failedTokens.Contains(t.Token))
                .Select(t => t.CustomerCode)
                .ToList();

            foreach (var code in sentCodes)
            {
                using var updCmd = new SqlCommand(@"
                    UPDATE UserActivity
                       SET LastNotificationSent = GETDATE()
                     WHERE CustomerCode = @CustomerCode",
                    conn);
                updCmd.Parameters.AddWithValue("@CustomerCode", code);
                await updCmd.ExecuteNonQueryAsync();
            }

            // ── Null-out stale/invalid FCM tokens in RegisterUsers ───────────
            foreach (var stale in failedTokens)
            {
                using var cleanCmd = new SqlCommand(
                    "UPDATE RegisterUsers SET FcmToken = NULL WHERE FcmToken = @token", conn);
                cleanCmd.Parameters.AddWithValue("@token", stale);
                await cleanCmd.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("[{Trigger}] Done. Sent={Sent} Failed={Failed}", triggerCode, totalSent, totalFailed);
        }

        // ── Firebase singleton init ───────────────────────────────
        private static void EnsureFirebaseInitialized()
        {
            if (_firebaseInitialized) return;
            lock (_fbLock)
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
    }
}

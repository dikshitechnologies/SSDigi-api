using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using static CHITSCHEME.Models.Rate_Fixing;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class RatefixingController : ControllerBase
    {
        // ─── Firebase init (shared flag with FBNotificationController) ───────────
        private static bool _firebaseInitialized = false;
        private static readonly object _lock = new object();

        private void EnsureFirebaseInitialized()
        {
            if (_firebaseInitialized) return;
            lock (_lock)
            {
                if (_firebaseInitialized) return;

                var jsonPath = Path.Combine(AppContext.BaseDirectory,
                    "ssdigi-6cfec-firebase-adminsdk-fbsvc-83bac60409.json");

                // Only create if no default app exists yet
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

        // ─── Send rate-update notification to all FCM tokens ─────────────────────
        private async Task SendRateUpdateNotificationAsync(string goldRate, string silverRate)
        {
            try
            {
                EnsureFirebaseInitialized();

                var tokens = new List<string>();

                using var conn = new SqlConnection(DBHelper.GetConnection());
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

                if (tokens.Count == 0) return;

                string title    = "Today's Rates!";
                string body     = $"Gold 22K - ₹{goldRate} per gram\nSilver - ₹{silverRate} per gram";
                string imageUrl = "https://app.dikshitech.com/ssdigi/NotifyImg/SSDigi.png";

                var dataPayload = new Dictionary<string, string>
                {
                    { "click_action", "FLUTTER_NOTIFICATION_CLICK" },
                    { "gold_rate",    goldRate    },
                    { "silver_rate",  silverRate  },
                    { "image_url",    imageUrl    }
                };

                const int batchSize = 500;
                var failedTokens = new List<string>();
                var messaging = FirebaseMessaging.DefaultInstance;

                for (int i = 0; i < tokens.Count; i += batchSize)
                {
                    var batch = tokens.Skip(i).Take(batchSize).ToList();

                    var multicast = new MulticastMessage
                    {
                        Tokens = batch,
                        Notification = new Notification { Title = title, Body = body, ImageUrl = imageUrl },
                        Data = dataPayload,
                        Android = new AndroidConfig
                        {
                            Priority = Priority.High,
                            Notification = new AndroidNotification
                            {
                                Title     = title,
                                Body      = body,
                                ImageUrl  = imageUrl,
                                Sound     = "default",
                                ChannelId = "general"
                            }
                        },
                        Apns = new ApnsConfig
                        {
                            Aps = new Aps { Sound = "default", Badge = 1, ContentAvailable = true }
                        }
                    };

                    var result = await messaging.SendEachForMulticastAsync(multicast);

                    for (int j = 0; j < result.Responses.Count; j++)
                        if (!result.Responses[j].IsSuccess)
                            failedTokens.Add(batch[j]);
                }

                // Null-out stale tokens
                if (failedTokens.Count > 0)
                {
                    foreach (var staleToken in failedTokens)
                    {
                        using var cleanCmd = new SqlCommand(
                            "UPDATE RegisterUsers SET FcmToken = NULL WHERE FcmToken = @token", conn);
                        cleanCmd.Parameters.AddWithValue("@token", staleToken);
                        await cleanCmd.ExecuteNonQueryAsync();
                    }
                }

                // ── Log to Notifications table ───────────────────────────────
                using (var insertLog = new SqlCommand(@"
                    INSERT INTO Notifications
                        (NotificationId, Title, Body, ImageUrl, SentAt, TotalDevices, SuccessCount, FailureCount)
                    VALUES
                        (@NotificationId, @Title, @Body, @ImageUrl, @SentAt, @TotalDevices, @SuccessCount, @FailureCount)",
                    conn))
                {
                    insertLog.Parameters.AddWithValue("@NotificationId", Guid.NewGuid().ToString());
                    insertLog.Parameters.AddWithValue("@Title",          title);
                    insertLog.Parameters.AddWithValue("@Body",           body);
                    insertLog.Parameters.AddWithValue("@ImageUrl", imageUrl);
                    insertLog.Parameters.AddWithValue("@SentAt",         DateTime.Now);
                    insertLog.Parameters.AddWithValue("@TotalDevices",   tokens.Count.ToString());
                    insertLog.Parameters.AddWithValue("@SuccessCount",   (tokens.Count - failedTokens.Count).ToString());
                    insertLog.Parameters.AddWithValue("@FailureCount",   failedTokens.Count.ToString());
                    await insertLog.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                // Notification failure should not break the rate-update response
            }
        }


        //--------------------------------------------------------------Get RateFixing  ------------------------------------------------------

        [HttpGet("getFullRateFixing")]
        public async Task<IActionResult> GetFullRateFixing()
        {
            var response = new RateFixingData();

            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();

                using (var cmd = new SqlCommand("SELECT FCODE, FNAME, FRATE FROM Division ORDER BY FNAME ASC", con))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        response.DivisionData.Add(new RateFixing
                        {
                            FCODE = reader["FCODE"].ToString(),
                            FNAME = reader["FNAME"].ToString().ToUpper(),
                            FRATE = reader["FRATE"].ToString()
                        });
                    }
                }
                using (var cmd = new SqlCommand("SELECT FOLDGOLDVA, FOLDGOLDDUST, FOLDGOLDRATE, FOLDSILVERVA, FOLDSILVERDUST, FOLDSILVERRATE FROM RateFix WHERE 1=1", con))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        response.RateFixData.Add(new OldRateFix
                        {
                            FOLDGOLDVA = reader["FOLDGOLDVA"].ToString(),
                            FOLDGOLDDUST = reader["FOLDGOLDDUST"].ToString(),
                            FOLDGOLDRATE = reader["FOLDGOLDRATE"].ToString(),
                            FOLDSILVERVA = reader["FOLDSILVERVA"].ToString(),
                            FOLDSILVERDUST = reader["FOLDSILVERDUST"].ToString(),
                            FOLDSILVERRATE = reader["FOLDSILVERRATE"].ToString()
                        });
                    }
                }

                return Ok(response);
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(500, new { message = "SQL Error", sqlEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal Server Error", ex.Message });
            }
        }

        //--------------------------------------------------------------update  RateFixing  ------------------------------------------------------

        [HttpPut("updateFullRateFixing")]
        public async Task<IActionResult> UpdateFullRateFixing([FromBody] FullRateFixingRequest request)
        {
            try
            {
                using var con = new SqlConnection(DBHelper.GetConnection());
                await con.OpenAsync();
                using var transaction = con.BeginTransaction();

                try
                {
                    var updateDivisionQuery = @"
                UPDATE Division SET FRATE = @FRATE WHERE FCODE = @FCODE";

                    foreach (var division in request.Division)
                    {
                        using (var cmd = new SqlCommand(updateDivisionQuery, con, transaction))
                        {
                            cmd.Parameters.AddWithValue("@FCODE", division.FCODE ?? "");
                            cmd.Parameters.AddWithValue("@FRATE", division.FRATE ?? "");
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    var checkQuery = "SELECT COUNT(*) FROM RateFix";
                    using var checkCmd = new SqlCommand(checkQuery, con, transaction);
                    int count = (int)await checkCmd.ExecuteScalarAsync();

                    string rateFixQuery;

                    if (count == 0)
                    {
                        rateFixQuery = @"
                    INSERT INTO RateFix 
                    (FOLDGOLDVA, FOLDGOLDDUST, FOLDGOLDRATE, FOLDSILVERVA, FOLDSILVERDUST, FOLDSILVERRATE)
                    VALUES 
                    (@FOLDGOLDVA, @FOLDGOLDDUST, @FOLDGOLDRATE, @FOLDSILVERVA, @FOLDSILVERDUST, @FOLDSILVERRATE)";
                    }
                    else
                    {
                        rateFixQuery = @"
                    UPDATE RateFix SET 
                        FOLDGOLDVA = @FOLDGOLDVA,
                        FOLDGOLDDUST = @FOLDGOLDDUST,
                        FOLDGOLDRATE = @FOLDGOLDRATE,
                        FOLDSILVERVA = @FOLDSILVERVA,
                        FOLDSILVERDUST = @FOLDSILVERDUST,
                        FOLDSILVERRATE = @FOLDSILVERRATE";
                    }

                    using (var cmd = new SqlCommand(rateFixQuery, con, transaction))
                    {
                        cmd.Parameters.AddWithValue("@FOLDGOLDVA", request.RateFix.FOLDGOLDVA ?? "");
                        cmd.Parameters.AddWithValue("@FOLDGOLDDUST", request.RateFix.FOLDGOLDDUST ?? "");
                        cmd.Parameters.AddWithValue("@FOLDGOLDRATE", request.RateFix.FOLDGOLDRATE ?? "");
                        cmd.Parameters.AddWithValue("@FOLDSILVERVA", request.RateFix.FOLDSILVERVA ?? "");
                        cmd.Parameters.AddWithValue("@FOLDSILVERDUST", request.RateFix.FOLDSILVERDUST ?? "");
                        cmd.Parameters.AddWithValue("@FOLDSILVERRATE", request.RateFix.FOLDSILVERRATE ?? "");
                        await cmd.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();

                    // ── Send rate-update push notification ───────────────────────
                    // FCODE 0002 = 916 (Gold), FCODE 0005 = Silver
                    string goldRate   = request.Division
                        .FirstOrDefault(d => d.FCODE == "0002")?.FRATE ?? "-";
                    string silverRate = request.Division
                        .FirstOrDefault(d => d.FCODE == "0005")?.FRATE ?? "-";

                    _ = Task.Run(() => SendRateUpdateNotificationAsync(goldRate, silverRate));

                    return Ok(new { message = "Division and RateFix updated successfully" });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, new { message = "Transaction failed", error = ex.Message });
                }
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new { message = "SQL Error", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal Server Error", error = ex.Message });
            }
        }



    }
}

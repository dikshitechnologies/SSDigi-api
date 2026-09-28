using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using QRCoder;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using JEWELLBISREACT.DBConnection;
using Razorpay.Api;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using CHITSCHEME.Models;
using CHITSCHEME.Controllers;


namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PaymentController : ControllerBase
    {
        private readonly IConfiguration _config;

        public PaymentController(IConfiguration config)
        {
            _config = config;
        }

        [HttpPost("create-order")]
        public IActionResult CreateOrder([FromBody] valAmount amt)
        {
            if (amt == null || string.IsNullOrWhiteSpace(amt.Amount))
                return BadRequest(new { message = "Amount is required" });

            if (!decimal.TryParse(amt.Amount, System.Globalization.NumberStyles.Any,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out decimal amountValue))
                return BadRequest(new { message = "Invalid amount format" });

            if (amountValue <= 0)
                return BadRequest(new { message = "Amount must be greater than 0" });

            string keyId = _config["Razorpay:KeyId"];
            string keySecret = _config["Razorpay:KeySecret"];

            var client = new RazorpayClient(keyId, keySecret);

            var amountInPaise = (int)decimal.Round(amountValue * 100m, 0, MidpointRounding.AwayFromZero);

            var options = new Dictionary<string, object>
            {
                { "amount", amountInPaise },
                { "currency", "INR" }
            };

            var order = client.Order.Create(options);

            return Ok(new
            {
                orderId = order["id"].ToString(),
                amount = Convert.ToDecimal(order["amount"]) / 100,
                currency = order["currency"].ToString(),
                keyId = keyId
            });
        }


        public class VerifyPaymentRequest
        {
            public string razorpay_order_id { get; set; }
            public string razorpay_payment_id { get; set; }
            public string razorpay_signature { get; set; }
        }
        
        public class valAmount
        {
            public string Amount { get; set; }
          
        }

        [HttpPost("verify-payment")]
        public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest req)
        {
            try
            {
                if (req == null ||
                    string.IsNullOrWhiteSpace(req.razorpay_order_id) ||
                    string.IsNullOrWhiteSpace(req.razorpay_payment_id) ||
                    string.IsNullOrWhiteSpace(req.razorpay_signature))
                {
                    return BadRequest(new { message = "Invalid payment data" });
                }

                // ── Step 1: Validate the order_id against our own DB ──────────
                string serverOrderId;
                int pendingRowId;
                using (var conn = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn.OpenAsync();
                    using var lookupCmd = new SqlCommand(
                        @"SELECT TOP 1 Id, RazorpayOrderId
                          FROM dbo.PendingPayments
                          WHERE RazorpayOrderId = @oid
                            AND Status IN ('pending','processing')",
                        conn);
                    lookupCmd.Parameters.AddWithValue("@oid", req.razorpay_order_id);
                    using var reader = await lookupCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                        return BadRequest(new { status = "failed", message = "Order not found or already processed." });

                    pendingRowId  = reader.GetInt32(0);
                    serverOrderId = reader.GetString(1);
                }

                // ── Step 2: Signature verification using the SERVER's order_id ─
                string keySecret = _config["Razorpay:KeySecret"];
                string generatedSignature = GenerateSignature(serverOrderId, req.razorpay_payment_id, keySecret);

                if (!string.Equals(generatedSignature, req.razorpay_signature, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { status = "failed", message = "Payment verification failed — signature mismatch." });

                // ── Step 3: Mark the pending row as 'processing' ──────────────
                using (var conn2 = new SqlConnection(DBHelper.GetConnection()))
                {
                    await conn2.OpenAsync();
                    using var markCmd = new SqlCommand(
                        @"UPDATE dbo.PendingPayments
                          SET Status = 'processing',
                              RazorpayPaymentId = @pid
                          WHERE Id = @id AND Status = 'pending'",
                        conn2);
                    markCmd.Parameters.AddWithValue("@pid", req.razorpay_payment_id);
                    markCmd.Parameters.AddWithValue("@id",  pendingRowId);
                    await markCmd.ExecuteNonQueryAsync();
                }

                return Ok(new { status = "success", message = "Payment verified successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error", error = ex.Message });
            }
        }

        // 🔒 Private helper — will NOT appear in Swagger
        private string GenerateSignature(string orderId, string paymentId, string secret)
        {
            string payload = orderId + "|" + paymentId;
            using (var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret)))
            {
                byte[] hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }


        [HttpPost("record")]
        public IActionResult RecordPayment([FromBody] PaymentRecordModel model)
        {
            try
            {

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    string query = @"
                    INSERT INTO PaymentRecords 
                    (UserId, RazorpayOrderId, RazorpayPaymentId, RazorpaySignature, Amount, Currency, Status, Description, Email, Contact, PaymentTime, VerificationTime,FpaymentType)
                    VALUES
                    (@UserId, @RazorpayOrderId, @RazorpayPaymentId, @RazorpaySignature, @Amount, @Currency, @Status, @Description, @Email, @Contact, GETDATE(), GETDATE(),@FpaymentType)";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@UserId", model.UserId);
                        cmd.Parameters.AddWithValue("@RazorpayOrderId", model.RazorpayOrderId);
                        cmd.Parameters.AddWithValue("@RazorpayPaymentId", model.RazorpayPaymentId);
                        cmd.Parameters.AddWithValue("@RazorpaySignature", model.RazorpaySignature);
                        cmd.Parameters.AddWithValue("@Amount", model.Amount);
                        cmd.Parameters.AddWithValue("@Currency", model.Currency);
                        cmd.Parameters.AddWithValue("@Status", model.Status);
                        cmd.Parameters.AddWithValue("@Description", model.Description ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Email", model.Email ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Contact", model.Contact ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@FpaymentType",model.FpaymentType);

                        con.Open();
                        cmd.ExecuteNonQuery();
                        con.Close();
                    }
                }

                return Ok(new { status = "success", message = "Payment record inserted successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "failed", message = ex.Message });
            }
        }


        public class PaymentRecordModel
        {
            public string UserId { get; set; }
            public string RazorpayOrderId { get; set; }
            public string RazorpayPaymentId { get; set; }
            public string RazorpaySignature { get; set; }
            public decimal Amount { get; set; }
            public string Currency { get; set; }
            public string Status { get; set; }
            public string Description { get; set; }
            public string Email { get; set; }
            public string Contact { get; set; }
            public string FpaymentType { get; set; }
        }











        //=============================================================================================

        // ─────────────────────────────────────────────────────────────────────
        // POST api/Payment/save-pending
        //
        // Called by the app BEFORE opening the Razorpay checkout sheet.
        // Stores the full InsertChitScheme payload alongside the Razorpay
        // orderId so the server-side webhook can complete the insert even if
        // the user closes the app while GPay / PhonePe is processing payment.
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost("save-pending")]
        public async Task<IActionResult> SavePending([FromBody] SavePendingRequest req)
        {
            if (req == null
                || string.IsNullOrWhiteSpace(req.RazorpayOrderId)
                || req.ChitPayload == null)
                return BadRequest(new { message = "RazorpayOrderId and ChitPayload are required." });

            try
            {
                string payloadJson = JsonSerializer.Serialize(req.ChitPayload);

                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                // MERGE so that a retry from the app does not create duplicate rows.
                using var cmd = new SqlCommand(@"
                    MERGE dbo.PendingPayments AS target
                    USING (SELECT @OrderId AS RazorpayOrderId) AS source
                        ON target.RazorpayOrderId = source.RazorpayOrderId
                    WHEN NOT MATCHED THEN
                        INSERT (RazorpayOrderId, UserId, ChitPayload, Status, CreatedAt)
                        VALUES (@OrderId, @UserId, @Payload, 'pending', GETDATE())
                    WHEN MATCHED AND target.Status NOT IN ('completed','processing') THEN
                        UPDATE SET ChitPayload = @Payload,
                                   UserId      = @UserId,
                                   Status      = 'pending';",
                    conn);

                cmd.Parameters.AddWithValue("@OrderId", req.RazorpayOrderId);
                cmd.Parameters.AddWithValue("@UserId",  req.UserId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Payload", payloadJson);

                await cmd.ExecuteNonQueryAsync();

                return Ok(new { message = "Pending payment saved." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to save pending payment.", error = ex.Message });
            }
        }

        public class SavePendingRequest
        {
            public string RazorpayOrderId { get; set; }
            public string UserId { get; set; }
            /// <summary>
            /// The full ChitSchemeModel that would be passed to InsertChitScheme,
            /// minus OmniTransactionId (not applicable for Razorpay).
            /// </summary>
            public ChitSchemeModel ChitPayload { get; set; }
        }


        // ─────────────────────────────────────────────────────────────────────
        // POST api/Payment/razorpay-webhook
        //
        // Razorpay delivers payment.captured here — including when the customer
        // paid via GPay/PhonePe and the app was closed before InsertChitScheme ran.
        //
        // Configure in Razorpay Dashboard → Settings → Webhooks:
        //   URL    : https://<your-domain>/api/Payment/razorpay-webhook
        //   Events : payment.captured
        //   Secret : Razorpay:WebhookSecret  (appsettings / env var)
        // ─────────────────────────────────────────────────────────────────────
        [AllowAnonymous]
        [HttpPost("razorpay-webhook")]
        public async Task<IActionResult> RazorpayWebhook()
        {
            // ── 1. Read raw body BEFORE any model binding touches the stream ──
            string rawBody;
            using (var sr = new System.IO.StreamReader(Request.Body, Encoding.UTF8))
                rawBody = await sr.ReadToEndAsync();

            // ── 2. HMAC-SHA256 signature validation ───────────────────────────
            string webhookSecret = _config["Razorpay:WebhookSecret"];
            if (!string.IsNullOrWhiteSpace(webhookSecret))
            {
                Request.Headers.TryGetValue("X-Razorpay-Signature", out var incomingSig);
                if (string.IsNullOrWhiteSpace(incomingSig))
                    return StatusCode(400, new { message = "Missing X-Razorpay-Signature header." });

                if (!string.Equals(ComputeWebhookSignature(rawBody, webhookSecret),
                                   incomingSig.ToString(),
                                   StringComparison.OrdinalIgnoreCase))
                    return StatusCode(400, new { message = "Webhook signature mismatch." });
            }

            // ── 3. Parse event envelope ────────────────────────────────────────
            JsonDocument doc;
            try { doc = JsonDocument.Parse(rawBody); }
            catch { return BadRequest(new { message = "Invalid JSON body." }); }

            using (doc)
            {
                var root = doc.RootElement;

                if (!root.TryGetProperty("event", out var eventProp))
                    return Ok(new { message = "No event field — ignored." });

                string eventType = eventProp.GetString();

                // ── 4. Idempotency via X-Razorpay-Event-Id ────────────────────
                Request.Headers.TryGetValue("X-Razorpay-Event-Id", out var eventIdHeader);
                string eventId = eventIdHeader.ToString();

                if (!root.TryGetProperty("payload", out var payload)
                    || !payload.TryGetProperty("payment", out var paymentWrapper)
                    || !paymentWrapper.TryGetProperty("entity", out var entity))
                    return BadRequest(new { message = "Unexpected webhook payload shape." });

                string paymentId = entity.TryGetProperty("id",       out var pid) ? pid.GetString() : null;
                string orderId   = entity.TryGetProperty("order_id", out var oid) ? oid.GetString() : null;
                string contact   = entity.TryGetProperty("contact",  out var cnt) ? cnt.GetString() : null;
                long   amtPaise  = entity.TryGetProperty("amount",   out var amt) ? amt.GetInt64()  : 0;

                if (string.IsNullOrWhiteSpace(paymentId))
                    return BadRequest(new { message = "Missing payment_id in webhook." });

                decimal amtRupees = amtPaise / 100m;

                using var conn = new SqlConnection(DBHelper.GetConnection());
                await conn.OpenAsync();

                // ── 5. Log event — UNIQUE(EventId) blocks duplicate runs ──────
                if (!string.IsNullOrWhiteSpace(eventId))
                {
                    bool isDuplicate = await TryLogWebhookEvent(conn, eventId, eventType, paymentId, orderId);
                    if (isDuplicate)
                        return Ok(new { message = "Duplicate event — already processed.", eventId });
                }

                // Only process payment.captured events
                if (eventType != "payment.captured")
                    return Ok(new { message = $"Event '{eventType}' ignored." });

                // ── 6. Idempotency — already inserted by the app? ─────────────
                using (var idempCmd = new SqlCommand(
                    "SELECT TOP 1 fVouchno FROM Bledger WHERE FRazorpayPaymentId = @pid AND fBillType = 'CT'",
                    conn))
                {
                    idempCmd.Parameters.AddWithValue("@pid", paymentId);
                    var existing = await idempCmd.ExecuteScalarAsync();
                    if (existing != null && existing != DBNull.Value)
                    {
                        await MarkWebhookEventProcessed(conn, eventId);
                        return Ok(new { message = "Already inserted by app.", voucherNo = existing.ToString() });
                    }
                }

                // ── 7. PRIMARY PATH: ChitPayload from PendingPayments ─────────
                List<SchemeList> schemeDetails = null;
                string           pendingUserId  = null;
                int              pendingRowId   = 0;

                if (!string.IsNullOrWhiteSpace(orderId))
                {
                    using var pendingCmd = new SqlCommand(@"
                        SELECT TOP 1 Id, ChitPayload, UserId
                        FROM dbo.PendingPayments
                        WHERE RazorpayOrderId = @oid
                          AND Status NOT IN ('completed')",
                        conn);
                    pendingCmd.Parameters.AddWithValue("@oid", orderId);
                    using var pendingReader = await pendingCmd.ExecuteReaderAsync();
                    if (await pendingReader.ReadAsync())
                    {
                        pendingRowId  = pendingReader.GetInt32(0);
                        string rawPayload = pendingReader.IsDBNull(1) ? null : pendingReader.GetString(1);
                        pendingUserId = pendingReader.IsDBNull(2) ? null : pendingReader.GetString(2);
                        pendingReader.Close();

                        if (!string.IsNullOrWhiteSpace(rawPayload))
                        {
                            try
                            {
                                var savedModel = JsonSerializer.Deserialize<ChitSchemeModel>(rawPayload,
                                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (savedModel?.SchemeDetails != null && savedModel.SchemeDetails.Count > 0)
                                    schemeDetails = savedModel.SchemeDetails;
                            }
                            catch { /* malformed payload — fall through to phone reconstruction */ }
                        }
                    }
                    else { pendingReader.Close(); }
                }

                // ── 8. FALLBACK PATH: reconstruct from phone + amount ─────────
                string resolvedCusCode    = null;
                string resolvedSchemeCode = null;

                if (schemeDetails == null)
                {
                    string phone = contact ?? "";
                    if (phone.StartsWith("+91"))                           phone = phone.Substring(3);
                    else if (phone.StartsWith("91") && phone.Length == 12) phone = phone.Substring(2);
                    phone = phone.Trim();

                    if (string.IsNullOrWhiteSpace(phone))
                    {
                        await ParkForReview(conn, orderId, paymentId, amtRupees, null, eventId,
                            "No ChitPayload in PendingPayments and no contact number in webhook.");
                        return Ok(new { message = "No payload and no contact — parked for review." });
                    }

                    var schemes = new List<WebhookSchemeRow>();
                    using (var schemeCmd = new SqlCommand(@"
                        SELECT
                            P.FCODE       AS CusCode,
                            P.FID         AS SchemeCode,
                            P.FAMOUNT     AS InstallmentAmt,
                            P.FCOMPCODE   AS CompCode,
                            P.FDUE        AS TotalDue,
                            ISNULL((SELECT MAX(FDUE) FROM Ledger
                                    WHERE fid = P.FID AND fCrDb='CR' AND fType='CT'), 0) AS PaidDue
                        FROM PARTY P
                        WHERE P.FPHONE = @phone
                          AND P.FPARENT LIKE '0000100044%'
                          AND P.FSHOW = '1'", conn))
                    {
                        schemeCmd.Parameters.AddWithValue("@phone", phone);
                        using var sr2 = await schemeCmd.ExecuteReaderAsync();
                        while (await sr2.ReadAsync())
                        {
                            schemes.Add(new WebhookSchemeRow
                            {
                                CusCode        = sr2["CusCode"].ToString(),
                                SchemeCode     = sr2["SchemeCode"].ToString(),
                                InstallmentAmt = Convert.ToDecimal(sr2["InstallmentAmt"]),
                                CompCode       = sr2["CompCode"].ToString(),
                                TotalDue       = Convert.ToInt32(sr2["TotalDue"]),
                                PaidDue        = Convert.ToInt32(sr2["PaidDue"])
                            });
                        }
                    }

                    if (schemes.Count == 0)
                    {
                        await ParkForReview(conn, orderId, paymentId, amtRupees, phone, eventId,
                            $"No active digi schemes found for phone {phone}. No ChitPayload in PendingPayments.");
                        return Ok(new { message = "No active schemes — parked for review." });
                    }

                    WebhookSchemeRow resolved = null;
                    if (schemes.Count == 1)
                    {
                        resolved = schemes[0];
                    }
                    else
                    {
                        var activeUnpaid  = schemes.Where(s => s.PaidDue < s.TotalDue).ToList();
                        var amountMatches = activeUnpaid
                            .Where(s => Math.Abs(s.InstallmentAmt - amtRupees) <= 5m)
                            .ToList();
                        if (amountMatches.Count == 1)
                            resolved = amountMatches[0];
                        else if (amountMatches.Count == 0 && activeUnpaid.Count == 1)
                            resolved = activeUnpaid[0];
                    }

                    if (resolved == null)
                    {
                        await ParkForReview(conn, orderId, paymentId, amtRupees, phone, eventId,
                            $"Ambiguous: {schemes.Count} active schemes for phone {phone}. ₹{amtRupees}. Manual review needed.");
                        return Ok(new { message = "Ambiguous scheme — parked for review." });
                    }

                    resolvedCusCode    = resolved.CusCode;
                    resolvedSchemeCode = resolved.SchemeCode;

                    schemeDetails = new List<SchemeList>
                    {
                        new SchemeList
                        {
                            CusCode    = resolved.CusCode,
                            SchemeCode = resolved.SchemeCode,
                            Amount     = resolved.InstallmentAmt.ToString("F2"),
                            TotalAmt   = amtRupees.ToString("F2"),
                            CompCode   = resolved.CompCode ?? "",
                            FDUE       = "1",
                            Weight     = null,
                            fbwt       = null,
                            fbamt      = null,
                            fbfinalamt = null,
                            finalwt    = null,
                            FGRATE     = null
                        }
                    };
                }
                else
                {
                    resolvedCusCode    = schemeDetails[0].CusCode;
                    resolvedSchemeCode = schemeDetails[0].SchemeCode;
                }

                // ── 9. Insert inside a Serializable transaction ───────────────
                try
                {
                    using var transaction = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);

                    // Race-guard inside the transaction
                    using (var idempTx = new SqlCommand(
                        "SELECT TOP 1 fVouchno FROM Bledger WHERE FRazorpayPaymentId = @pid AND fBillType = 'CT'",
                        conn, transaction))
                    {
                        idempTx.Parameters.AddWithValue("@pid", paymentId);
                        var ex2 = await idempTx.ExecuteScalarAsync();
                        if (ex2 != null && ex2 != DBNull.Value)
                        {
                            transaction.Rollback();
                            await MarkWebhookEventProcessed(conn, eventId);
                            if (pendingRowId > 0) await MarkPendingComplete(conn, pendingRowId, paymentId);
                            return Ok(new { message = "Already inserted (race check).", voucherNo = ex2.ToString() });
                        }
                    }

                    string voucherNo = SchemeDetailsController.GetSingleChitSchemeVoucherNo(conn, transaction);

                    foreach (var item in schemeDetails)
                    {
                        using var dueCmd = new SqlCommand(
                            "SELECT ISNULL(MAX(FDUE), 0) FROM ledger WITH (UPDLOCK, HOLDLOCK) " +
                            "WHERE fid = @fid AND fCrDb = 'CR' AND fType = 'CT'",
                            conn, transaction);
                        dueCmd.Parameters.AddWithValue("@fid", item.SchemeCode);
                        var dueResult = await dueCmd.ExecuteScalarAsync();
                        item.FDUE = ((dueResult != DBNull.Value ? Convert.ToInt32(dueResult) : 0) + 1).ToString();
                    }

                    SchemeDetailsController.InsertBledgerPublic(schemeDetails, voucherNo, conn, transaction, paymentId);
                    SchemeDetailsController.InsertLedgerPublic(schemeDetails, voucherNo, conn, transaction);

                    transaction.Commit();

                    // ── Post-commit book-keeping (best-effort, outside transaction) ──
                    await LogWebhookRecord(conn, pendingUserId ?? "", orderId, paymentId, amtRupees, contact);
                    await MarkWebhookEventProcessed(conn, eventId);
                    if (pendingRowId > 0) await MarkPendingComplete(conn, pendingRowId, paymentId);

                    return Ok(new
                    {
                        message       = "Webhook insert successful.",
                        voucherNo,
                        schemeCode    = resolvedSchemeCode,
                        payloadSource = pendingRowId > 0 ? "PendingPayments" : "phone-reconstruct"
                    });
                }
                catch (Exception ex)
                {
                    if (pendingRowId > 0) await MarkPendingFailed(conn, pendingRowId, ex.Message);
                    await ParkForReview(conn, orderId, paymentId, amtRupees, contact, eventId,
                        $"Insert failed: {ex.Message}");
                    return StatusCode(500, new { message = "Webhook insert failed.", error = ex.Message });
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Webhook helper types and methods
        // ─────────────────────────────────────────────────────────────────────

        private class WebhookSchemeRow
        {
            public string  CusCode        { get; set; }
            public string  SchemeCode     { get; set; }
            public decimal InstallmentAmt { get; set; }
            public string  CompCode       { get; set; }
            public int     TotalDue       { get; set; }
            public int     PaidDue        { get; set; }
        }

        private static async Task<bool> TryLogWebhookEvent(
            SqlConnection conn, string eventId, string eventType, string paymentId, string orderId)
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    INSERT INTO dbo.RazorpayWebhookEvents
                        (EventId, EventType, PaymentId, OrderId, ReceivedAt, Status)
                    VALUES
                        (@eid, @etype, @pid, @oid, SYSUTCDATETIME(), 'received');",
                    conn);
                cmd.Parameters.AddWithValue("@eid",   eventId   ?? "");
                cmd.Parameters.AddWithValue("@etype", eventType ?? "");
                cmd.Parameters.AddWithValue("@pid",   paymentId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@oid",   orderId   ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
                return false;
            }
            catch (SqlException sx) when (sx.Number == 2627 || sx.Number == 2601)
            {
                return true; // duplicate — already processed
            }
            catch
            {
                return false; // log table error — let processing continue
            }
        }

        private static async Task MarkWebhookEventProcessed(SqlConnection conn, string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;
            try
            {
                using var cmd = new SqlCommand(@"
                    UPDATE dbo.RazorpayWebhookEvents
                    SET Status = 'processed', ProcessedAt = SYSUTCDATETIME()
                    WHERE EventId = @eid",
                    conn);
                cmd.Parameters.AddWithValue("@eid", eventId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* best-effort */ }
        }

        private static async Task ParkForReview(SqlConnection conn, string orderId,
            string paymentId, decimal amount, string contact, string eventId, string reason)
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    MERGE dbo.PendingPayments AS target
                    USING (SELECT @OrderId AS RazorpayOrderId) AS src
                        ON target.RazorpayOrderId = src.RazorpayOrderId
                    WHEN NOT MATCHED THEN
                        INSERT (RazorpayOrderId, RazorpayPaymentId, UserId, ChitPayload,
                                Status, CreatedAt, ErrorMessage)
                        VALUES (@OrderId, @PaymentId, @Contact,
                                @Payload, 'needs_review', GETDATE(), @Reason)
                    WHEN MATCHED THEN
                        UPDATE SET Status            = 'needs_review',
                                   RazorpayPaymentId = @PaymentId,
                                   ErrorMessage      = @Reason,
                                   ProcessedAt       = GETDATE();",
                    conn);
                cmd.Parameters.AddWithValue("@OrderId",   orderId   ?? "");
                cmd.Parameters.AddWithValue("@PaymentId", paymentId ?? "");
                cmd.Parameters.AddWithValue("@Contact",   contact   ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Payload",   $"{{\"amount\":{amount}}}");
                cmd.Parameters.AddWithValue("@Reason",    reason    ?? "");
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* best-effort */ }

            if (!string.IsNullOrWhiteSpace(eventId))
            {
                try
                {
                    using var evCmd = new SqlCommand(@"
                        UPDATE dbo.RazorpayWebhookEvents
                        SET Status = 'needs_review', ErrorMessage = @reason, ProcessedAt = SYSUTCDATETIME()
                        WHERE EventId = @eid",
                        conn);
                    evCmd.Parameters.AddWithValue("@reason", reason  ?? "");
                    evCmd.Parameters.AddWithValue("@eid",    eventId);
                    await evCmd.ExecuteNonQueryAsync();
                }
                catch { /* best-effort */ }
            }
        }

        private static async Task LogWebhookRecord(SqlConnection conn, string userId,
            string orderId, string paymentId, decimal amount, string contact)
        {
            try
            {
                using var cmd = new SqlCommand(@"
                    INSERT INTO PaymentRecords
                        (UserId, RazorpayOrderId, RazorpayPaymentId, RazorpaySignature,
                         Amount, Currency, Status, Description, Contact,
                         PaymentTime, VerificationTime, FpaymentType)
                    VALUES
                        (@uid, @oid, @pid, 'webhook',
                         @amt, 'INR', 'success', 'Webhook auto-insert', @contact,
                         GETDATE(), GETDATE(), 'Y')",
                    conn);
                cmd.Parameters.AddWithValue("@uid",     userId    ?? "");
                cmd.Parameters.AddWithValue("@oid",     orderId   ?? "");
                cmd.Parameters.AddWithValue("@pid",     paymentId ?? "");
                cmd.Parameters.AddWithValue("@amt",     amount);
                cmd.Parameters.AddWithValue("@contact", contact   ?? "");
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* best-effort audit log */ }
        }

        private static async Task MarkPendingComplete(SqlConnection conn, int id, string paymentId)
        {
            using var cmd = new SqlCommand(@"
                UPDATE dbo.PendingPayments
                SET Status = 'completed', ProcessedAt = GETDATE(), RazorpayPaymentId = @pid
                WHERE Id = @id",
                conn);
            cmd.Parameters.AddWithValue("@pid", paymentId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@id",  id);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task MarkPendingFailed(SqlConnection conn, int id, string error)
        {
            using var cmd = new SqlCommand(@"
                UPDATE dbo.PendingPayments
                SET Status = 'failed', ProcessedAt = GETDATE(), ErrorMessage = @err
                WHERE Id = @id",
                conn);
            cmd.Parameters.AddWithValue("@err", error ?? "");
            cmd.Parameters.AddWithValue("@id",  id);
            await cmd.ExecuteNonQueryAsync();
        }

        private static string ComputeWebhookSignature(string body, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        //=============================================================================================

        [HttpPost("verify")]
        public IActionResult Verify([FromBody] PaymentDto dto)
        {
            if (dto == null || dto.Amount <= 0 || string.IsNullOrEmpty(dto.TransactionRef))
                return BadRequest("Invalid input");

            using (var conn = new SqlConnection(DBHelper.GetConnection()))
            {
                conn.Open();
                var cmd = new SqlCommand(@"
                INSERT INTO Payments (TransactionRef, Amount, UpiId, RawMessage, Status)
                VALUES (@ref, @amt, @upi, @msg, 'Success')", conn);

                cmd.Parameters.AddWithValue("@ref", dto.TransactionRef);
                cmd.Parameters.AddWithValue("@amt", dto.Amount);
                cmd.Parameters.AddWithValue("@upi", dto.UpiId ?? "");
                cmd.Parameters.AddWithValue("@msg", dto.Message ?? "");
                cmd.ExecuteNonQuery();
            }

            return Ok(new { message = "Saved" });
        }


        //[HttpPost("SavePaymentResponse")]
        //public IActionResult SavePaymentResponse([FromBody] PaymentResponseDto request)
        //{
        //    try
        //    {
        //        using (SqlConnection conn = new SqlConnection(DBHelper.GetConnection()))
        //        {   
        //            conn.Open();
        //            using (SqlCommand cmd = new SqlCommand(@"
        //        INSERT INTO PaymentResponses (
        //            EasePayID, TxnID, Status, Result, Amount, PaymentMethod, CardType,
        //            CardNumber, BankName, IssuingBank, Mode, AuthCode, BankRefNum,
        //            Phone, Email, FirstName, AddedOn, PaymentSource, ProductInfo, ErrorMessage, RawResponse
        //        )
        //        VALUES (
        //            @EasePayID, @TxnID, @Status, @Result, @Amount, @PaymentMethod, @CardType,
        //            @CardNumber, @BankName, @IssuingBank, @Mode, @AuthCode, @BankRefNum,
        //            @Phone, @Email, @FirstName, @AddedOn, @PaymentSource, @ProductInfo, @ErrorMessage, @RawResponse
        //        )", conn))
        //            {
        //                cmd.Parameters.AddWithValue("@EasePayID", request.EasePayID ?? "");
        //                cmd.Parameters.AddWithValue("@TxnID", request.TxnID ?? "");
        //                cmd.Parameters.AddWithValue("@Status", request.Status ?? "");
        //                cmd.Parameters.AddWithValue("@Result", request.Result ?? "");
        //                cmd.Parameters.AddWithValue("@Amount", request.Amount);
        //                cmd.Parameters.AddWithValue("@PaymentMethod", request.PaymentMethod ?? "");
        //                cmd.Parameters.AddWithValue("@CardType", request.CardType ?? "");
        //                cmd.Parameters.AddWithValue("@CardNumber", request.CardNumber ?? "");
        //                cmd.Parameters.AddWithValue("@BankName", request.BankName ?? "");
        //                cmd.Parameters.AddWithValue("@IssuingBank", request.IssuingBank ?? "");
        //                cmd.Parameters.AddWithValue("@Mode", request.Mode ?? "");
        //                cmd.Parameters.AddWithValue("@AuthCode", request.AuthCode ?? "");
        //                cmd.Parameters.AddWithValue("@BankRefNum", request.BankRefNum ?? "");
        //                cmd.Parameters.AddWithValue("@Phone", request.Phone ?? "");
        //                cmd.Parameters.AddWithValue("@Email", request.Email ?? "");
        //                cmd.Parameters.AddWithValue("@FirstName", request.FirstName ?? "");
        //                cmd.Parameters.AddWithValue("@AddedOn", (object?)request.AddedOn ?? DBNull.Value);
        //                cmd.Parameters.AddWithValue("@PaymentSource", request.PaymentSource ?? "");
        //                cmd.Parameters.AddWithValue("@ProductInfo", request.ProductInfo ?? "");
        //                cmd.Parameters.AddWithValue("@ErrorMessage", request.ErrorMessage ?? "");
        //                cmd.Parameters.AddWithValue("@RawResponse", request.RawResponse ?? "");

        //                cmd.ExecuteNonQuery();
        //            }
        //        }

        //        return Ok(new { status = true, message = "Payment response saved successfully." });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, new { status = false, message = "Error saving payment response.", error = ex.Message });
        //    }
        //}

    }
}






public class PaymentResponseDto
{
    public string EasePayID { get; set; }
    public string TxnID { get; set; }
    public string Status { get; set; }
    public string Result { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; }
    public string CardType { get; set; }
    public string CardNumber { get; set; }
    public string BankName { get; set; }
    public string IssuingBank { get; set; }
    public string Mode { get; set; }
    public string AuthCode { get; set; }
    public string BankRefNum { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public string FirstName { get; set; }
    public DateTime? AddedOn { get; set; }
    public string PaymentSource { get; set; }
    public string ProductInfo { get; set; }
    public string ErrorMessage { get; set; }
    public string RawResponse { get; set; }
}

public class PaymentDto
    {
        public string TransactionRef { get; set; }
        public decimal Amount { get; set; }
        public string UpiId { get; set; }
        public string Message { get; set; }
    }

      
    

  
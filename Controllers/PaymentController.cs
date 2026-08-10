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
        public IActionResult VerifyPayment([FromBody] VerifyPaymentRequest req)
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

                string keySecret = _config["Razorpay:KeySecret"];
                string generatedSignature = GenerateSignature(req.razorpay_order_id, req.razorpay_payment_id, keySecret);

                if (generatedSignature == req.razorpay_signature)
                    return Ok(new { status = "success", message = "Payment verified successfully" });
                else
                    return BadRequest(new { status = "failed", message = "Payment verification failed" });
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

      
    

  
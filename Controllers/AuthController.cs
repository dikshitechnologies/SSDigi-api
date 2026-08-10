using CHITSCHEME.Helpers;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using static QRCoder.PayloadGenerator;

namespace CHITSCHEME.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;

        public AuthController(IConfiguration config)
        {
            _config = config;
        }
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Phone))
                return BadRequest(new { message = "Phone number is required." });

            if (request.Phone.Length != 10)
                return BadRequest(new { message = "Phone number must be 10 digits." });

            if (!IsPhoneNumberValid(request.Phone))
                return BadRequest(new { message = "Invalid phone number format." });

            try
            {
                var divisions = new List<object>();
                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                var responseDivisions = new
                {
                    divisions = new
                    {
                        gold = new List<object>(),
                        silver = new List<object>()
                    }
                };


                // -------- Check Party --------
                string partyName = null;
                string partyPhone = null;

                var partyCmd = new SqlCommand(@"
            SELECT TOP 1 FACNAME, FPHONE 
            FROM Party 
            WHERE faclevel < 0 
              AND fparent LIKE '0000100044%' 
              AND fphone = @phone
            ORDER BY FCODE", connection);
                partyCmd.Parameters.AddWithValue("@phone", request.Phone);

                using (var reader = await partyCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        partyName = reader["FACNAME"].ToString();
                        partyPhone = reader["FPHONE"].ToString();
                    }
                }

                // -------- Check RegisterUsers --------
                string username = string.Empty;
                string email = string.Empty;
                string userId = string.Empty;

                var regDetailsCmd = new SqlCommand(
                    "SELECT fcode, fAcname, fMail,FPHONE FROM party WHERE fparent like '000020000900015%' and fPhone= @phone",
                    connection);
                regDetailsCmd.Parameters.AddWithValue("@phone", request.Phone);

                using (var reader = await regDetailsCmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        userId = reader["fcode"].ToString();
                        username = reader["fAcname"].ToString();
                        email = reader["fMail"].ToString();
                        partyPhone = reader["FPHONE"].ToString();
                    }
                }
                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(partyPhone))
                {
                 

                    // Fetch Division Data
                    var divisionCmd = new SqlCommand(
                        @"SELECT fCode, fName, fRate 
            FROM Division 
           WHERE fCode IN ('0003','0002','0014','0004','0005')",
                        connection);

                    using (var reader = await divisionCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string code = reader["fCode"].ToString();
                            string name = reader["fName"].ToString();
                            decimal rate = Convert.ToDecimal(reader["fRate"]);

                            // GOLD: 14K, 18K, 22K, 24K
                            //if (code == "0003" || code == "0002" || code == "0014" || code == "0004")
                            //{
                            //    responseDivisions.divisions.gold.Add(new
                            //    {
                            //        name = name,
                            //        rate = rate
                            //    });
                            //}
                            if (code == "0002" )
                            {
                                responseDivisions.divisions.gold.Add(new
                                {
                                    name = "22K",
                                    rate = rate
                                });
                            }

                            // SILVER
                            if (code == "0005")
                            {
                                responseDivisions.divisions.silver.Add(new
                                {
                                    name = "SILVER",
                                    rate = rate
                                });
                            }
                        }
                    }

                   
                }


                // -------- If party exists and already registered --------
                if ((username != null && partyPhone != null) && userId != "")
                {
                    var token = JwtHelper.GenerateJwtToken(request.Phone, "User", _config);
                    return Ok(new { token, UserPermission = "U", UserId = userId, username, email, phone=partyPhone, responseDivisions });
                }

           

                // -------- Otherwise --------
                return Unauthorized(new { message = "Please check the phone number." });
            }
            catch (SqlException)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Database error. Please try again later." });
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred. Please try again later." });
            }
        }



        private bool IsPhoneNumberValid(string phone)
        {

            var regex = new Regex(@"^\d{10}$");
            return regex.IsMatch(phone);
        }



        [HttpGet("AdminValidate/{username}/{password}")]
        public async Task<IActionResult> AdminValidate([FromRoute] string username, [FromRoute] string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return BadRequest(new { message = "Username and password are required." });

            try
            {

                var responseDivisions = new
                {
                    divisions = new
                    {
                        gold = new List<object>(),
                        silver = new List<object>()
                    }
                };





                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();


                // Fetch Division Data
                var divisionCmd = new SqlCommand(
                    @"SELECT fCode, fName, fRate 
            FROM Division 
           WHERE fCode IN ('0003','0002','0014','0004','0005')",
                    connection);

                using (var reader1 = await divisionCmd.ExecuteReaderAsync())
                {
                    while (await reader1.ReadAsync())
                    {
                        string code = reader1["fCode"].ToString();
                        string name = reader1["fName"].ToString();
                        decimal rate = Convert.ToDecimal(reader1["fRate"]);

                        // GOLD: 14K, 18K, 22K, 24K
                        if (code == "0003" || code == "0002" || code == "0014" || code == "0004")
                        {
                            responseDivisions.divisions.gold.Add(new
                            {
                                name = name,
                                rate = rate
                            });
                        }

                        // SILVER
                        if (code == "0005")
                        {
                            responseDivisions.divisions.silver.Add(new
                            {
                                name = name,
                                rate = rate
                            });
                        }
                    }
                }
                // ✅ Updated: Select more columns
                var cmd = new SqlCommand(@"
            SELECT TOP 1 FCOMPCODE, FADMIN, PHONE1
            FROM COMPANY 
            WHERE FADMIN = @username AND FSUP = @password", connection);

                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@password", password);

                using var reader = await cmd.ExecuteReaderAsync();


                if (await reader.ReadAsync())
                {
                    string fcompcode = reader["FCOMPCODE"].ToString();
                    string adminName = reader["FADMIN"].ToString();
                    string phone = reader["PHONE1"].ToString();

                    // ✅ Optionally use phone in token
                    var token = JwtHelper.GenerateJwtToken(phone, "Admin", _config);

                    return Ok(new
                    {
                        role = "Admin",
                        token,
                        UserPermission = "A",
                        UserId = fcompcode,
                        AdminName = adminName,
                        Phone = phone,
                        responseDivisions
                    });
                }
                else
                {
                    return Unauthorized(new { message = "Invalid admin credentials." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error validating admin.", error = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpPost("guest-login")]
        public async Task<IActionResult> GuestLogin()
        {
            try
            {


                var responseDivisions = new
                {
                    divisions = new
                    {
                        gold = new List<object>(),
                        silver = new List<object>()
                    }
                };

                var connectionString = DBHelper.GetConnection();
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();

                // Fetch Division Data
                var divisionCmd = new SqlCommand(
                    @"SELECT fCode, fName, fRate 
            FROM Division 
           WHERE fCode IN ('0003','0002','0014','0004','0005')",
                    connection);

                using (var reader = await divisionCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string code = reader["fCode"].ToString();
                        string name = reader["fName"].ToString();
                        decimal rate = Convert.ToDecimal(reader["fRate"]);

                        // GOLD: 14K, 18K, 22K, 24K
                        if (code == "0003" || code == "0002" || code == "0014" || code == "0004")
                        {
                            responseDivisions.divisions.gold.Add(new
                            {
                                name = name,
                                rate = rate
                            });
                        }

                        // SILVER
                        if (code == "0005")
                        {
                            responseDivisions.divisions.silver.Add(new
                            {
                                name = name,
                                rate = rate
                            });
                        }
                    }
                }


                // Generate a random GuestId
                string guestId = Guid.NewGuid().ToString("N").Substring(0, 10);

                // Generate JWT token with Guest role
                var token = JwtHelper.GenerateJwtToken(guestId, "Guest", _config);

                return Ok(new
                {
                    role = "Guest",
                    token,
                    UserPermission = "G",
                    GuestId = guestId,
                    username = "Guest User",
                    email = "",
                    responseDivisions
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error creating guest login.", error = ex.Message });
            }
        }







    }
}

public class LoginRequest
{
    public string Phone { get; set; }
    public string FcmToken { get; set; }
    public string DeviceType { get; set; }
    public string DeviceId { get; set; }
}
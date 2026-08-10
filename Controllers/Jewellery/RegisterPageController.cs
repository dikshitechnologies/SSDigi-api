using CHITSCHEME.Helpers;
using CHITSCHEME.Models;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class RegisterPageController : ControllerBase
    {


        //---------------------------------------------Duplicate Name Checking ---------------------------------
        private bool RegistruserExists(SqlConnection con, string sectionName)
         {
            using (SqlCommand cmd = new SqlCommand("SELECT 1 FROM party   where fparent like '000020000900015%' and fphone=@PhoneNumber", con))
            {
                cmd.Parameters.AddWithValue("@PhoneNumber", sectionName);
                return cmd.ExecuteScalar() != null;
            }
        }



        [HttpPost("RegisterUser")]
        public async Task<IActionResult> RegisterUser([FromBody] RegisterUser model)
        {
            if (model == null)
                return BadRequest("Model cannot be null.");

            // ✅ Validate First Name
            if (string.IsNullOrWhiteSpace(model.Firstname) || model.Firstname.ToLower() == "string")
                return BadRequest("First name is empty.");
            if (model.Firstname.Length > 100)
                return BadRequest("First name cannot exceed 100 characters.");

            // ✅ Validate Phone Number
            if (string.IsNullOrWhiteSpace(model.Phonenumber) || model.Phonenumber.ToLower() == "string")
                return BadRequest("Phone number is empty.");
            if (model.Phonenumber.Length > 20)
                return BadRequest("Phone number cannot exceed 20 characters.");

            using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
            {
                await connection.OpenAsync();

                if (RegistruserExists(connection, model.Phonenumber))
                {
                    return Conflict(new { message = "Phonenumber  already exists" });
                }

                // ✅ Get max fcode from party
                string maxRegisterIdQuery = "SELECT MAX(fcode) FROM party";
                string insertPartyQuery = @"
            INSERT INTO party (fCode, fAcname, fParent, faclevel, fMail, fPhone, FDATE)
            VALUES (@fCode, @fAcname, @fParent, @faclevel, @fMail, @fPhone, @FDATE);
        ";

                try
                {
                    object result;
                    using (SqlCommand maxIdCommand = new SqlCommand(maxRegisterIdQuery, connection))
                    {
                        result = await maxIdCommand.ExecuteScalarAsync();
                    }

                    // ✅ Generate new fCode
                    string newUserCode;
                    if (result == DBNull.Value || result == null)
                    {
                        newUserCode = "00001";  // First user
                    }
                    else
                    {
                        string lastUserId = result.ToString();
                        if (int.TryParse(lastUserId, out int lastId))
                        {
                            int nextId = lastId + 1;
                            newUserCode = nextId.ToString("D5");
                        }
                        else
                        {
                            return StatusCode(500, new { message = "Invalid user ID format in database." });
                        }
                    }

                    // ✅ Generate fParent Code
                    string fparentCode = "000020000900015" + newUserCode;

                    // ✅ Insert new record into party
                    using (SqlCommand cmd = new SqlCommand(insertPartyQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@fCode", newUserCode);
                        cmd.Parameters.AddWithValue("@fAcname", model.Firstname);
                        cmd.Parameters.AddWithValue("@fParent", fparentCode);
                        cmd.Parameters.AddWithValue("@faclevel", "-4"); // Example: set level manually (adjust if needed)
                        cmd.Parameters.AddWithValue("@fMail", (object)model.Email ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@fPhone", model.Phonenumber);
                        cmd.Parameters.AddWithValue("@FDATE", DateTime.Now);

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                            return Ok(new { message = "User registered successfully in 'party' table" });
                        else
                            return StatusCode(500, "Failed to register user.");
                    }
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
                }
            }
        }


        [HttpGet("profilePage/{UserID}")]
        public IActionResult ProfilePage(string UserID)
        {
            try
            {
                var token = Request.Headers["Authorization"].ToString()
                   .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                   .Trim();

                if (string.IsNullOrWhiteSpace(token))
                    return Unauthorized("Token is missing or invalid.");


                if (!new JwtSecurityTokenHandler().CanReadToken(token))
                    return Unauthorized("Malformed JWT.");

                string role = JwtHelper.GetRoleFromJwtToken(token);

                if (string.IsNullOrEmpty(role))
                    return Unauthorized(new { message = "Invalid or expired token" });

                using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
                {
                    connection.Open();
                    string query;

                    if (role == "Admin")
                    {
                     
                        query = @"
                    SELECT 
                        fcompname AS UserName, 
                        PHONE1 AS Email,
                        '' AS AddressLine,
                        '' AS City,
                        '' AS State,
                        '' AS Pincode,
                        '' AS fprofileImg
                    FROM COMPANY
                    WHERE fcompcode = @UserID";
                    }
                    else
                    {
                        
                        query = @"
                     SELECT 
                         fAcname, 
                         fMail, 
                         ISNULL(fstreet, '') AS AddressLine,
                         ISNULL(fCity, '') AS City,
                         ISNULL(FSTAT, '') AS State,
                         ISNULL(fPincode, '') AS Pincode,
                         ISNULL(fImage, '') AS fprofileImg
                     FROM party 
                     WHERE fCode=@UserID";
                    }

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserID", UserID);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var result = new
                                {
                                    UserName = reader["fAcname"].ToString(),
                                    Email = reader["fMail"].ToString(),
                                    AddressLine = reader["AddressLine"].ToString(),
                                    City = reader["City"].ToString(),
                                    State = reader["State"].ToString(),
                                    Pincode = reader["Pincode"].ToString(),
                                    fprofileImg = reader["fprofileImg"].ToString()
                                };

                                return Ok(result);
                            }
                            else
                            {
                                return NotFound(new { message = "User not found" });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }


    }
}

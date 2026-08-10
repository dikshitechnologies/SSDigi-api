
using System.Text.Json.Serialization;
using CHITSCHEME.Global;
using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class DivisionController : ControllerBase
    {





        [HttpGet("getDivisionItems")]
        public IActionResult GetDivisionItems()
        {
            try
            {
                // Simulate fetching data from a database or service
                var divisionItems = new List<Division_Creation>();
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    con.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT FCODE,FNAME FROM DIVISION ORDER BY FCODE DESC", con))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var divisionItem = new Division_Creation
                                {
                                    DivisionCode = reader["FCODE"].ToString(),
                                    DivisionName = reader["FNAME"].ToString()
                                };
                                divisionItems.Add(divisionItem);
                            }
                        }
                    }
                }

                return Ok(divisionItems);
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "A database error occurred. Please try again later.");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again later.");
            }
        }


        //------------------------------------------------------Division Get Next Number --------------------------------------

        [HttpGet("divisionNextFcode")]
        public IActionResult GetNextFcode()
        {
            try
            {

                string query = "SELECT MAX(FCODE) AS Fcode FROM DIVISION";

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    con.Open();

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        var result = cmd.ExecuteScalar();

                        var maxFcode = result == DBNull.Value ? "0001" : result.ToString();

                        int fcodeLength = maxFcode.Length;

                        int nextFcodeValue = int.Parse(maxFcode) + 1;

                        string nextFcode = nextFcodeValue.ToString().PadLeft(fcodeLength, '0');


                        if (nextFcode.Length > fcodeLength)
                        {

                            nextFcode = nextFcodeValue.ToString();
                        }

                      
                        return Ok(new { nextcode = nextFcode });
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "A database error occurred. Please try again later.");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again later.");
            }
        }



        //---------------------------------------------Duplicate Name Checking ---------------------------------
        private bool DivisionNameExists(SqlConnection con, string divisionName)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT 1 FROM DIVISION WHERE FNAME = @divisionName", con))
            {
                cmd.Parameters.AddWithValue("@divisionName", divisionName);
                return cmd.ExecuteScalar() != null;
            }
        }



        //------------------------------------------------------Division Crate ------------------------------------
        [HttpPost("createDivision")]
        public async Task<IActionResult> CreateDivision([FromBody] AddDivision newDivision)
        {
            if (newDivision == null)
            {
                return BadRequest("Division data is required.");
            }

            if (string.IsNullOrEmpty(newDivision.DivisionName))
            {
                return BadRequest("DivisionCode andDivisionName are required.");
            }

            if (newDivision.DivisionName.Length > 25)
            {
                return BadRequest("DivisionCode should be max 10 characters and DivisionName max 25 characters.");
            }

            string query = "INSERT INTO DIVISION (FCODE, FNAME) VALUES (@DivisionCode, @DivisionName)";

            try
            {
                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    await con.OpenAsync();

                    if (DivisionNameExists(con, newDivision.DivisionName))
                    {
                        return Conflict(new { message = "Division name already exists" });
                    }
                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {

                        cmd.Parameters.AddWithValue("@DivisionCode", newDivision.DivisionCode);
                        cmd.Parameters.AddWithValue("@DivisionName", newDivision.DivisionName.ToUpper());

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            return Ok("Division item created successfully.");
                        }
                        else
                        {
                            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to create Division item.");
                        }
                    }
                }
            }
            catch (SqlException sqlEx)
            {

                return StatusCode(StatusCodes.Status500InternalServerError, "A database error occurred. Please try again later.");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again later.");
            }
        }

        //------------------------------------------------------Division Update --------------------------------------
        [HttpPut("updateDivision")]
        public async Task<IActionResult> UpdateDivisionName([FromBody] AddDivision newUpdateDivision)
        {
            try
            {
                if (string.IsNullOrEmpty(newUpdateDivision.DivisionCode) || string.IsNullOrEmpty(newUpdateDivision.DivisionName))
                {
                    return BadRequest("DivisionCode and DivisionName are required.");
                }

                string query = "UPDATE DIVISION SET FNAME = @DivisionName WHERE FCODE = @DivisionCode";

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    con.Open();

                    if (DivisionNameExists(con, newUpdateDivision.DivisionName))
                    {
                        return Conflict(new { message = "Division name already exists. Please choose a different name." });
                    }

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {

                        cmd.Parameters.AddWithValue("@DivisionName", newUpdateDivision.DivisionName);
                        cmd.Parameters.AddWithValue("@DivisionCode", newUpdateDivision.DivisionCode);

                        int rowsAffected = cmd.ExecuteNonQuery();

                        if (rowsAffected == 0)
                        {
                            return NotFound("Division item not found.");
                        }

                        return Ok("Division item updated successfully.");
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "A database error occurred. Please try again later.");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again later.");
            }
        }


        //------------------------------------------------------Division Delete --------------------------------------
        [HttpDelete("deleteDivision/{divisionCode}")]
        public async Task<IActionResult> DeleteDivision([FromRoute] string divisionCode)
        {
            try
            {
                if (string.IsNullOrEmpty(divisionCode))
                {
                    return BadRequest("DivisionCode is required.");
                }



                var checkIfValueExists = new CheckIfValueExists();

                bool isUsedInItemTransaction = await checkIfValueExists.DoesValueExist("ITEMTRANSACTION", "FDIV", divisionCode);
                bool isUsedInItemPurchase = await checkIfValueExists.DoesValueExist("ITEMPURCHASE", "FDIV", divisionCode);


                if (isUsedInItemTransaction || isUsedInItemPurchase)
                {
                    return Conflict(new { message = "Division Name is used in related tables and cannot be deleted." });
                }

                string query = "DELETE FROM DIVISION WHERE FCODE = @DivisionCode";

                using (SqlConnection con = new SqlConnection(DBHelper.GetConnection()))
                {
                    con.Open();

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@DivisionCode", divisionCode);

                        int rowsAffected = cmd.ExecuteNonQuery();

                        if (rowsAffected == 0)
                        {
                            return NotFound("Division item not found.");
                        }

                        return Ok("Division item deleted successfully.");
                    }
                }
            }
            catch (SqlException sqlEx)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "A database error occurred. Please try again later.");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again later.");
            }
        }


        public class Division_Creation
        {
            [JsonPropertyName("fcode")]
            public string DivisionCode { get; set; }
            [JsonPropertyName("fname")]
            public string DivisionName { get; set; }


        }


        public class AddDivision
        {
            [JsonPropertyName("fcode")]
            public string DivisionCode { get; set; }
            [JsonPropertyName("fname")]
            public string DivisionName { get; set; }


        }

    }
}

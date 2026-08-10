using JEWELLBISREACT.DBConnection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class GetPartyListController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetPartyList(
            int page = 1,
            int pageSize = 10,
            string search = "")
        {
            try
            {
                string connectionString = DBHelper.GetConnection();

                List<object> partyList = new List<object>();
                int totalRecords = 0;

                using (SqlConnection con = new SqlConnection(connectionString))
                {
                    con.Open();

                    // Total Count Query
                    string countQuery = @"
                        SELECT COUNT(*)
                        FROM PARTY
                        WHERE FPARENT LIKE '0000100044%'
                        AND fAclevel < 0
                        AND fCode <> '00045'
                        AND fShow = '1'
                        AND (
                            @search = ''
                            OR fCode LIKE '%' + @search + '%'
                            OR fAcname LIKE '%' + @search + '%'
                        )";

                    using (SqlCommand countCmd = new SqlCommand(countQuery, con))
                    {
                        countCmd.Parameters.AddWithValue("@search", search ?? "");

                        totalRecords = Convert.ToInt32(countCmd.ExecuteScalar());
                    }

                    // Pagination Query
                    string query = @"
                        SELECT fCode, fAcname
                        FROM PARTY
                        WHERE FPARENT LIKE '0000100044%'
                        AND fAclevel < 0
                        AND fCode <> '00045'
                        AND fShow = '1'
                        AND (
                            @search = ''
                            OR fCode LIKE '%' + @search + '%'
                            OR fAcname LIKE '%' + @search + '%'
                        )
                        ORDER BY fAcname
                        OFFSET @Offset ROWS
                        FETCH NEXT @PageSize ROWS ONLY";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        cmd.Parameters.AddWithValue("@search", search ?? "");
                        cmd.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
                        cmd.Parameters.AddWithValue("@PageSize", pageSize);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                partyList.Add(new
                                {
                                    fCode = reader["fCode"]?.ToString(),
                                    fAcname = reader["fAcname"]?.ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(new
                {
                    page,
                    pageSize,
                    totalRecords,
                    totalPages = (int)Math.Ceiling((double)totalRecords / pageSize),
                    data = partyList
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching party list",
                    error = ex.Message
                });
            }
        }
    }
}
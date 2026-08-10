using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Data;
using CHITSCHEME.Helpers;
using JEWELLBISREACT.DBConnection;

namespace CHITSCHEME.Controllers.Jewellery
{
    [Route("api/[controller]")]
    [ApiController]
    public class PartyListController : ControllerBase
    {
        [HttpGet("GetPartyList")]
        //public IActionResult GetPartyList(
        //    string? search = null,
        //    int page = 1,

        //    int pageSize = 10)
        //{
        //    // 🔐 JWT Validation
        //    var token = Request.Headers["Authorization"]
        //        .ToString()
        //        .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
        //        .Trim();

        //    if (string.IsNullOrWhiteSpace(token))
        //        return Unauthorized("Token is missing or invalid.");

        //    if (!new JwtSecurityTokenHandler().CanReadToken(token))
        //        return Unauthorized("Malformed JWT.");

        //    string role = JwtHelper.GetRoleFromJwtToken(token);

        //    if (string.IsNullOrEmpty(role))
        //        return Unauthorized(new { message = "Invalid or expired token" });

        //    // Pagination safety
        //    if (page < 1) page = 1;
        //    if (pageSize < 1) pageSize = 10;

        //    int offset = (page - 1) * pageSize;

        //    var data = new List<object>();
        //    int totalRecords = 0;

        //    using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
        //    {
        //        connection.Open();

        //        // 🔢 Total Count Query
        //        string countQuery = @"
        //            SELECT COUNT(*)
        //            FROM Party
        //            WHERE fparent LIKE @fparent
        //              AND fAclevel < 0
        //              AND (
        //                    @search IS NULL
        //                    OR fcode LIKE '%' + @search + '%'
        //                    OR facname LIKE '%' + @search + '%'
        //                  )";

        //        using (SqlCommand countCmd = new SqlCommand(countQuery, connection))
        //        {
        //            countCmd.Parameters.AddWithValue("@fparent", "000020000900015%");
        //            countCmd.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);

        //            totalRecords = (int)countCmd.ExecuteScalar();
        //        }

        //        // 📄 Paged Data Query
        //        string dataQuery = @"
        //            SELECT fcode, facname,FPHONE
        //            FROM Party
        //            WHERE fparent LIKE @fparent
        //              AND fAclevel < 0
        //              AND (
        //                    @search IS NULL
        //                    OR fcode LIKE '%' + @search + '%'
        //                    OR facname LIKE '%' + @search + '%'
        //                  )
        //            ORDER BY facname
        //            OFFSET @offset ROWS
        //            FETCH NEXT @pageSize ROWS ONLY";

        //        using (SqlCommand cmd = new SqlCommand(dataQuery, connection))
        //        {
        //            cmd.Parameters.AddWithValue("@fparent", "000020000900015%");
        //            cmd.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);
        //            cmd.Parameters.AddWithValue("@offset", offset);
        //            cmd.Parameters.AddWithValue("@pageSize", pageSize);

        //            using (SqlDataReader reader = cmd.ExecuteReader())
        //            {
        //                while (reader.Read())
        //                {
        //                    data.Add(new
        //                    {
        //                        fcode = reader["fcode"].ToString(),
        //                        facname = reader["facname"].ToString(),
        //                        FPHONE = reader["FPHONE"].ToString()
        //                    });
        //                }
        //            }
        //        }
        //    }

        //    return Ok(new
        //    {
        //        page,
        //        pageSize,
        //        totalRecords,
        //        totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),
        //        data
        //    });
        //}


        public IActionResult GetPartyList(
    string? search = "",
    int page = 1,
    int pageSize = 10)
        {
            // 🔐 JWT Validation
            var token = Request.Headers["Authorization"]
                .ToString()
                .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (string.IsNullOrWhiteSpace(token))
                return Unauthorized("Token is missing or invalid.");

            if (!new JwtSecurityTokenHandler().CanReadToken(token))
                return Unauthorized("Malformed JWT.");

            string role = JwtHelper.GetRoleFromJwtToken(token);

            if (string.IsNullOrEmpty(role))
                return Unauthorized(new { message = "Invalid or expired token" });

            // Pagination safety
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            int offset = (page - 1) * pageSize;

            var data = new List<object>();
            int totalRecords = 0;

            using (SqlConnection connection = new SqlConnection(DBHelper.GetConnection()))
            {
                connection.Open();

                // 🔢 Total Count Query
            //    string countQuery = @"
            //SELECT COUNT(*)
            //FROM Party
            //WHERE fparent LIKE @fparent
            //  AND fAclevel < 0
            //  AND (@search IS NULL OR @search = '' 
            //       OR fcode LIKE '%' + @search + '%' 
            //       OR facname LIKE '%' + @search + '%')";
                string countQuery = @"
            SELECT COUNT(*)
            FROM RegisterUsers where 
               (@search IS NULL OR @search = '' 
                   OR userId LIKE '%' + @search + '%' 
                   OR Username LIKE '%' + @search + '%')";

                using (SqlCommand countCmd = new SqlCommand(countQuery, connection))
                {
                    //countCmd.Parameters.AddWithValue("@fparent", "000020000900015%");
                    countCmd.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);

                    totalRecords = (int)countCmd.ExecuteScalar();
                }

                // 📄 Paged Data Query
                //    string dataQuery = @"
                //SELECT fcode, facname, FPHONE
                //FROM Party
                //WHERE fparent LIKE @fparent
                //  AND fAclevel < 0
                //  AND (@search IS NULL OR @search = '' 
                //       OR fcode LIKE '%' + @search + '%' 
                //       OR facname LIKE '%' + @search + '%')
                //ORDER BY facname
                //OFFSET @offset ROWS
                //FETCH NEXT @pageSize ROWS ONLY";
                string dataQuery = @"
            	   SELECT UserID, UserName, PhoneNumber
                    FROM RegisterUsers
                    WHERE 
                       (@search IS NULL OR @search = '' 
                           OR UserID LIKE '%' + @search + '%' 
                           OR UserName LIKE '%' + @search + '%')
                    ORDER BY UserName
                    OFFSET @offset ROWS
                    FETCH NEXT @pageSize ROWS ONLY";

                using (SqlCommand cmd = new SqlCommand(dataQuery, connection))
                {
                    //cmd.Parameters.AddWithValue("@fparent", "000020000900015%");
                    cmd.Parameters.AddWithValue("@search", (object?)search ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    cmd.Parameters.AddWithValue("@pageSize", pageSize);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            data.Add(new
                            {
                                fcode = reader["UserID"].ToString(),
                                facname = reader["UserName"].ToString(),
                                FPHONE = reader["PhoneNumber"].ToString()
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
                totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize),
                data
            });
        }

    }
}

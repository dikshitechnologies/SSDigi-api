
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CHITSCHEME.Helpers
{
    public static class JwtHelper
    {
        public static string GenerateJwtToken(string phone, string role, IConfiguration config)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, phone),
                new Claim(ClaimTypes.Role, role)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: config["Jwt:Issuer"],
                    audience: config["Jwt:Audience"],
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(Convert.ToDouble(config["Jwt:ExpireMinutes"] ?? "60")),
                    signingCredentials: creds
                );

                return new JwtSecurityTokenHandler().WriteToken(token);
            }


        //------------------------------------Extract tocken phone number ----------------------------------
        public static string GetPhoneFromJwtToken(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null || jsonToken.ValidTo < DateTime.UtcNow)
            {
                return null; 
            }

            var phoneClaim = jsonToken?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

            return phoneClaim?.Value;
        }
        public static string GetRoleFromJwtToken(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null || jsonToken.ValidTo < DateTime.UtcNow)
                return null;

            var roleClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role);
            return roleClaim?.Value;
        }


    }
}

    

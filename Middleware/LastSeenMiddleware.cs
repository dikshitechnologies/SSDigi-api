using JEWELLBISREACT.DBConnection;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace CHITSCHEME.Middleware
{
    /// <summary>
    /// Automatically updates UserActivity.LastSeen = GETDATE() on every
    /// authenticated API request. Frontend doesn't need to do anything extra.
    ///
    /// The CustomerCode is read from the "sub" or "nameid" JWT claim, which
    /// is the phone number used during login. We match it against UserActivity
    /// using the CustomerCode stored there.
    ///
    /// Skipped for: guest users, swagger, static files, and the login endpoint
    /// itself (login handler already sets LastSeen via /api/UserActivity/login).
    /// </summary>
    public class LastSeenMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LastSeenMiddleware> _logger;

        // Paths to skip
        private static readonly HashSet<string> _skipPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "/swagger",
            "/offerimg",
            "/thumbnails",
            "/notification",
            "/uploads"
        };

        private static readonly HashSet<string> _skipPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/UserActivity/login",
            "/api/UserActivity/last-seen",
            "/api/AuthReg/login",
            "/api/Auth/login"
        };

        public LastSeenMiddleware(RequestDelegate next, ILogger<LastSeenMiddleware> logger)
        {
            _next   = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            await _next(context);   // Let the request complete first

            try
            {
                var path = context.Request.Path.Value ?? string.Empty;

                // Skip non-API and excluded paths
                if (_skipPaths.Contains(path)) return;
                if (_skipPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return;
                if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return;

                // Only for authenticated, non-guest users
                var user = context.User;
                if (user?.Identity?.IsAuthenticated != true) return;

                var role = user.FindFirst(ClaimTypes.Role)?.Value
                        ?? user.FindFirst("role")?.Value;
                if (role == "Guest") return;

                // CustomerCode stored as phone in the JWT "sub" / "nameid" claim
                var customerCode = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                ?? user.FindFirst("sub")?.Value;

                if (string.IsNullOrWhiteSpace(customerCode)) return;

                // Fire-and-forget DB update — don't block response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var connStr = DBHelper.GetConnection();
                        using var conn = new SqlConnection(connStr);
                        await conn.OpenAsync();

                        using var cmd = new SqlCommand(@"
                            IF EXISTS (SELECT 1 FROM UserActivity WHERE CustomerCode = @Code)
                                UPDATE UserActivity SET LastSeen = GETDATE() WHERE CustomerCode = @Code",
                            conn);
                        cmd.Parameters.AddWithValue("@Code", customerCode);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("LastSeenMiddleware update failed: {Msg}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("LastSeenMiddleware outer error: {Msg}", ex.Message);
            }
        }
    }

    // Extension method for clean registration in Program.cs
    public static class LastSeenMiddlewareExtensions
    {
        public static IApplicationBuilder UseLastSeen(this IApplicationBuilder app)
            => app.UseMiddleware<LastSeenMiddleware>();
    }
}

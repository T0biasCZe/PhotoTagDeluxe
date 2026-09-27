using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace PhotoTag.Server {
    public static class JwtHelper {
        public static string GetUsernameFromToken(string token) {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var username = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                ?? jwt.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value
                ?? jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
            return username;
        }

        public static string ValidateAndGetUsername(string token, string jwtSecret) {
            var handler = new JwtSecurityTokenHandler();
            try {
                var validationParams = new Microsoft.IdentityModel.Tokens.TokenValidationParameters {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSecret))
                };
                var principal = handler.ValidateToken(token, validationParams, out var validatedToken);
                return principal.Identity.Name
                    ?? principal.FindFirst("unique_name")?.Value
                    ?? principal.FindFirst("sub")?.Value;
            } catch {
                return null;
            }
        }
    }
}

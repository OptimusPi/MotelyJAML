using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Motely.MCP;

/// <summary>
/// Mints a short-lived local JWT for development, so a client can authenticate against the
/// server's JWT Bearer handler (which, with no <c>Mcp:Authority</c> configured, validates against
/// the same symmetric key). Dev-only: production sets <c>Mcp:Authority</c> and a real issuer.
/// </summary>
internal static class DevTokenIssuer
{
    public static string Mint(string signingKey, string audience)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "motely-dev",
            audience: audience,
            claims:
            [
                new Claim(ClaimTypes.Name, "dev"),
                new Claim(ClaimTypes.Role, "user"),
                new Claim("mcp:scope", "mcp:tools")
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

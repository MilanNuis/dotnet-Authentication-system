using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AuthApi.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace AuthApi.Api.Services;

/// <summary>
/// Creates JWTs and refresh tokens. Kept small on purpose for learning —
/// no repository/factory layers hiding the auth behavior.
/// </summary>
public class TokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Builds a 15-minute JWT access token.
    /// Claims are key/value pairs embedded in the token (like Laravel Sanctum
    /// abilities / JWT "payload" fields). Controllers read them via User.Claims.
    /// </summary>
    public string CreateAccessToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is missing from configuration.");

        var claims = new List<Claim>
        {
            // NameIdentifier is the standard ASP.NET claim for user id (maps to ClaimTypes.NameIdentifier).
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Cryptographically random opaque token. Returned to the client once;
    /// only HashRefreshToken(...) is stored in PostgreSQL.
    /// </summary>
    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Deterministic SHA-256 hash used as the session lookup key.
    /// Unlike PasswordHasher, this is not salted — we need the same input
    /// to always produce the same hash so we can Find the Session by hash.
    /// </summary>
    public string HashRefreshToken(string refreshToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hashBytes);
    }
}

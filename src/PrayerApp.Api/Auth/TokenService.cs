using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Auth;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Creates access tokens and raw refresh tokens. No database access.</summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(User user)
    {
        var now = time.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(_options.GetSecurityKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [JwtRegisteredClaimNames.Name] = user.DisplayName,
            },
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }

    /// <summary>256 bits from a cryptographic RNG, base64url-encoded (43 chars, cookie-safe).</summary>
    public static string GenerateRefreshToken() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// A plain SHA-256 is enough here, unlike passwords: the input is 256 random bits,
    /// so there's nothing to brute-force. Slow hashes (PBKDF2, bcrypt) exist to protect
    /// low-entropy human-chosen secrets.
    /// </summary>
    public static byte[] HashRefreshToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

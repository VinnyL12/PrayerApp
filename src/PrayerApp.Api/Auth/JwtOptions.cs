using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;

namespace PrayerApp.Api.Auth;

/// <summary>
/// Bound from the "Jwt" config section. SigningKey comes from user secrets locally
/// (Jwt__SigningKey env var in containers), never from appsettings.json.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "";

    [Required]
    public string Audience { get; init; } = "";

    // Base64 of at least 32 random bytes: HS256 needs a 256-bit key.
    [Required, MinLength(44)]
    public string SigningKey { get; init; } = "";

    [Range(1, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 30)]
    public int RefreshTokenDays { get; init; } = 7;

    public SymmetricSecurityKey GetSecurityKey() => new(Convert.FromBase64String(SigningKey));
}

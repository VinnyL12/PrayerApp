using System.ComponentModel.DataAnnotations;

namespace PrayerApp.Shared.Contracts.Auth;

public sealed record RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }

    [Required, MaxLength(50)]
    public required string DisplayName { get; init; }
}

public sealed record LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}

/// <summary>
/// Returned by register, login and refresh. The refresh token is deliberately not in
/// the body: it travels only in an httpOnly cookie that client-side code can't read.
/// </summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record UserDto(Guid Id, string Email, string DisplayName);

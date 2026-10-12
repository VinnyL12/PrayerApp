using System.ComponentModel.DataAnnotations;

namespace PrayerApp.Shared.Contracts.Auth;

// Request types use { get; set; } rather than { get; init; } so Blazor forms can bind to
// them directly. The payoff: the same [Required]/[MaxLength] attributes validate in the
// browser (instant feedback) and on the server (the real enforcement).

public sealed record RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; set; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; set; }

    [Required, MaxLength(50), Display(Name = "Display name")]
    public required string DisplayName { get; set; }
}

public sealed record LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; set; }

    [Required]
    public required string Password { get; set; }
}

/// <summary>
/// Returned by register, login and refresh. The refresh token is deliberately not in
/// the body: it travels only in an httpOnly cookie that client-side code can't read.
/// </summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record UserDto(Guid Id, string Email, string DisplayName);

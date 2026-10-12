using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Client.Auth;

/// <summary>
/// Holds the access token in memory only: not localStorage, not sessionStorage. A
/// page reload loses it, and AuthService gets a new one using the httpOnly refresh
/// cookie, which scripts (including injected ones) can never read.
/// </summary>
public sealed class TokenStore
{
    public string? AccessToken { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public UserDto? User { get; private set; }

    /// <summary>Raised when the user signs in, out, or their session is renewed.</summary>
    public event Action? Changed;

    public void Set(AuthResponse auth)
    {
        AccessToken = auth.AccessToken;
        ExpiresAt = auth.ExpiresAt;
        User = auth.User;
        Changed?.Invoke();
    }

    public void Clear()
    {
        AccessToken = null;
        ExpiresAt = default;
        User = null;
        Changed?.Invoke();
    }
}

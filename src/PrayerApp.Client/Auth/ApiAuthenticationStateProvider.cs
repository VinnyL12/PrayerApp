using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Client.Auth;

/// <summary>
/// Tells Blazor who is signed in, which drives <c>&lt;AuthorizeView&gt;</c> and
/// <c>[Authorize]</c> pages. Think of it as the provider behind a React useAuth() hook.
///
/// It only affects what the UI shows. The client never decodes or trusts the JWT for
/// security; the API validates the token on every request.
/// </summary>
public sealed class ApiAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly TokenStore _store;
    private readonly AuthService _auth;

    public ApiAuthenticationStateProvider(TokenStore store, AuthService auth)
    {
        _store = store;
        _auth = auth;
        _store.Changed += OnTokenStoreChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _auth.EnsureSessionRestoredAsync();
        return BuildState(_store.User);
    }

    public void Dispose() => _store.Changed -= OnTokenStoreChanged;

    private void OnTokenStoreChanged() =>
        NotifyAuthenticationStateChanged(Task.FromResult(BuildState(_store.User)));

    private static AuthenticationState BuildState(UserDto? user)
    {
        if (user is null)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
        ];

        // Makes User.IsInRole("Admin"), <AuthorizeView Roles="Admin"> and
        // [Authorize(Roles = "Admin")] work. UI only: the API makes its own decision.
        if (user.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.Admin));
        }

        // Passing an authenticationType is what makes IsAuthenticated true.
        var identity = new ClaimsIdentity(claims, authenticationType: "PrayerApp");

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}

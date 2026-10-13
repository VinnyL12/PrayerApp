using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using PrayerApp.Api.Admin;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Api.Auth;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "prayerapp_refresh";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        // Everything else in the API requires a token (fallback policy in Program.cs).
        group.MapPost("/register", Register).AllowAnonymous();
        group.MapPost("/login", Login).AllowAnonymous();
        group.MapPost("/refresh", Refresh).AllowAnonymous();
        group.MapPost("/logout", Logout).AllowAnonymous();
        group.MapGet("/me", Me);

        return app;
    }

    private static async Task<Results<Ok<AuthResponse>, ValidationProblem>> Register(
        RegisterRequest request,
        UserManager<User> users,
        TokenService tokens,
        RefreshTokenService refreshTokens,
        AdminAccess admins,
        TimeProvider time,
        HttpContext http,
        CancellationToken ct)
    {
        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = time.GetUtcNow(),
        };

        // Identity hashes the password (PBKDF2) and enforces the password rules.
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await SignInAsync(user, tokens, refreshTokens, admins, http, ct));
    }

    private static async Task<Results<Ok<AuthResponse>, ProblemHttpResult>> Login(
        LoginRequest request,
        UserManager<User> users,
        TokenService tokens,
        RefreshTokenService refreshTokens,
        AdminAccess admins,
        HttpContext http,
        CancellationToken ct)
    {
        // One generic message for every failure, so the response doesn't reveal
        // whether an email is registered or an account is locked.
        var invalid = TypedResults.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || await users.IsLockedOutAsync(user))
        {
            return invalid;
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            // Counts toward lockout (5 failures -> 15 minutes, configured in Program.cs).
            await users.AccessFailedAsync(user);
            return invalid;
        }

        await users.ResetAccessFailedCountAsync(user);
        return TypedResults.Ok(await SignInAsync(user, tokens, refreshTokens, admins, http, ct));
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> Refresh(
        TokenService tokens,
        RefreshTokenService refreshTokens,
        AdminAccess admins,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.Request.Cookies.TryGetValue(RefreshCookieName, out var presented))
        {
            return TypedResults.Unauthorized();
        }

        var rotation = await refreshTokens.RotateAsync(presented, ct);
        if (rotation is null)
        {
            DeleteRefreshCookie(http);
            return TypedResults.Unauthorized();
        }

        SetRefreshCookie(http, rotation.RefreshToken);

        // Re-read on every refresh, so a promotion or demotion reaches the UI within one
        // access-token lifetime without signing out.
        return TypedResults.Ok(await CreateResponseAsync(rotation.User, tokens.CreateAccessToken(rotation.User), admins, ct));
    }

    private static async Task<NoContent> Logout(RefreshTokenService refreshTokens, HttpContext http, CancellationToken ct)
    {
        if (http.Request.Cookies.TryGetValue(RefreshCookieName, out var presented))
        {
            await refreshTokens.RevokeAsync(presented, ct);
        }

        DeleteRefreshCookie(http);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<UserDto>, UnauthorizedHttpResult>> Me(
        ClaimsPrincipal principal,
        UserManager<User> users,
        AdminAccess admins,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var user = userId is null ? null : await users.FindByIdAsync(userId);

        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(await ToDtoAsync(user, admins, ct));
    }

    private static async Task<AuthResponse> SignInAsync(
        User user, TokenService tokens, RefreshTokenService refreshTokens, AdminAccess admins, HttpContext http, CancellationToken ct)
    {
        var refresh = await refreshTokens.IssueAsync(user.Id, ct);
        SetRefreshCookie(http, refresh);
        return await CreateResponseAsync(user, tokens.CreateAccessToken(user), admins, ct);
    }

    private static async Task<AuthResponse> CreateResponseAsync(User user, AccessToken access, AdminAccess admins, CancellationToken ct) =>
        new(access.Token, access.ExpiresAt, await ToDtoAsync(user, admins, ct));

    // The role is deliberately not a claim in the access token: the API checks it fresh
    // per admin request. This flag only tells the client which links to show.
    private static async Task<UserDto> ToDtoAsync(User user, AdminAccess admins, CancellationToken ct) =>
        new(user.Id, user.Email!, user.DisplayName, await admins.IsAdminAsync(user.Id, ct));

    private static void SetRefreshCookie(HttpContext http, IssuedRefreshToken refresh) =>
        http.Response.Cookies.Append(RefreshCookieName, refresh.Token, CreateCookieOptions(refresh.ExpiresAt));

    private static void DeleteRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(RefreshCookieName, CreateCookieOptions(expiresAt: null));

    private static CookieOptions CreateCookieOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,                 // invisible to JavaScript, so XSS can't read it
        Secure = true,                   // HTTPS only (browsers exempt localhost)
        SameSite = SameSiteMode.Strict,  // never sent on requests from other sites (CSRF)
        Path = "/auth",                  // only sent to auth endpoints, not every API call
        Expires = expiresAt,
        IsEssential = true,
    };
}

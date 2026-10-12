using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Api.Auth;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "prayerapp_refresh";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", Register);
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout);
        group.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AuthResponse>, ValidationProblem>> Register(
        RegisterRequest request,
        UserManager<User> users,
        TokenService tokens,
        RefreshTokenService refreshTokens,
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

        return TypedResults.Ok(await SignInAsync(user, tokens, refreshTokens, http, ct));
    }

    private static async Task<Results<Ok<AuthResponse>, ProblemHttpResult>> Login(
        LoginRequest request,
        UserManager<User> users,
        TokenService tokens,
        RefreshTokenService refreshTokens,
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
        return TypedResults.Ok(await SignInAsync(user, tokens, refreshTokens, http, ct));
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> Refresh(
        TokenService tokens,
        RefreshTokenService refreshTokens,
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
        return TypedResults.Ok(CreateResponse(rotation.User, tokens.CreateAccessToken(rotation.User)));
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
        UserManager<User> users)
    {
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var user = userId is null ? null : await users.FindByIdAsync(userId);

        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(ToDto(user));
    }

    private static async Task<AuthResponse> SignInAsync(
        User user, TokenService tokens, RefreshTokenService refreshTokens, HttpContext http, CancellationToken ct)
    {
        var refresh = await refreshTokens.IssueAsync(user.Id, ct);
        SetRefreshCookie(http, refresh);
        return CreateResponse(user, tokens.CreateAccessToken(user));
    }

    private static AuthResponse CreateResponse(User user, AccessToken access) =>
        new(access.Token, access.ExpiresAt, ToDto(user));

    private static UserDto ToDto(User user) => new(user.Id, user.Email!, user.DisplayName);

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

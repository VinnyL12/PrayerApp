using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PrayerApp.Api.Auth;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Tests.Auth;

public class TokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        AccessTokenMinutes = 15,
    };

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly User _user = new() { Id = Guid.NewGuid(), DisplayName = "Test User" };

    private TokenService CreateService() => new(Microsoft.Extensions.Options.Options.Create(Options), _time);

    private static Task<TokenValidationResult> ValidateAsync(string token, SecurityKey key) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = key,
        });

    [Fact]
    public async Task Access_token_validates_and_carries_user_claims()
    {
        var token = CreateService().CreateAccessToken(_user);

        var result = await ValidateAsync(token.Token, Options.GetSecurityKey());

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(_user.Id.ToString(), result.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal("Test User", result.Claims[JwtRegisteredClaimNames.Name]);
    }

    [Fact]
    public void Access_token_expires_after_configured_minutes()
    {
        var token = CreateService().CreateAccessToken(_user);

        Assert.Equal(_time.GetUtcNow().AddMinutes(15), token.ExpiresAt);
    }

    [Fact]
    public async Task Access_token_signed_with_a_different_key_is_rejected()
    {
        var token = CreateService().CreateAccessToken(_user);
        var attackerKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));

        var result = await ValidateAsync(token.Token, attackerKey);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Refresh_tokens_are_unique_and_cookie_safe()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => TokenService.GenerateRefreshToken()).ToList();

        Assert.Equal(tokens.Count, tokens.ToHashSet().Count);
        Assert.All(tokens, t =>
        {
            Assert.Equal(43, t.Length); // 32 bytes -> 43 base64url chars, no padding
            Assert.Matches("^[A-Za-z0-9_-]+$", t);
        });
    }

    [Fact]
    public void Refresh_token_hash_is_deterministic_and_32_bytes()
    {
        var token = TokenService.GenerateRefreshToken();

        var first = TokenService.HashRefreshToken(token);
        var second = TokenService.HashRefreshToken(token);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, TokenService.HashRefreshToken(TokenService.GenerateRefreshToken()));
    }
}

using System.Net;
using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class AuthFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Endpoints_require_a_token_by_default()
    {
        var anonymous = factory.CreateApiClient();

        var response = await anonymous.GetAsync("/communities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registered_user_can_call_me()
    {
        var alice = await factory.RegisterAsync("Alice");

        var me = await (await alice.Client.GetAsync("/auth/me")).ReadAsync<UserDto>();

        Assert.Equal(alice.User.Id, me.Id);
        Assert.Equal("Alice", me.DisplayName);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_replaying_the_old_one_revokes_the_session()
    {
        var alice = await factory.RegisterAsync();
        var client = factory.CreateApiClient();

        var first = await RefreshAsync(client, alice.RefreshCookie);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var rotatedCookie = TestUsers.RefreshCookieFrom(first);
        Assert.NotEqual(alice.RefreshCookie, rotatedCookie);

        // An attacker replays the original (already used) token.
        var replay = await RefreshAsync(client, alice.RefreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The whole family is revoked, so the legitimate new token is dead too.
        var afterReplay = await RefreshAsync(client, rotatedCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
    }

    [Fact]
    public async Task Concurrent_refreshes_with_the_same_token_succeed_at_most_once()
    {
        var alice = await factory.RegisterAsync();
        var client = factory.CreateApiClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => RefreshAsync(client, alice.RefreshCookie)));

        // The atomic compare-and-swap in RefreshTokenService lets exactly one through.
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var alice = await factory.RegisterAsync();
        var client = factory.CreateApiClient();

        var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        logout.Headers.Add("Cookie", alice.RefreshCookie);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, alice.RefreshCookie)).StatusCode);
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }
}

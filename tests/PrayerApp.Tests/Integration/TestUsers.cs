using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Tests.Integration;

/// <summary>A registered user with an HttpClient that sends their access token.</summary>
public sealed record TestUser(HttpClient Client, UserDto User, string RefreshCookie);

public static class TestUsers
{
    // Must match the API: enums as names.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public const string Password = "CorrectHorse9";

    public static async Task<TestUser> RegisterAsync(this ApiFactory factory, string displayName = "Test User")
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = Password,
            DisplayName = displayName,
        }, Json);
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return new TestUser(client, auth.User, RefreshCookieFrom(response));
    }

    /// <summary>Extracts "prayerapp_refresh=value" from a Set-Cookie header.</summary>
    public static string RefreshCookieFrom(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Select(header => header.Split(';')[0])
            .Single(pair => pair.StartsWith("prayerapp_refresh=", StringComparison.Ordinal));

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PatchAsJsonAsync(url, body, Json);
}

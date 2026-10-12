using System.Net.Http.Json;
using PrayerApp.Client.Services;
using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Client.Auth;

/// <summary>
/// Sign-in, sign-out and token renewal. Talks to /auth/* through the "auth" HttpClient,
/// which sends cookies (the refresh token) but never a bearer token.
/// </summary>
public sealed class AuthService(IHttpClientFactory httpClientFactory, TokenStore store, TimeProvider time)
{
    public const string HttpClientName = "auth";

    // Renew a little before expiry so a request never leaves with a token that dies in flight.
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private Task<bool>? _refreshInFlight;
    private Task? _sessionRestore;

    private HttpClient Http => httpClientFactory.CreateClient(HttpClientName);

    public Task<ApiResult> LoginAsync(LoginRequest request) => SignInAsync("/auth/login", request);

    public Task<ApiResult> RegisterAsync(RegisterRequest request) => SignInAsync("/auth/register", request);

    public async Task LogoutAsync()
    {
        try
        {
            using var _ = await Http.PostAsync("/auth/logout", content: null);
        }
        catch (HttpRequestException)
        {
            // Offline: still sign out locally. The server-side token expires on its own.
        }

        store.Clear();
    }

    /// <summary>
    /// On app start, tries to resume the session from the refresh cookie, so a reload
    /// doesn't sign the user out. Runs once; later callers await the same task.
    /// </summary>
    public Task EnsureSessionRestoredAsync() => _sessionRestore ??= RefreshAsync();

    /// <summary>The current access token, renewed first if it's about to expire.</summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        if (store.AccessToken is not null && store.ExpiresAt - time.GetUtcNow() < RefreshMargin)
        {
            await RefreshAsync();
        }

        return store.AccessToken;
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token. Single-flight: if a refresh
    /// is already running, every caller shares it instead of starting another.
    ///
    /// This isn't just an optimization. The server rotates refresh tokens and treats a
    /// second use of the same token as theft, revoking the session. Three components
    /// loading at once, each refreshing independently, would sign the user out.
    /// </summary>
    public Task<bool> RefreshAsync() => _refreshInFlight ??= RefreshOnceAsync();

    private async Task<bool> RefreshOnceAsync()
    {
        // Guarantees this method returns to the caller (who stores the task in
        // _refreshInFlight) before the finally block below can clear it.
        await Task.Yield();

        try
        {
            using var response = await Http.PostAsync("/auth/refresh", content: null);
            if (!response.IsSuccessStatusCode)
            {
                if (store.AccessToken is not null)
                {
                    store.Clear();
                }

                return false;
            }

            store.Set((await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options))!);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        finally
        {
            _refreshInFlight = null;
        }
    }

    private async Task<ApiResult> SignInAsync<TRequest>(string url, TRequest request)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(url, request, ApiJson.Options);
            if (!response.IsSuccessStatusCode)
            {
                return await ApiResult.FailureFromAsync(response);
            }

            store.Set((await response.Content.ReadFromJsonAsync<AuthResponse>(ApiJson.Options))!);
            return ApiResult.Success;
        }
        catch (HttpRequestException)
        {
            return ApiResult.NetworkFailure();
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace PrayerApp.Client.Auth;

/// <summary>
/// For /auth/* calls: tells the browser to send and accept cookies on this cross-origin
/// request, the equivalent of fetch(url, { credentials: "include" }). Without it the
/// browser silently drops the refresh cookie.
/// </summary>
public sealed class IncludeCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// For every other API call: attaches "Authorization: Bearer ..." and, if the API still
/// answers 401 (say the token was rejected after a server restart), refreshes once and
/// retries once. Like an axios request/response interceptor pair.
/// </summary>
public sealed class BearerTokenHandler(AuthService auth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await auth.GetAccessTokenAsync();
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null || !await auth.RefreshAsync())
        {
            return response;
        }

        response.Dispose();

        // An HttpRequestMessage can only be sent once, so build a copy for the retry.
        var retry = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync());
        return await base.SendAsync(retry, cancellationToken);
    }
}

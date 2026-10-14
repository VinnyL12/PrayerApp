using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using PrayerApp.Api.Admin;
using PrayerApp.Api.Common;

namespace PrayerApp.Api.LiveUpdates;

/// <summary>
/// A push-only channel to signed-in browsers (like a Socket.IO namespace with no client
/// events). Clients never call methods here; the server sends hints through
/// <see cref="LiveUpdatePublisher"/>.
///
/// Authentication is the app's fallback policy, the same as every endpoint: no valid JWT,
/// no connection. SignalR then groups each user's connections (several tabs) under their
/// "sub" claim, so Clients.User(id) reaches all of them.
/// </summary>
public sealed class LiveUpdatesHub(AdminAccess adminAccess) : Hub
{
    public const string AdminsGroup = "admins";

    public override async Task OnConnectedAsync()
    {
        // Checked once per connection, against the database like the Admin policy. A user
        // promoted or demoted later joins or leaves on their next connection, which comes
        // at most one token lifetime later (see CloseOnAuthenticationExpiration in Program).
        // Until then a demoted admin only receives payload-free hints; refetching the queue
        // still answers 403.
        if (await adminAccess.IsAdminAsync(Context.User!.GetUserId(), Context.ConnectionAborted))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>
/// SignalR's default reads ClaimTypes.NameIdentifier. This API keeps JWT claim names as-is
/// (MapInboundClaims = false), so the user id is in "sub".
/// </summary>
public sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User.Identity?.IsAuthenticated == true ? connection.User.GetUserId().ToString() : null;
}

using Microsoft.AspNetCore.SignalR.Client;
using PrayerApp.Client.Auth;
using PrayerApp.Shared.Contracts.Notifications;

namespace PrayerApp.Client.Services;

/// <summary>
/// The tab's one SignalR connection to the API's live-update hub (think: a single
/// socket.io client shared app-wide). Components subscribe to its events and refetch;
/// hints carry no data. One connection per tab, however many components listen.
///
/// Lifetime follows the signed-in user: components call <see cref="EnsureStartedAsync"/>,
/// and signing out, or signing in as someone else, closes a connection that belongs to the
/// previous user so it never receives their hints.
/// </summary>
public sealed class LiveConnection
{
    private readonly HubConnection _hub;
    private readonly TokenStore _tokens;
    private readonly ILogger<LiveConnection> _logger;

    // Start, stop and user switches all go through this gate, one at a time. WebAssembly is
    // single-threaded, but awaits still interleave, and StartAsync on a connection that is
    // already starting throws.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Whose token opened the current connection.
    private Guid? _connectedUser;

    public LiveConnection(Uri hubUrl, AuthService auth, TokenStore tokens, ILogger<LiveConnection> logger)
    {
        _tokens = tokens;
        _logger = logger;

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // Called on every connect and reconnect, so a reconnect after the server
                // closes an expired connection picks up a renewed token. In the browser the
                // client sends it as ?access_token= (WebSockets can't carry headers).
                options.AccessTokenProvider = auth.GetAccessTokenAsync;
            })
            // Retries after 0, 2, 10 and 30 seconds, then gives up; the components' fallback
            // poll calls EnsureStartedAsync again after that.
            .WithAutomaticReconnect()
            .Build();

        _hub.On(LiveUpdateEvents.NotificationsChanged, () => NotificationsChanged?.Invoke());
        _hub.On(LiveUpdateEvents.ModerationChanged, () => ModerationChanged?.Invoke());

        // Hints sent while the connection was down are gone, so listeners refetch once.
        _hub.Reconnected += _ =>
        {
            Reconnected?.Invoke();
            return Task.CompletedTask;
        };

        tokens.Changed += () => _ = SyncAsync(startIfNeeded: false);
    }

    public event Action? NotificationsChanged;

    public event Action? ModerationChanged;

    public event Action? Reconnected;

    /// <summary>True while hints can arrive. When false, listeners should poll.</summary>
    public bool IsConnected => _hub.State == HubConnectionState.Connected;

    /// <summary>Connects for the signed-in user, if not already connected. Safe to call often.</summary>
    public Task EnsureStartedAsync() => SyncAsync(startIfNeeded: true);

    private async Task SyncAsync(bool startIfNeeded)
    {
        await _gate.WaitAsync();
        try
        {
            var user = _tokens.User?.Id;
            var open = _hub.State != HubConnectionState.Disconnected;

            // Already up (or reconnecting) for this user. A renewed token for the same user
            // also lands here: the open connection carries on.
            if (open && _connectedUser == user)
            {
                return;
            }

            // Signed out, or a different account signed in on this tab.
            if (open)
            {
                await _hub.StopAsync();
            }

            if (user is null || !startIfNeeded)
            {
                return;
            }

            _connectedUser = user;
            await _hub.StartAsync();
        }
        catch (Exception ex)
        {
            // API down or restarting. Not fatal: callers keep polling while disconnected.
            _logger.LogWarning(ex, "Live updates unavailable; falling back to polling.");
        }
        finally
        {
            _gate.Release();
        }
    }
}

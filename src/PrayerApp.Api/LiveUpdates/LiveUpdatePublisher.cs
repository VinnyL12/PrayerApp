using Microsoft.AspNetCore.SignalR;
using PrayerApp.Shared.Contracts.Notifications;

namespace PrayerApp.Api.LiveUpdates;

/// <summary>
/// Sends live hints to connected browsers. Call it only after the change has committed, the
/// same rule as enqueueing notifications: a hint sent earlier could make a client refetch
/// before the row exists, or for a change that then rolled back.
///
/// Best-effort and never throws: the data is already saved, and a client that misses a hint
/// catches up when it reconnects (or from its fallback poll).
/// </summary>
public sealed class LiveUpdatePublisher(IHubContext<LiveUpdatesHub> hub, ILogger<LiveUpdatePublisher> logger)
{
    public async Task NotificationsChangedAsync(IEnumerable<Guid> recipients, CancellationToken ct = default)
    {
        // Distinct: a batch with 12 notifications for one author sends that author one hint.
        var userIds = recipients.Distinct().Select(id => id.ToString()).ToList();
        if (userIds.Count == 0)
        {
            return;
        }

        try
        {
            await hub.Clients.Users(userIds).SendAsync(LiveUpdateEvents.NotificationsChanged, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't send a live notification hint to {Count} users.", userIds.Count);
        }
    }

    public Task NotificationsChangedAsync(Guid recipient, CancellationToken ct = default) =>
        NotificationsChangedAsync([recipient], ct);

    public async Task ModerationChangedAsync(CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.Group(LiveUpdatesHub.AdminsGroup).SendAsync(LiveUpdateEvents.ModerationChanged, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't send a live moderation hint.");
        }
    }
}

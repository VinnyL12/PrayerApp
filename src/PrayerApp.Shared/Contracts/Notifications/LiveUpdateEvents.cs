namespace PrayerApp.Shared.Contracts.Notifications;

/// <summary>
/// The SignalR hub's address and the messages it sends, shared so the API and the client
/// can't drift apart on a string.
///
/// Every message is a hint with no payload: "something changed, refetch". The client then
/// asks the same endpoints it always uses, which keeps every privacy rule in one place
/// and makes a lost or repeated hint harmless.
/// </summary>
public static class LiveUpdateEvents
{
    public const string HubPath = "/hubs/live";

    /// <summary>Sent to a user when their notifications change (a new one, or one removed).</summary>
    public const string NotificationsChanged = nameof(NotificationsChanged);

    /// <summary>Sent to every connected admin when the moderation queue may have changed.</summary>
    public const string ModerationChanged = nameof(ModerationChanged);
}

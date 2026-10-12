namespace PrayerApp.Client.Services;

/// <summary>
/// Shared unread-notification count. The bell in the header polls it; the notifications
/// page refreshes it after marking things read. Any component can subscribe to Changed,
/// like a tiny store (Zustand-style) without a library.
/// </summary>
public sealed class NotificationState(PrayerApiClient api)
{
    public int UnreadCount { get; private set; }

    public event Action? Changed;

    public async Task RefreshAsync()
    {
        var result = await api.GetNotificationsAsync(unreadOnly: true, limit: 1);
        if (result.Succeeded && result.Value!.UnreadCount != UnreadCount)
        {
            UnreadCount = result.Value.UnreadCount;
            Changed?.Invoke();
        }
    }
}

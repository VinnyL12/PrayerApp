using PrayerApp.Shared.Contracts.Moderation;

namespace PrayerApp.Client.Services;

/// <summary>
/// How many reported posts await an admin's decision. The avatar badge polls it; the
/// moderation page refreshes it after each decision. Same shape as NotificationState.
/// </summary>
public sealed class ModerationState(PrayerApiClient api)
{
    public ModerationSummary Summary { get; private set; } = new(0);

    public event Action? Changed;

    public async Task RefreshAsync()
    {
        var result = await api.GetModerationSummaryAsync();
        if (result.Succeeded && result.Value != Summary)
        {
            // Records compare by value, so this only notifies when something changed.
            Summary = result.Value!;
            Changed?.Invoke();
        }
    }
}

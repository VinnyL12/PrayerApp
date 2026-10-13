namespace PrayerApp.Shared.Enums;

public enum NotificationType
{
    PostAcknowledged = 1,

    /// <summary>An admin hid the recipient's post.</summary>
    PostHidden = 2,

    /// <summary>The author of a post the recipient responded to added an update.</summary>
    PostUpdated = 3,
}

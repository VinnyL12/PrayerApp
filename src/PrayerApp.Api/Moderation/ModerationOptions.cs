namespace PrayerApp.Api.Moderation;

/// <summary>Bound from the "Moderation" configuration section.</summary>
public sealed class ModerationOptions
{
    public const int DefaultAutoHideThreshold = 3;

    /// <summary>
    /// Open reports that hide a post until an admin reviews it. Low enough that a scam
    /// comes down quickly, high enough that one person can't hide a post they dislike.
    /// </summary>
    public int AutoHideThreshold { get; set; } = DefaultAutoHideThreshold;
}

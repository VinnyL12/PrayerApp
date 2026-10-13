namespace PrayerApp.Shared.Enums;

/// <summary>
/// Whether moderation has taken a post out of view. Separate from PostStatus, which is
/// the author's lifecycle: an answered post can be hidden, and restoring it brings it back
/// as answered.
/// </summary>
public enum ModerationStatus
{
    Visible = 1,

    /// <summary>Hidden automatically after enough reports; waiting for an admin.</summary>
    UnderReview = 2,

    /// <summary>An admin decided to hide it.</summary>
    Hidden = 3,
}

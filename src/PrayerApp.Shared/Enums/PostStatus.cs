namespace PrayerApp.Shared.Enums;

/// <summary>
/// Lifecycle of a post. Archived is the soft-delete state: the row and its
/// acknowledgments stay in the database but the post leaves feeds.
/// </summary>
public enum PostStatus
{
    Active = 1,
    Answered = 2,
    Archived = 3,
}

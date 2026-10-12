using PrayerApp.Shared.Enums;

namespace PrayerApp.Domain.Rules;

/// <summary>
/// The post lifecycle as a small state machine:
///
///   Active ──► Answered ──► Archived
///      └──────────────────────▲
///
/// Archived is terminal (it's the soft delete). Answered can't go back to Active.
/// </summary>
public static class PostStatusTransitions
{
    // Adjacency list: each state maps to the states reachable from it in one step.
    private static readonly Dictionary<PostStatus, PostStatus[]> Allowed = new()
    {
        [PostStatus.Active] = [PostStatus.Answered, PostStatus.Archived],
        [PostStatus.Answered] = [PostStatus.Archived],
        [PostStatus.Archived] = [],
    };

    public static bool CanTransition(PostStatus from, PostStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
}

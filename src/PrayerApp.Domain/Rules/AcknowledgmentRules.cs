using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Domain.Rules;

public enum AcknowledgmentDenial
{
    None,
    OwnPost,
    PostClosed,
}

public static class AcknowledgmentRules
{
    /// <summary>
    /// Business rules for acknowledging a post, independent of the database.
    /// Membership and the one-per-user limit are checked by the caller and the
    /// unique index respectively.
    /// </summary>
    public static AcknowledgmentDenial Check(Post post, Guid userId)
    {
        if (post.AuthorId == userId)
        {
            return AcknowledgmentDenial.OwnPost;
        }

        // Answered posts can still receive support; archived ones are closed.
        if (post.Status == PostStatus.Archived)
        {
            return AcknowledgmentDenial.PostClosed;
        }

        return AcknowledgmentDenial.None;
    }
}

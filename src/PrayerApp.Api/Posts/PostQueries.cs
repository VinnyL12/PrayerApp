using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Posts;

/// <summary>Reusable query pieces. These compose into SQL; nothing runs until ToListAsync etc.</summary>
public static class PostQueries
{
    /// <summary>
    /// Who can see a post: its author always; otherwise current members of its
    /// community, unless it's archived or moderation has taken it out of view.
    /// (Admins review hidden posts through the moderation queue, not here.)
    /// </summary>
    public static IQueryable<Post> VisibleTo(this IQueryable<Post> posts, Guid me) =>
        posts.Where(p => p.AuthorId == me
            || (p.Status != PostStatus.Archived
                && p.ModerationStatus == ModerationStatus.Visible
                && p.Community.Memberships.Any(m => m.UserId == me)));

    /// <summary>
    /// Projects to the API shape. Anonymity is applied here, inside the SQL query, so
    /// an anonymous author's name never leaves the database for other viewers.
    /// </summary>
    public static IQueryable<PostDto> ToDto(this IQueryable<Post> posts, Guid me) =>
        posts.Select(p => new PostDto(
            p.Id,
            p.CommunityId,
            p.Community.Category,
            p.Community.Name,
            p.Body,
            p.Status,
            p.IsUrgent,
            p.IsAnonymous,
            p.CreatedAt,
            p.IsAnonymous && p.AuthorId != me ? null : p.Author.DisplayName,
            p.AuthorId == me,
            p.AcknowledgmentCount,
            p.Acknowledgments
                .Where(a => a.UserId == me)
                .Select(a => (AcknowledgmentType?)a.Type)
                .FirstOrDefault(),
            p.Link,
            // Only the id: a LEFT JOIN on the unique PostId index, never the image bytes.
            p.Image != null ? p.Image.Id : null,
            p.ModerationStatus,
            p.Reports.Any(r => r.ReporterUserId == me),
            p.Updates.Count,
            // TOP 1 per post via the (PostId, CreatedAt DESC) index: a seek, not a sort.
            p.Updates
                .OrderByDescending(u => u.CreatedAt)
                .ThenByDescending(u => u.Id)
                .Select(u => new PostUpdateDto(u.Id, u.Body, u.CreatedAt))
                .FirstOrDefault()));
}

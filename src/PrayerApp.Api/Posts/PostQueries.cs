using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Posts;

/// <summary>Reusable query pieces. These compose into SQL; nothing runs until ToListAsync etc.</summary>
public static class PostQueries
{
    /// <summary>
    /// Who can see a post: its author always; otherwise current members of its
    /// community, unless it's archived.
    /// </summary>
    public static IQueryable<Post> VisibleTo(this IQueryable<Post> posts, Guid me) =>
        posts.Where(p => p.AuthorId == me
            || (p.Status != PostStatus.Archived && p.Community.Memberships.Any(m => m.UserId == me)));

    /// <summary>
    /// Projects to the API shape. Anonymity is applied here, inside the SQL query, so
    /// an anonymous author's name never leaves the database for other viewers.
    /// </summary>
    public static IQueryable<PostDto> ToDto(this IQueryable<Post> posts, Guid me) =>
        posts.Select(p => new PostDto(
            p.Id,
            p.CommunityId,
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
                .FirstOrDefault()));
}

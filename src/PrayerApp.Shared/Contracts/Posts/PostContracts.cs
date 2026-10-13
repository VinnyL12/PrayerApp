using System.ComponentModel.DataAnnotations;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Shared.Contracts.Posts;

public sealed record CreatePostRequest
{
    [Required(ErrorMessage = "Write something before posting."), MaxLength(2000)]
    public required string Body { get; set; }

    public bool IsUrgent { get; set; }

    public bool IsAnonymous { get; set; }

    /// <summary>Optional https link (fundraiser, payment page). See PostLinks.</summary>
    [MaxLength(PostLinks.MaxLength)]
    public string? Link { get; set; }
}

public sealed record UpdatePostStatusRequest
{
    [Required]
    public required PostStatus Status { get; set; }
}

/// <param name="AuthorDisplayName">Null when the post is anonymous and the viewer isn't the author.</param>
/// <param name="MyAcknowledgment">The viewer's own response to this post, if any.</param>
/// <param name="CommunityCategory">Decides which responses fit (see AcknowledgmentPolicy).</param>
/// <param name="ImageId">The post's image, served at /images/{ImageId}; null when there's none.</param>
/// <param name="ModerationStatus">Anything but Visible is only ever shown to the author (and admins, in the queue).</param>
/// <param name="ReportedByMe">The viewer has reported this post.</param>
public sealed record PostDto(
    int Id,
    int CommunityId,
    CommunityCategory CommunityCategory,
    string CommunityName,
    string Body,
    PostStatus Status,
    bool IsUrgent,
    bool IsAnonymous,
    DateTimeOffset CreatedAt,
    string? AuthorDisplayName,
    bool IsMine,
    int AcknowledgmentCount,
    AcknowledgmentType? MyAcknowledgment,
    string? Link,
    Guid? ImageId,
    ModerationStatus ModerationStatus,
    bool ReportedByMe);

/// <param name="NextCursor">Pass as ?cursor= to get the next page; null on the last page.</param>
public sealed record FeedPage(IReadOnlyList<PostDto> Posts, string? NextCursor);

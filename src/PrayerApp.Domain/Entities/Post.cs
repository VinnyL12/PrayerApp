using PrayerApp.Shared.Enums;

namespace PrayerApp.Domain.Entities;

public class Post
{
    public int Id { get; set; }

    // Always stored, even for anonymous posts: the author still needs "my posts"
    // and notifications. IsAnonymous only controls what other members are shown.
    public Guid AuthorId { get; set; }

    public int CommunityId { get; set; }

    public required string Body { get; set; }

    public PostStatus Status { get; set; } = PostStatus.Active;

    // Moderation is a separate axis from Status, which belongs to the author.
    public ModerationStatus ModerationStatus { get; set; } = ModerationStatus.Visible;

    public bool IsUrgent { get; set; }

    public bool IsAnonymous { get; set; }

    /// <summary>Optional https link, for example a fundraiser or payment page.</summary>
    public string? Link { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Denormalized counter. Incremented in the same transaction that inserts the
    // Acknowledgment row, so it can't drift if the notification pipeline loses an event.
    public int AcknowledgmentCount { get; set; }

    public User Author { get; set; } = null!;

    public Community Community { get; set; } = null!;

    public PostImage? Image { get; set; }

    public ICollection<Acknowledgment> Acknowledgments { get; } = [];

    public ICollection<Report> Reports { get; } = [];

    public ICollection<PostUpdate> Updates { get; } = [];
}

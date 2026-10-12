using PrayerApp.Shared.Enums;

namespace PrayerApp.Domain.Entities;

/// <summary>
/// One user's preset response to one post. (PostId, UserId) is unique:
/// a user acknowledges a post at most once.
/// </summary>
public class Acknowledgment
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public Guid UserId { get; set; }

    public AcknowledgmentType Type { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Post Post { get; set; } = null!;

    public User User { get; set; } = null!;
}

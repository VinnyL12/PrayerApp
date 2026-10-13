namespace PrayerApp.Domain.Entities;

/// <summary>
/// A follow-up the author adds to their post ("Surgery went well"). Everyone who responded
/// to the post is notified, so the people who prayed find out how it turned out.
/// </summary>
public class PostUpdate
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Post Post { get; set; } = null!;
}

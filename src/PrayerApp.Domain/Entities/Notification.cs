using PrayerApp.Shared.Enums;

namespace PrayerApp.Domain.Entities;

public class Notification
{
    public int Id { get; set; }

    public Guid RecipientUserId { get; set; }

    public int PostId { get; set; }

    // Internal only: acknowledger identities are never shown to authors. Kept so the
    // pipeline can deduplicate and so the row is traceable when debugging.
    public Guid ActorUserId { get; set; }

    public NotificationType Type { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User Recipient { get; set; } = null!;

    public Post Post { get; set; } = null!;

    public User Actor { get; set; } = null!;
}

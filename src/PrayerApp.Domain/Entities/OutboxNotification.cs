using PrayerApp.Shared.Enums;

namespace PrayerApp.Domain.Entities;

/// <summary>
/// A notification waiting to be written: the transactional outbox. The request that causes
/// it (an acknowledgment) inserts this row in its own transaction, so the event is saved
/// exactly when the change is, and survives a restart. NotificationProcessor turns rows
/// into Notifications and deletes them, so the table is normally empty or close to it.
/// </summary>
public class OutboxNotification
{
    public long Id { get; set; }

    public NotificationType Type { get; set; }

    public Guid RecipientUserId { get; set; }

    public Guid ActorUserId { get; set; }

    public int PostId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Set when this row kept failing on its own and was set aside (a dead letter), so it
    /// stops blocking the rows behind it. Null means still pending.
    /// </summary>
    public DateTimeOffset? FailedAt { get; set; }

    public string? LastError { get; set; }
}

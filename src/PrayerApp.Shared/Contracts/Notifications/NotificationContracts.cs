using PrayerApp.Shared.Enums;

namespace PrayerApp.Shared.Contracts.Notifications;

/// <summary>
/// Deliberately has no actor field: authors learn that someone responded, never who.
/// </summary>
public sealed record NotificationDto(
    int Id,
    NotificationType Type,
    int PostId,
    string PostExcerpt,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record NotificationList(IReadOnlyList<NotificationDto> Items, int UnreadCount);

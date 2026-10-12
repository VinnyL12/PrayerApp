using System.Threading.Channels;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Notifications;

public sealed record NotificationEvent(
    NotificationType Type,
    Guid RecipientUserId,
    Guid ActorUserId,
    int PostId,
    DateTimeOffset OccurredAt);

/// <summary>
/// In-process producer/consumer queue between request handlers (producers) and
/// <see cref="NotificationProcessor"/> (the single consumer).
///
/// Bounded, so a burst can't grow memory without limit. When full, new events are
/// dropped (load shedding) rather than making the HTTP request wait (back-pressure):
/// the acknowledgment is already committed, and a notification is best-effort.
///
/// Known limitation: events still in the queue are lost if the process stops. A real
/// broker (Azure Service Bus, RabbitMQ) or an outbox table would make them durable.
/// </summary>
public sealed class NotificationQueue(ILogger<NotificationQueue> logger)
{
    public const int Capacity = 1_000;

    private readonly Channel<NotificationEvent> _channel = Channel.CreateBounded<NotificationEvent>(
        new BoundedChannelOptions(Capacity)
        {
            // With Wait, TryWrite returns false when full instead of silently discarding,
            // which lets us log the drop. (WriteAsync would wait; we never call it.)
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelReader<NotificationEvent> Reader => _channel.Reader;

    public bool TryEnqueue(NotificationEvent notification)
    {
        if (_channel.Writer.TryWrite(notification))
        {
            return true;
        }

        logger.LogWarning(
            "Notification queue full ({Capacity}); dropping {Type} notification for post {PostId}.",
            Capacity, notification.Type, notification.PostId);
        return false;
    }
}

using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Notifications;

/// <summary>
/// The consumer side of <see cref="NotificationQueue"/>: a long-running background loop
/// that turns events into Notification rows, in batches, with retries.
/// </summary>
public sealed class NotificationProcessor(
    NotificationQueue queue,
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<NotificationProcessor> logger) : BackgroundService
{
    private const int MaxBatchSize = 100;
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<NotificationEvent>(MaxBatchSize);

        try
        {
            // Sleeps (no CPU, no polling) until at least one event is available.
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                // Take everything already waiting, up to a cap, so a burst of 50
                // acknowledgments becomes one database round trip instead of 50.
                while (batch.Count < MaxBatchSize && queue.Reader.TryRead(out var notification))
                {
                    batch.Add(notification);
                }

                await WriteWithRetryAsync(batch, stoppingToken);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        if (queue.Reader.Count > 0)
        {
            logger.LogWarning("Stopping with {Count} undelivered notifications.", queue.Reader.Count);
        }
    }

    private async Task WriteWithRetryAsync(List<NotificationEvent> batch, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // This service is a singleton living for the whole app; DbContext is scoped
                // (one per unit of work). So each batch gets its own scope and DbContext,
                // the same way each HTTP request does.
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                db.Notifications.AddRange(batch.Select(e => new Notification
                {
                    Type = e.Type,
                    RecipientUserId = e.RecipientUserId,
                    ActorUserId = e.ActorUserId,
                    PostId = e.PostId,
                    CreatedAt = e.OccurredAt,
                }));

                await db.SaveChangesAsync(ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxAttempts)
            {
                // Exponential backoff (200ms, 400ms) for transient failures like a
                // dropped connection or a deadlock victim.
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                logger.LogWarning(ex, "Writing {Count} notifications failed (attempt {Attempt}); retrying in {Delay}.",
                    batch.Count, attempt, delay);
                await Task.Delay(delay, time, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Give up rather than block the queue forever. A durable design would move
                // these to a dead-letter store for inspection instead of only logging.
                logger.LogError(ex, "Dropping {Count} notifications after {Attempts} attempts.", batch.Count, MaxAttempts);
                return;
            }
        }
    }
}

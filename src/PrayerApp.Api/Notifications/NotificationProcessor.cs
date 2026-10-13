using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Enums;

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

                var toWrite = await StillNeededAsync(db, batch, ct);
                if (toWrite.Count == 0)
                {
                    return;
                }

                db.Notifications.AddRange(toWrite.Select(e => new Notification
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

    /// <summary>
    /// Drops events that no longer need a row, which makes writing idempotent: the same
    /// event processed twice (a retry, or one enqueued again) still yields one notification.
    ///   - Withdrawn: the acknowledgment was removed before this batch ran.
    ///   - Already written, or repeated within this batch: an acknowledgment removed and
    ///     re-added quickly can enqueue two events for the same (post, supporter).
    /// </summary>
    private static async Task<List<NotificationEvent>> StillNeededAsync(
        AppDbContext db, List<NotificationEvent> batch, CancellationToken ct)
    {
        var postIds = batch.Select(e => e.PostId).Distinct().ToList();
        var actorIds = batch.Select(e => e.ActorUserId).Distinct().ToList();

        // Two set-based queries per batch rather than two per event. Filtering on both
        // IN-lists can over-fetch (a post from one event, an actor from another); the
        // hash sets then answer each exact (post, actor) question in O(1).
        var acknowledged = (await db.Acknowledgments
                .Where(a => postIds.Contains(a.PostId) && actorIds.Contains(a.UserId))
                .Select(a => new { a.PostId, a.UserId })
                .ToListAsync(ct))
            .Select(a => (a.PostId, a.UserId))
            .ToHashSet();

        var written = (await db.Notifications
                .Where(n => postIds.Contains(n.PostId) && actorIds.Contains(n.ActorUserId))
                .Select(n => new { n.Type, n.PostId, n.ActorUserId })
                .ToListAsync(ct))
            .Select(n => (n.Type, n.PostId, n.ActorUserId))
            .ToHashSet();

        var needed = new List<NotificationEvent>(batch.Count);
        foreach (var e in batch)
        {
            if (e.Type == NotificationType.PostAcknowledged && !acknowledged.Contains((e.PostId, e.ActorUserId)))
            {
                continue;
            }

            // HashSet.Add returns false if the key was already there: one call both checks
            // for an existing row and claims the key for this batch.
            if (written.Add((e.Type, e.PostId, e.ActorUserId)))
            {
                needed.Add(e);
            }
        }

        return needed;
    }
}

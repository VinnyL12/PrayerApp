using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Data;
using PrayerApp.Api.Data.Configurations;
using PrayerApp.Api.LiveUpdates;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Notifications;

/// <summary>
/// Drains the notification outbox: a long-running background loop that turns
/// <see cref="OutboxNotification"/> rows into Notification rows, in batches, with retries.
///
/// Delivery is at-least-once by construction: a row leaves the outbox in the same
/// transaction that writes its notification, so a crash at any point either leaves both
/// undone (the row is retried) or both done. The writer is idempotent on top of that.
/// </summary>
public sealed class NotificationProcessor(
    NotificationSignal signal,
    IServiceScopeFactory scopeFactory,
    LiveUpdatePublisher live,
    TimeProvider time,
    ILogger<NotificationProcessor> logger) : BackgroundService
{
    public const int MaxBatchSize = 100;
    private const int MaxAttempts = 3;

    // The signal covers rows written by this process. The timer covers the rest: rows from
    // another API instance, or a signal lost to a crash between commit and ring.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Draining first thing on startup is what makes a restart lossless: whatever was
        // committed but not yet processed when the last process stopped is still here.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A full batch means there may be more; keep going until a partial one.
                while (await ProcessWithRetryAsync(stoppingToken) == MaxBatchSize)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Database unreachable, most likely. Rows stay in the outbox; try again later.
                // Catching everything keeps one bad drain from stopping the loop for good.
                logger.LogError(ex, "Draining the notification outbox failed; retrying in up to {Interval}.", PollInterval);
            }

            try
            {
                await signal.WaitAsync(PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Processes one batch, retrying transient failures. Returns how many rows it took.</summary>
    private async Task<int> ProcessWithRetryAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ProcessBatchAsync(onlyId: null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxAttempts)
            {
                // Exponential backoff (200ms, 400ms) for transient failures like a
                // dropped connection or a deadlock victim.
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                logger.LogWarning(ex, "Writing notifications failed (attempt {Attempt}); retrying in {Delay}.", attempt, delay);
                await Task.Delay(delay, time, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Still failing. Maybe the database is down, maybe one bad row is failing the
                // whole batch; processing rows one at a time tells the two apart.
                logger.LogWarning(ex, "A notification batch failed {Attempts} times; retrying its rows one at a time.", MaxAttempts);
                return await IsolateFailuresAsync(ct);
            }
        }
    }

    /// <summary>
    /// Retries each pending row in its own transaction and sets aside (dead-letters) any row
    /// that fails alone, so one poison row can't hold up every notification behind it.
    /// If the database itself is down, setting the row aside fails too and the exception
    /// propagates: nothing is set aside, and the outer loop backs off.
    /// </summary>
    private async Task<int> IsolateFailuresAsync(CancellationToken ct)
    {
        List<long> ids;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            ids = await scope.ServiceProvider.GetRequiredService<AppDbContext>().NotificationOutbox
                .Where(e => e.FailedAt == null)
                .OrderBy(e => e.Id)
                .Select(e => e.Id)
                .Take(MaxBatchSize)
                .ToListAsync(ct);
        }

        foreach (var id in ids)
        {
            try
            {
                await ProcessBatchAsync(onlyId: id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await SetAsideAsync(id, ex, ct);
            }
        }

        return ids.Count;
    }

    private async Task SetAsideAsync(long id, Exception ex, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var error = ex.GetBaseException().Message;
        if (error.Length > OutboxNotificationConfiguration.LastErrorMaxLength)
        {
            error = error[..OutboxNotificationConfiguration.LastErrorMaxLength];
        }

        await db.NotificationOutbox
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.FailedAt, time.GetUtcNow())
                .SetProperty(e => e.LastError, error), ct);

        logger.LogError(ex, "Set aside outbox notification {Id} after it failed on its own.", id);
    }

    /// <summary>
    /// One unit of work: claim pending rows, write their notifications, delete them, commit.
    /// Returns how many rows it claimed.
    /// </summary>
    private async Task<int> ProcessBatchAsync(long? onlyId, CancellationToken ct)
    {
        // This service is a singleton living for the whole app; DbContext is scoped (one per
        // unit of work). So each batch gets its own scope and DbContext, the same way each
        // HTTP request does.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Claim rows the classic SQL Server queue way. UPDLOCK keeps them locked until commit;
        // READPAST skips rows someone else has locked instead of waiting. Two processors (two
        // API instances) therefore take different rows rather than delivering the same one
        // twice, and a request deleting a row it just wrote (see Acknowledgment Remove) makes
        // the processor skip it.
        var claimed = await (onlyId is null
                ? db.NotificationOutbox.FromSql($"""
                    SELECT TOP ({MaxBatchSize}) * FROM NotificationOutbox WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE FailedAt IS NULL
                    ORDER BY Id
                    """)
                : db.NotificationOutbox.FromSql($"""
                    SELECT * FROM NotificationOutbox WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE Id = {onlyId.Value} AND FailedAt IS NULL
                    """))
            .AsNoTracking()
            .ToListAsync(ct);

        if (claimed.Count == 0)
        {
            return 0;
        }

        var needed = await StillNeededAsync(db, claimed, ct);
        db.Notifications.AddRange(needed.Select(e => new Notification
        {
            Type = e.Type,
            RecipientUserId = e.RecipientUserId,
            ActorUserId = e.ActorUserId,
            PostId = e.PostId,
            CreatedAt = e.OccurredAt,
        }));
        await db.SaveChangesAsync(ct);

        // Every claimed row leaves the outbox, including the ones StillNeeded skipped:
        // those were handled too, by deciding no notification was needed.
        var ids = claimed.Select(e => e.Id).ToList();
        await db.NotificationOutbox.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);

        // Committed, so recipients can be told to refetch. One hint per recipient per
        // batch: batching doubles as coalescing for the live channel too.
        await live.NotificationsChangedAsync(needed.Select(e => e.RecipientUserId), ct);
        return claimed.Count;
    }

    /// <summary>
    /// Drops rows that no longer need a notification, which keeps writing idempotent: the same
    /// event processed twice still yields one notification.
    ///   - Withdrawn: the acknowledgment was removed before this batch ran.
    ///   - Already written, or repeated within this batch: an acknowledgment removed and
    ///     re-added quickly leaves two rows for the same (post, supporter).
    /// </summary>
    private static async Task<List<OutboxNotification>> StillNeededAsync(
        AppDbContext db, List<OutboxNotification> batch, CancellationToken ct)
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

        var needed = new List<OutboxNotification>(batch.Count);
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

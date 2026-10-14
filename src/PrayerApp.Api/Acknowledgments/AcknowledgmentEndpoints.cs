using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Api.LiveUpdates;
using PrayerApp.Api.Notifications;
using PrayerApp.Api.Posts;
using PrayerApp.Domain.Entities;
using PrayerApp.Domain.Rules;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Acknowledgments;

public static class AcknowledgmentEndpoints
{
    public static IEndpointRouteBuilder MapAcknowledgmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/posts/{postId:int}/acknowledgments").WithTags("Acknowledgments");

        group.MapPost("/", Acknowledge);
        group.MapPut("/mine", Change);
        group.MapDelete("/mine", Remove);
        group.MapGet("/summary", Summary);

        return app;
    }

    private static async Task<Results<Ok<AcknowledgmentResult>, NotFound, ProblemHttpResult, ValidationProblem>> Acknowledge(
        int postId,
        AcknowledgeRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        TimeProvider time,
        NotificationQueue notifications,
        CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!Enum.IsDefined(request.Type))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Type)] = ["Unknown acknowledgment type."],
            });
        }

        var post = await db.Posts.AsNoTracking().Include(p => p.Community)
            .Where(p => p.Id == postId).VisibleTo(me).SingleOrDefaultAsync(ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        switch (AcknowledgmentRules.Check(post, me))
        {
            case AcknowledgmentDenial.OwnPost:
                return TypedResults.Problem("You can't acknowledge your own post.", statusCode: StatusCodes.Status403Forbidden);
            case AcknowledgmentDenial.PostClosed:
                return TypedResults.Problem("This post is archived.", statusCode: StatusCodes.Status409Conflict);
        }

        // Same rule the client uses to choose which buttons to show; enforced here because
        // a client can always send any value it likes.
        if (!AcknowledgmentPolicy.IsAllowed(request.Type, post.Community.Category, post.Status))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Type)] = [$"\"{request.Type.ToLabel()}\" isn't an available response for this post."],
            });
        }

        // Friendly fast path for the common case (user taps twice, seconds apart). It can't
        // be the only guard: two simultaneous requests would both pass it. See catch below.
        if (await db.Acknowledgments.AnyAsync(a => a.PostId == postId && a.UserId == me, ct))
        {
            return AlreadyAcknowledged();
        }

        // The row insert and the counter increment commit together or not at all, so
        // Post.AcknowledgmentCount always equals the number of Acknowledgment rows.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = time.GetUtcNow();
        db.Acknowledgments.Add(new Acknowledgment
        {
            PostId = postId,
            UserId = me,
            Type = request.Type,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // The unique (PostId, UserId) index is the real guard: it holds even when two
            // requests from the same user arrive at once and both pass the check above.
            return AlreadyAcknowledged();
        }

        // Increment inside SQL (count = count + 1), not read-modify-write in C#. Reading
        // the count, adding one, and saving would lose updates when two users acknowledge
        // concurrently: both read 4, both write 5.
        await db.Posts
            .Where(p => p.Id == postId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AcknowledgmentCount, p => p.AcknowledgmentCount + 1), ct);

        await transaction.CommitAsync(ct);

        // Only after commit: enqueueing earlier could notify the author about an
        // acknowledgment that then rolled back. Non-blocking; the author's notification
        // row is written moments later by NotificationProcessor.
        notifications.TryEnqueue(new NotificationEvent(
            NotificationType.PostAcknowledged, post.AuthorId, me, postId, now));

        var count = await db.Posts.Where(p => p.Id == postId).Select(p => p.AcknowledgmentCount).SingleAsync(ct);
        return TypedResults.Ok(new AcknowledgmentResult(postId, request.Type, count));
    }

    /// <summary>Swap your response for a different one. The response count doesn't change.</summary>
    private static async Task<Results<Ok<AcknowledgmentResult>, NotFound, ProblemHttpResult, ValidationProblem>> Change(
        int postId,
        AcknowledgeRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!Enum.IsDefined(request.Type))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Type)] = ["Unknown acknowledgment type."],
            });
        }

        // VisibleTo also covers archived posts: only their author can see them, and authors
        // can't respond to their own posts, so a response on an archived post is frozen.
        var post = await db.Posts.AsNoTracking().Include(p => p.Community)
            .Where(p => p.Id == postId).VisibleTo(me).SingleOrDefaultAsync(ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        // Checked against the post's current status: once a post is answered, a supporter
        // can switch "Praying for you" to "Celebrating with you", but not the reverse.
        if (!AcknowledgmentPolicy.IsAllowed(request.Type, post.Community.Category, post.Status))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Type)] = [$"\"{request.Type.ToLabel()}\" isn't an available response for this post."],
            });
        }

        // One UPDATE that only matches an existing response, so there's no window between
        // "do they have one?" and "change it". CreatedAt stays: new wording isn't new
        // support, so it doesn't count again toward the post's 24-hour velocity.
        var changed = await db.Acknowledgments
            .Where(a => a.PostId == postId && a.UserId == me)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Type, request.Type), ct);
        if (changed == 0)
        {
            return NotAcknowledged();
        }

        var count = await db.Posts.Where(p => p.Id == postId).Select(p => p.AcknowledgmentCount).SingleAsync(ct);
        return TypedResults.Ok(new AcknowledgmentResult(postId, request.Type, count));
    }

    /// <summary>Withdraw your response. It leaves no trace: the author's notification goes too.</summary>
    private static async Task<Results<Ok<AcknowledgmentRemovedResult>, NotFound, ProblemHttpResult>> Remove(
        int postId,
        AppDbContext db,
        ClaimsPrincipal principal,
        LiveUpdatePublisher live,
        CancellationToken ct)
    {
        var me = principal.GetUserId();

        // As in Change, archived posts are invisible here and so frozen.
        var post = await db.Posts.Where(p => p.Id == postId).VisibleTo(me)
            .Select(p => new { p.AuthorId })
            .SingleOrDefaultAsync(ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        // The mirror image of Acknowledge: row and counter change together or not at all.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The DELETE's row count decides whether to decrement, not an earlier read. Two
        // simultaneous removes both run it; the second blocks on the first's row lock,
        // then finds nothing to delete, so the counter goes down exactly once.
        var removed = await db.Acknowledgments
            .Where(a => a.PostId == postId && a.UserId == me)
            .ExecuteDeleteAsync(ct);
        if (removed == 0)
        {
            return NotAcknowledged();
        }

        await db.Posts
            .Where(p => p.Id == postId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AcknowledgmentCount, p => p.AcknowledgmentCount - 1), ct);

        // If the notification hasn't been written yet, NotificationProcessor sees the
        // acknowledgment is gone and skips it.
        var notificationsRemoved = await db.Notifications
            .Where(n => n.PostId == postId && n.ActorUserId == me && n.Type == NotificationType.PostAcknowledged)
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);

        if (notificationsRemoved > 0)
        {
            await live.NotificationsChangedAsync(post.AuthorId, ct);
        }

        var count = await db.Posts.Where(p => p.Id == postId).Select(p => p.AcknowledgmentCount).SingleAsync(ct);
        return TypedResults.Ok(new AcknowledgmentRemovedResult(postId, count));
    }

    private static ProblemHttpResult AlreadyAcknowledged() =>
        TypedResults.Problem("You've already acknowledged this post.", statusCode: StatusCodes.Status409Conflict);

    private static ProblemHttpResult NotAcknowledged() =>
        TypedResults.Problem("You haven't responded to this post.", statusCode: StatusCodes.Status404NotFound);

    /// <summary>Author-only breakdown by response type. Counts only: never who responded.</summary>
    private static async Task<Results<Ok<AcknowledgmentSummary>, NotFound>> Summary(
        int postId, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!await db.Posts.AnyAsync(p => p.Id == postId && p.AuthorId == me, ct))
        {
            return TypedResults.NotFound();
        }

        var counts = await db.Acknowledgments
            .Where(a => a.PostId == postId)
            .GroupBy(a => a.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Type, x => x.Count, ct);

        // Include every type, with zeros, so clients always get the same shape.
        var byType = Enum.GetValues<AcknowledgmentType>()
            .ToDictionary(t => t, t => counts.GetValueOrDefault(t));

        return TypedResults.Ok(new AcknowledgmentSummary(postId, byType.Values.Sum(), byType));
    }
}

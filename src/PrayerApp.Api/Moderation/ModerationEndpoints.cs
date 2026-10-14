using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrayerApp.Api.Admin;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Api.LiveUpdates;
using PrayerApp.Api.Posts;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Moderation;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Moderation;

/// <summary>
/// Members report posts; admins review reported posts from every community and keep or
/// hide them. Which admin decided, like who reported, never reaches other users.
/// </summary>
public static class ModerationEndpoints
{
    private const int QueueLimit = 50;

    public static IEndpointRouteBuilder MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/posts/{postId:int}/reports", Report).WithTags("Moderation");

        // Admins only. Non-admins get 403: unlike a hidden post, the existence of the
        // moderation area isn't a secret worth hiding behind a 404.
        var group = app.MapGroup("/moderation").WithTags("Moderation").RequireAuthorization(Policies.Admin);
        group.MapGet("/summary", Summary);
        group.MapGet("/queue", Queue);
        group.MapGet("/posts/{postId:int}/image", Image);
        group.MapPost("/posts/{postId:int}/hide", Hide);
        group.MapPost("/posts/{postId:int}/restore", Restore);

        return app;
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult, ValidationProblem>> Report(
        int postId,
        ReportRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        TimeProvider time,
        IOptions<ModerationOptions> options,
        LiveUpdatePublisher live,
        CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!Enum.IsDefined(request.Reason))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Reason)] = ["Unknown report reason."],
            });
        }

        // Hidden and archived posts aren't visible, so they can't be reported: 404.
        var post = await db.Posts.AsNoTracking()
            .Where(p => p.Id == postId).VisibleTo(me)
            .Select(p => new { p.AuthorId })
            .SingleOrDefaultAsync(ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        if (post.AuthorId == me)
        {
            return TypedResults.Problem("You can't report your own post.", statusCode: StatusCodes.Status403Forbidden);
        }

        db.Reports.Add(new Report
        {
            PostId = postId,
            ReporterUserId = me,
            Reason = request.Reason,
            Details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim(),
            CreatedAt = time.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Same pattern as acknowledgments: the unique index is the real guard.
            return TypedResults.Problem("You've already reported this post.", statusCode: StatusCodes.Status409Conflict);
        }

        // Auto-hide, as one conditional UPDATE run after the report has committed. Doing it
        // inside the report's transaction would be wrong two ways: with row locking, two
        // reports arriving together each wait on the other's uncommitted row (deadlock);
        // with snapshot reads, each counts only itself and neither crosses the threshold.
        // After commit, the last report to land always counts every earlier one, and a
        // repeat run is harmless ("set UnderReview where still Visible").
        var threshold = options.Value.AutoHideThreshold;
        await db.Posts
            .Where(p => p.Id == postId
                && p.ModerationStatus == ModerationStatus.Visible
                && p.Reports.Count(r => r.ResolvedAt == null) >= threshold)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ModerationStatus, ModerationStatus.UnderReview), ct);

        await live.ModerationChangedAsync(ct);

        // No body: a reporter doesn't learn whether their report tipped the post over the
        // threshold, which would tell a bad actor how many accounts it takes.
        return TypedResults.NoContent();
    }

    private static async Task<Ok<ModerationSummary>> Summary(AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new ModerationSummary(await PendingPosts(db).CountAsync(ct)));

    /// <summary>Reported posts from every community, most-reported first, then the longest-waiting.</summary>
    private static async Task<Ok<List<ModerationItem>>> Queue(AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        // Priority order computed in SQL, so only the top of the queue leaves the database.
        var postIds = await PendingPosts(db)
            .OrderByDescending(p => p.Reports.Count(r => r.ResolvedAt == null))
            .ThenBy(p => p.Reports.Where(r => r.ResolvedAt == null).Min(r => r.CreatedAt))
            .Select(p => p.Id)
            .Take(QueueLimit)
            .ToListAsync(ct);

        // The same projection every other endpoint uses, so anonymous authors stay
        // anonymous to admins too.
        var posts = await db.Posts.Where(p => postIds.Contains(p.Id)).ToDto(me).ToDictionaryAsync(p => p.Id, ct);

        // Reason, note and time only; the reporter column is never selected.
        var reports = (await db.Reports
                .Where(r => postIds.Contains(r.PostId) && r.ResolvedAt == null)
                .OrderBy(r => r.CreatedAt)
                .Select(r => new { r.PostId, r.Reason, r.Details, r.CreatedAt })
                .ToListAsync(ct))
            .ToLookup(r => r.PostId);

        var items = postIds.Select(id =>
        {
            var open = reports[id].ToList();
            return new ModerationItem(
                posts[id],
                open.Count,
                open.GroupBy(r => r.Reason).ToDictionary(g => g.Key, g => g.Count()),
                open.Where(r => r.Details is not null).Select(r => r.Details!).ToList(),
                open[0].CreatedAt);
        }).ToList();

        return TypedResults.Ok(items);
    }

    /// <summary>
    /// A post's image for admins, including when the post is hidden and its public image
    /// URL is off. Authenticated, so the client fetches it with its token.
    /// </summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> Image(
        int postId, AppDbContext db, CancellationToken ct)
    {
        var image = await db.PostImages
            .Where(i => i.PostId == postId)
            .Select(i => new { i.ContentType, i.Data })
            .SingleOrDefaultAsync(ct);

        return image is null ? TypedResults.NotFound() : TypedResults.File(image.Data, image.ContentType);
    }

    private static async Task<Results<Ok<PostDto>, NotFound>> Hide(
        int postId, AppDbContext db, ClaimsPrincipal principal, TimeProvider time, LiveUpdatePublisher live, CancellationToken ct)
    {
        var me = principal.GetUserId();

        var post = await FindPostAsync(db, postId, ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        if (post.ModerationStatus != ModerationStatus.Hidden)
        {
            var now = time.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            post.ModerationStatus = ModerationStatus.Hidden;

            // Tell the author, without saying which admin. ActorUserId stays internal.
            if (post.AuthorId != me)
            {
                db.Notifications.Add(new Notification
                {
                    RecipientUserId = post.AuthorId,
                    PostId = postId,
                    ActorUserId = me,
                    Type = NotificationType.PostHidden,
                    CreatedAt = now,
                });
            }

            await db.SaveChangesAsync(ct);
            await ResolveOpenReportsAsync(db, postId, ReportResolution.PostHidden, me, now, ct);
            await transaction.CommitAsync(ct);

            if (post.AuthorId != me)
            {
                await live.NotificationsChangedAsync(post.AuthorId, ct);
            }

            await live.ModerationChangedAsync(ct);
        }

        return TypedResults.Ok(await db.Posts.Where(p => p.Id == postId).ToDto(me).SingleAsync(ct));
    }

    /// <summary>"Keep post": dismiss its open reports and make it visible again.</summary>
    private static async Task<Results<Ok<PostDto>, NotFound>> Restore(
        int postId, AppDbContext db, ClaimsPrincipal principal, TimeProvider time, LiveUpdatePublisher live, CancellationToken ct)
    {
        var me = principal.GetUserId();

        var post = await FindPostAsync(db, postId, ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        post.ModerationStatus = ModerationStatus.Visible;
        await db.SaveChangesAsync(ct);

        await ResolveOpenReportsAsync(db, postId, ReportResolution.Dismissed, me, time.GetUtcNow(), ct);

        // Undo the "your post was hidden" notice; it isn't true anymore.
        var hiddenNoticesRemoved = await db.Notifications
            .Where(n => n.PostId == postId && n.Type == NotificationType.PostHidden)
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);

        if (hiddenNoticesRemoved > 0)
        {
            await live.NotificationsChangedAsync(post.AuthorId, ct);
        }

        await live.ModerationChangedAsync(ct);

        return TypedResults.Ok(await db.Posts.Where(p => p.Id == postId).ToDto(me).SingleAsync(ct));
    }

    // --- Shared query pieces ------------------------------------------------------------

    /// <summary>Posts awaiting a decision: open reports, not archived by their author.</summary>
    private static IQueryable<Post> PendingPosts(AppDbContext db) =>
        db.Posts.Where(p => p.Status != PostStatus.Archived && p.Reports.Any(r => r.ResolvedAt == null));

    /// <summary>Archived posts are the author's to keep or delete; moderation leaves them alone.</summary>
    private static Task<Post?> FindPostAsync(AppDbContext db, int postId, CancellationToken ct) =>
        db.Posts.SingleOrDefaultAsync(p => p.Id == postId && p.Status != PostStatus.Archived, ct);

    private static Task<int> ResolveOpenReportsAsync(
        AppDbContext db, int postId, ReportResolution resolution, Guid admin, DateTimeOffset now, CancellationToken ct) =>
        db.Reports
            .Where(r => r.PostId == postId && r.ResolvedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ResolvedAt, now)
                .SetProperty(r => r.Resolution, resolution)
                .SetProperty(r => r.ResolvedByUserId, admin), ct);
}

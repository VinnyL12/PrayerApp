using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Posts;

/// <summary>
/// Follow-ups on a post. Anyone who can see the post can read them; only its author can
/// add or delete them; everyone who responded is notified of each new one.
/// </summary>
public static class PostUpdateEndpoints
{
    public static IEndpointRouteBuilder MapPostUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/posts/{postId:int}/updates").WithTags("Post updates");

        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapDelete("/{updateId:int}", Delete);

        return app;
    }

    /// <summary>Newest first: someone coming back wants the latest news on top.</summary>
    private static async Task<Results<Ok<List<PostUpdateDto>>, NotFound>> List(
        int postId, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!await db.Posts.Where(p => p.Id == postId).VisibleTo(me).AnyAsync(ct))
        {
            return TypedResults.NotFound();
        }

        var updates = await db.PostUpdates
            .Where(u => u.PostId == postId)
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.Id)
            .Select(u => new PostUpdateDto(u.Id, u.Body, u.CreatedAt))
            .ToListAsync(ct);

        return TypedResults.Ok(updates);
    }

    private static async Task<Results<Created<PostUpdateCreated>, NotFound, ProblemHttpResult, ValidationProblem>> Create(
        int postId,
        CreatePostUpdateRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        TimeProvider time,
        CancellationToken ct)
    {
        var me = principal.GetUserId();

        var body = request.Body.Trim();
        if (body.Length == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Body)] = ["Write something before posting an update."],
            });
        }

        // Someone else's post looks identical to a missing one.
        var post = await db.Posts
            .Where(p => p.Id == postId && p.AuthorId == me)
            .Select(p => new { p.Status, p.ModerationStatus, Updates = p.Updates.Count })
            .SingleOrDefaultAsync(ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        if (post.Status == PostStatus.Archived)
        {
            return TypedResults.Problem("This post is archived.", statusCode: StatusCodes.Status409Conflict);
        }

        // Supporters can't open a hidden post, so a notification about it would lead nowhere.
        if (post.ModerationStatus != ModerationStatus.Visible)
        {
            return TypedResults.Problem("This post is hidden while it's reviewed, so it can't get updates.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (post.Updates >= PostUpdateRules.MaxPerPost)
        {
            return TypedResults.Problem($"A post can have at most {PostUpdateRules.MaxPerPost} updates.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var update = new PostUpdate { PostId = postId, Body = body, CreatedAt = now };
        db.PostUpdates.Add(update);
        await db.SaveChangesAsync(ct);

        // Fan-out on write, in one statement: a notification row for every current
        // supporter, built inside SQL Server from the Acknowledgments table. One round trip
        // whether 3 people responded or 3,000, and it commits or rolls back with the update.
        // (Queueing one event per supporter instead could overflow the 1,000-slot
        // in-memory queue and silently drop some.) Interpolated values become parameters.
        var notified = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Notifications (RecipientUserId, PostId, PostUpdateId, ActorUserId, Type, IsRead, CreatedAt)
            SELECT a.UserId, a.PostId, {update.Id}, {me}, {(int)NotificationType.PostUpdated}, 0, {now}
            FROM Acknowledgments a
            WHERE a.PostId = {postId} AND a.UserId <> {me}
            """, ct);

        await transaction.CommitAsync(ct);

        var dto = new PostUpdateDto(update.Id, update.Body, update.CreatedAt);
        return TypedResults.Created($"/posts/{postId}/updates/{update.Id}", new PostUpdateCreated(dto, notified));
    }

    /// <summary>For a mistake or an overshare. Its notifications go with it.</summary>
    private static async Task<Results<NoContent, NotFound>> Delete(
        int postId, int updateId, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!await db.PostUpdates.AnyAsync(u => u.Id == updateId && u.PostId == postId && u.Post.AuthorId == me, ct))
        {
            return TypedResults.NotFound();
        }

        // Notifications first: their foreign key to the update is NoAction (see
        // NotificationConfiguration), so the update can't be deleted while they point at it.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Notifications.Where(n => n.PostUpdateId == updateId).ExecuteDeleteAsync(ct);
        await db.PostUpdates.Where(u => u.Id == updateId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);

        return TypedResults.NoContent();
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;
using PrayerApp.Domain.Rules;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Posts;

public static class PostEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;
    private const int MyPostsLimit = 100;

    public static IEndpointRouteBuilder MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Posts");

        group.MapGet("/communities/{communityId:int}/posts", Feed);
        group.MapPost("/communities/{communityId:int}/posts", Create);
        group.MapGet("/posts/{id:int}", Get);
        group.MapPatch("/posts/{id:int}/status", UpdateStatus);
        group.MapGet("/me/posts", MyPosts);

        return app;
    }

    /// <summary>
    /// Newest-first feed with keyset pagination. Phase 4 replaces the ordering with
    /// the ranking score; the membership check and paging shape stay.
    /// </summary>
    private static async Task<Results<Ok<FeedPage>, NotFound, ForbidHttpResult, ValidationProblem>> Feed(
        int communityId,
        string? cursor,
        int? limit,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var me = principal.GetUserId();

        var access = await db.Communities
            .Where(c => c.Id == communityId)
            .Select(c => new { IsMember = c.Memberships.Any(m => m.UserId == me) })
            .SingleOrDefaultAsync(ct);

        if (access is null)
        {
            return TypedResults.NotFound();
        }

        if (!access.IsMember)
        {
            return TypedResults.Forbid();
        }

        var query = db.Posts.Where(p => p.CommunityId == communityId && p.Status == PostStatus.Active);

        if (cursor is not null)
        {
            if (!FeedCursor.TryDecode(cursor, out var after))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cursor"] = ["Invalid cursor."],
                });
            }

            // "Strictly after the last item" in (CreatedAt DESC, Id DESC) order. Unlike
            // OFFSET paging, this stays correct when new posts arrive between page loads,
            // and it's an index seek instead of skipping N rows.
            query = query.Where(p => p.CreatedAt < after.CreatedAt
                || (p.CreatedAt == after.CreatedAt && p.Id < after.Id));
        }

        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);

        // Fetch one extra row: if it exists there's a next page, and we didn't need a COUNT(*).
        var posts = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(pageSize + 1)
            .ToDto(me)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (posts.Count > pageSize)
        {
            posts.RemoveAt(pageSize);
            var last = posts[^1];
            nextCursor = new FeedCursor(last.CreatedAt, last.Id).Encode();
        }

        return TypedResults.Ok(new FeedPage(posts, nextCursor));
    }

    private static async Task<Results<Created<PostDto>, NotFound, ForbidHttpResult, ValidationProblem>> Create(
        int communityId,
        CreatePostRequest request,
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
                [nameof(request.Body)] = ["Body cannot be empty."],
            });
        }

        var access = await db.Communities
            .Where(c => c.Id == communityId)
            .Select(c => new { IsMember = c.Memberships.Any(m => m.UserId == me) })
            .SingleOrDefaultAsync(ct);

        if (access is null)
        {
            return TypedResults.NotFound();
        }

        // App-layer enforcement of "author must be a member" (decided at kickoff
        // instead of a composite foreign key, so users can leave communities freely).
        if (!access.IsMember)
        {
            return TypedResults.Forbid();
        }

        var post = new Post
        {
            AuthorId = me,
            CommunityId = communityId,
            Body = body,
            IsUrgent = request.IsUrgent,
            IsAnonymous = request.IsAnonymous,
            CreatedAt = time.GetUtcNow(),
        };

        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);

        var dto = await db.Posts.Where(p => p.Id == post.Id).ToDto(me).SingleAsync(ct);
        return TypedResults.Created($"/posts/{post.Id}", dto);
    }

    private static async Task<Results<Ok<PostDto>, NotFound>> Get(
        int id, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        var post = await db.Posts
            .Where(p => p.Id == id)
            .VisibleTo(me)
            .ToDto(me)
            .SingleOrDefaultAsync(ct);

        return post is null ? TypedResults.NotFound() : TypedResults.Ok(post);
    }

    private static async Task<Results<Ok<PostDto>, NotFound, ProblemHttpResult, ValidationProblem>> UpdateStatus(
        int id,
        UpdatePostStatusRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!Enum.IsDefined(request.Status))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Status)] = ["Unknown status."],
            });
        }

        // Someone else's post looks identical to a missing one.
        var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == id && p.AuthorId == me, ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        if (post.Status != request.Status)
        {
            if (!PostStatusTransitions.CanTransition(post.Status, request.Status))
            {
                return TypedResults.Problem(
                    $"A post can't move from {post.Status} to {request.Status}.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            post.Status = request.Status;
            await db.SaveChangesAsync(ct);
        }

        var dto = await db.Posts.Where(p => p.Id == id).ToDto(me).SingleAsync(ct);
        return TypedResults.Ok(dto);
    }

    private static async Task<Ok<List<PostDto>>> MyPosts(AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        var posts = await db.Posts
            .Where(p => p.AuthorId == me)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(MyPostsLimit)
            .ToDto(me)
            .ToListAsync(ct);

        return TypedResults.Ok(posts);
    }
}

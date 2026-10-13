using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;
using PrayerApp.Domain.Ranking;
using PrayerApp.Domain.Rules;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Posts;

public static class PostEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 50;
    private const int MyPostsLimit = 100;
    private const int CandidateWindowDays = 30;
    private const int MaxCandidates = 500;

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
    /// Ranked feed (see FeedRanker). Candidates are fetched by recency from the feed
    /// index, scored in memory, and the top page selected with a bounded heap.
    /// </summary>
    private static async Task<Results<Ok<FeedPage>, NotFound, ForbidHttpResult, ValidationProblem>> Feed(
        int communityId,
        string? cursor,
        int? limit,
        AppDbContext db,
        ClaimsPrincipal principal,
        TimeProvider time,
        IOptions<RankingOptions> ranking,
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

        FeedCursor? previous = null;
        if (cursor is not null)
        {
            if (!FeedCursor.TryDecode(cursor, out var decoded))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cursor"] = ["Invalid cursor."],
                });
            }

            previous = decoded;
        }

        // Every page of one scroll session is ranked as of the same instant.
        var asOf = previous?.AsOf ?? time.GetUtcNow();
        var velocityWindowStart = asOf.AddHours(-24);
        var oldestCandidate = asOf.AddDays(-CandidateWindowDays);

        // One query: the newest active and answered posts as of asOf, each with its count
        // of acknowledgments in the 24 hours before asOf (a range seek on the
        // (PostId, CreatedAt) index per post). Older posts have decayed to near-zero
        // scores, so capping the candidate set loses nothing a reader would reach.
        //
        // Answered posts stay so the community can celebrate them, and they rank exactly
        // like active ones: a score that changed when the author marked a post answered
        // would shift it mid-scroll and break the as-of paging guarantee. On the
        // (CommunityId, Status, CreatedAt) index this is two seeks, one per status.
        var candidates = await db.Posts
            .Where(p => p.CommunityId == communityId
                && p.Status != PostStatus.Archived
                && p.ModerationStatus == ModerationStatus.Visible
                // Once you've reported a post, you don't have to keep seeing it.
                && !p.Reports.Any(r => r.ReporterUserId == me)
                && p.CreatedAt <= asOf
                && p.CreatedAt >= oldestCandidate)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(MaxCandidates)
            .Select(p => new RankingInput(
                p.Id,
                p.CreatedAt,
                p.Acknowledgments.Count(a => a.CreatedAt > velocityWindowStart && a.CreatedAt <= asOf),
                p.IsUrgent))
            .ToListAsync(ct);

        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);

        // Ask for one extra: if it exists there's another page.
        var ranked = FeedRanker.TopK(candidates, asOf, pageSize + 1, ranking.Value, previous?.Last);
        var page = ranked.Take(pageSize).ToList();

        var pageIds = page.Select(r => r.PostId).ToList();
        var dtos = await db.Posts
            .Where(p => pageIds.Contains(p.Id))
            .ToDto(me)
            .ToDictionaryAsync(p => p.Id, ct);

        // SQL returns rows in no particular order; restore rank order.
        var posts = page.Select(r => dtos[r.PostId]).ToList();
        var nextCursor = ranked.Count > pageSize ? new FeedCursor(asOf, page[^1]).Encode() : null;

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

        if (!PostLinks.TryNormalize(request.Link, out var link, out var linkError))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Link)] = [linkError!],
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
            Link = link,
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

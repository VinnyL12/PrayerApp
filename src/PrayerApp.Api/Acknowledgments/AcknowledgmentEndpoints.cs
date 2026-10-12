using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
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
        group.MapGet("/summary", Summary);

        return app;
    }

    private static async Task<Results<Ok<AcknowledgmentResult>, NotFound, ProblemHttpResult, ValidationProblem>> Acknowledge(
        int postId,
        AcknowledgeRequest request,
        AppDbContext db,
        ClaimsPrincipal principal,
        TimeProvider time,
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

        var post = await db.Posts.AsNoTracking().Where(p => p.Id == postId).VisibleTo(me).SingleOrDefaultAsync(ct);
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

        // Friendly fast path for the common case (user taps twice, seconds apart). It can't
        // be the only guard: two simultaneous requests would both pass it. See catch below.
        if (await db.Acknowledgments.AnyAsync(a => a.PostId == postId && a.UserId == me, ct))
        {
            return AlreadyAcknowledged();
        }

        // The row insert and the counter increment commit together or not at all, so
        // Post.AcknowledgmentCount always equals the number of Acknowledgment rows.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.Acknowledgments.Add(new Acknowledgment
        {
            PostId = postId,
            UserId = me,
            Type = request.Type,
            CreatedAt = time.GetUtcNow(),
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

        var count = await db.Posts.Where(p => p.Id == postId).Select(p => p.AcknowledgmentCount).SingleAsync(ct);
        return TypedResults.Ok(new AcknowledgmentResult(postId, request.Type, count));
    }

    private static ProblemHttpResult AlreadyAcknowledged() =>
        TypedResults.Problem("You've already acknowledged this post.", statusCode: StatusCodes.Status409Conflict);

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

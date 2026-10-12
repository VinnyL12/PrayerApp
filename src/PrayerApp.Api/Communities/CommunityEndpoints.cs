using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Communities;

namespace PrayerApp.Api.Communities;

public static class CommunityEndpoints
{
    public static IEndpointRouteBuilder MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/communities").WithTags("Communities");

        group.MapGet("/", List);
        group.MapGet("/{id:int}", Get);

        // PUT/DELETE on a "membership" sub-resource makes join and leave idempotent:
        // joining twice or leaving twice is a harmless no-op, so clients can safely retry.
        group.MapPut("/{id:int}/membership", Join);
        group.MapDelete("/{id:int}/membership", Leave);

        return app;
    }

    private static async Task<Ok<List<CommunityDto>>> List(AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var communities = await db.Communities
            .OrderBy(c => c.Id)
            .ToDto(principal.GetUserId())
            .ToListAsync(ct);

        return TypedResults.Ok(communities);
    }

    private static async Task<Results<Ok<CommunityDto>, NotFound>> Get(
        int id, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var community = await db.Communities
            .Where(c => c.Id == id)
            .ToDto(principal.GetUserId())
            .SingleOrDefaultAsync(ct);

        return community is null ? TypedResults.NotFound() : TypedResults.Ok(community);
    }

    private static async Task<Results<NoContent, NotFound>> Join(
        int id, AppDbContext db, ClaimsPrincipal principal, TimeProvider time, CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!await db.Communities.AnyAsync(c => c.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        if (await db.CommunityMemberships.AnyAsync(m => m.UserId == me && m.CommunityId == id, ct))
        {
            return TypedResults.NoContent();
        }

        db.CommunityMemberships.Add(new CommunityMembership
        {
            UserId = me,
            CommunityId = id,
            JoinedAt = time.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request joined first. The outcome the caller wanted is true.
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> Leave(
        int id, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!await db.Communities.AnyAsync(c => c.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        // The user's posts stay in the community; they just stop seeing its feed.
        await db.CommunityMemberships
            .Where(m => m.UserId == me && m.CommunityId == id)
            .ExecuteDeleteAsync(ct);

        return TypedResults.NoContent();
    }

    private static IQueryable<CommunityDto> ToDto(this IQueryable<Community> communities, Guid me) =>
        communities.Select(c => new CommunityDto(
            c.Id,
            c.Name,
            c.Description,
            c.Category,
            c.Memberships.Count(),
            c.Memberships.Any(m => m.UserId == me)));
}

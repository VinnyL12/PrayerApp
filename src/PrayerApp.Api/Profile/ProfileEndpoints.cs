using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Admin;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Shared.Contracts.Profile;

namespace PrayerApp.Api.Profile;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me/profile", Get).WithTags("Profile");
        return app;
    }

    private static async Task<Results<Ok<ProfileDto>, UnauthorizedHttpResult>> Get(
        AppDbContext db, AdminAccess admins, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        // One query: each count is a correlated subquery SQL Server answers from an index
        // (posts by author, acknowledgments by user, memberships by user). Responses
        // received sums the denormalized counter instead of counting acknowledgment rows.
        var row = await db.Users
            .Where(u => u.Id == me)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                Email = u.Email!,
                u.CreatedAt,
                Posts = db.Posts.Count(p => p.AuthorId == me),
                ResponsesGiven = db.Acknowledgments.Count(a => a.UserId == me),
                ResponsesReceived = db.Posts.Where(p => p.AuthorId == me).Sum(p => (int?)p.AcknowledgmentCount) ?? 0,
                Communities = db.CommunityMemberships.Count(m => m.UserId == me),
            })
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new ProfileDto(
            row.Id,
            row.DisplayName,
            row.Email,
            await admins.IsAdminAsync(me, ct),
            admins.IsOwner(row.Email),
            row.CreatedAt,
            row.Posts,
            row.ResponsesGiven,
            row.ResponsesReceived,
            row.Communities));
    }
}

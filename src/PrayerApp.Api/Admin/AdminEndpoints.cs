using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Shared.Contracts.Admin;

namespace PrayerApp.Api.Admin;

/// <summary>Managing who is an admin. Every endpoint here requires the Admin policy.</summary>
public static class AdminEndpoints
{
    private const int MaxResults = 50;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization(Policies.Admin);

        group.MapGet("/users", ListUsers);
        group.MapPut("/users/{userId:guid}/admin", Promote);
        group.MapDelete("/users/{userId:guid}/admin", Demote);

        return app;
    }

    /// <summary>Admins first, then everyone else by name. Optional search on name or email.</summary>
    private static async Task<Ok<List<AdminUserDto>>> ListUsers(
        string? search, AppDbContext db, AdminAccess access, CancellationToken ct)
    {
        var users = db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            users = users.Where(u => u.DisplayName.Contains(term) || u.Email!.Contains(term));
        }

        var rows = await users
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                Email = u.Email!,
                HasRole = db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == AdminRole.Id),
                u.CreatedAt,
            })
            .OrderByDescending(u => u.HasRole)
            .ThenBy(u => u.DisplayName)
            .Take(MaxResults)
            .ToListAsync(ct);

        // Owners come from configuration, not SQL, so that part is applied here. An owner
        // without a role row still counts as an admin.
        var result = rows
            .Select(u =>
            {
                var isOwner = access.IsOwner(u.Email);
                return new AdminUserDto(u.Id, u.DisplayName, u.Email, u.HasRole || isOwner, isOwner, u.CreatedAt);
            })
            .OrderByDescending(u => u.IsAdmin)
            .ThenBy(u => u.DisplayName)
            .ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Promote(
        Guid userId, AppDbContext db, AdminAccess access, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
        {
            return TypedResults.NotFound();
        }

        return await ChangeRoleAsync(db, access, me, ct, async () =>
        {
            if (!await db.UserRoles.AnyAsync(r => r.UserId == userId && r.RoleId == AdminRole.Id, ct))
            {
                db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = AdminRole.Id });
                await db.SaveChangesAsync(ct);
            }
        });
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Demote(
        Guid userId, AppDbContext db, AdminAccess access, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        // Because no admin can remove themselves, the admin making a change is always
        // still an admin afterwards: the app can't reach zero admins this way.
        if (userId == me)
        {
            return TypedResults.Problem("You can't remove your own admin role. Ask another admin.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(ct);
        if (email is null)
        {
            return TypedResults.NotFound();
        }

        if (access.IsOwner(email))
        {
            return TypedResults.Problem("This admin is an owner, set in the app's configuration, and can't be removed here.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return await ChangeRoleAsync(db, access, me, ct, () =>
            db.UserRoles.Where(r => r.UserId == userId && r.RoleId == AdminRole.Id).ExecuteDeleteAsync(ct));
    }

    /// <summary>
    /// Runs one role change while holding a lock that every role change takes first, so
    /// changes happen one at a time.
    ///
    /// Without it, two admins demoting each other at the same moment could both pass the
    /// "is the caller still an admin?" check (each sees the other's change as not yet
    /// committed) and both succeed, leaving neither as admin. With it, the second waits,
    /// then re-checks and finds its caller is no longer an admin.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ChangeRoleAsync(
        AppDbContext db, AdminAccess access, Guid me, CancellationToken ct, Func<Task> change)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // UPDLOCK: only one transaction holds it at a time. HOLDLOCK: keep it until commit.
        // Locking the single Admin row in AspNetRoles is a cheap mutex all role changes share.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT Id FROM AspNetRoles WITH (UPDLOCK, HOLDLOCK) WHERE Id = {AdminRole.Id}", ct);

        // The policy checked this when the request arrived; another admin may have demoted
        // the caller since. Inside the lock, this answer can't go stale. (AdminAccess is
        // scoped, so it shares this request's DbContext and runs inside the transaction.)
        if (!await access.IsAdminAsync(me, ct))
        {
            return TypedResults.Problem("You're no longer an admin.", statusCode: StatusCodes.Status403Forbidden);
        }

        await change();
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrayerApp.Api.Data;

namespace PrayerApp.Api.Admin;

/// <summary>
/// Answers "is this user an admin?" from the source of truth: an owner email in
/// configuration, or an Admin row in AspNetUserRoles.
/// </summary>
public sealed class AdminAccess(AppDbContext db, IOptions<AdminOptions> options)
{
    // A hash set: O(1) per lookup however many owners are configured. Emails compare
    // case-insensitively, like Identity's own email matching.
    private readonly HashSet<string> _owners = new(
        options.Value.Emails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()),
        StringComparer.OrdinalIgnoreCase);

    public bool IsOwner(string? email) => email is not null && _owners.Contains(email);

    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct = default)
    {
        // One round trip for both halves of the rule. The role check is a seek on
        // AspNetUserRoles' (UserId, RoleId) primary key.
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Email,
                HasRole = db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == AdminRole.Id),
            })
            .SingleOrDefaultAsync(ct);

        return user is not null && (user.HasRole || IsOwner(user.Email));
    }
}

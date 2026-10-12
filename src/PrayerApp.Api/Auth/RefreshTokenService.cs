using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Auth;

public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

public sealed record RotationResult(User User, IssuedRefreshToken RefreshToken);

/// <summary>Stores, rotates and revokes refresh tokens.</summary>
public sealed class RefreshTokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider time)
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>Starts a new token family (a fresh login).</summary>
    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken ct)
    {
        var issued = AddToken(userId, familyId: Guid.NewGuid(), time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return issued;
    }

    /// <summary>
    /// Exchanges a valid refresh token for a new one. Returns null if the token is
    /// unknown, expired, or already used (in which case its family is revoked).
    /// </summary>
    public async Task<RotationResult?> RotateAsync(string presentedToken, CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(presentedToken);
        var existing = await db.RefreshTokens
            .AsNoTracking()
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (existing is null)
        {
            return null;
        }

        var now = time.GetUtcNow();

        if (existing.RevokedAt is not null)
        {
            // Reuse detected: this token was already exchanged, so someone else holds a
            // copy. We can't tell which party is legitimate, so end the whole session.
            await RevokeFamilyAsync(existing.FamilyId, now, ct);
            return null;
        }

        if (existing.ExpiresAt <= now)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Atomic compare-and-swap: only succeeds if the token is still unrevoked at the
        // moment of the UPDATE. If two requests race with the same token, exactly one
        // gets rowsAffected == 1; the other is treated as reuse.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == existing.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(ct);
            await RevokeFamilyAsync(existing.FamilyId, now, ct);
            return null;
        }

        var issued = AddToken(existing.UserId, existing.FamilyId, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new RotationResult(existing.User, issued);
    }

    /// <summary>Logout: revokes the presented token's whole family.</summary>
    public async Task RevokeAsync(string presentedToken, CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(presentedToken);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);

        if (familyId is not null)
        {
            await RevokeFamilyAsync(familyId.Value, time.GetUtcNow(), ct);
        }
    }

    private IssuedRefreshToken AddToken(Guid userId, Guid familyId, DateTimeOffset now)
    {
        var token = TokenService.GenerateRefreshToken();
        var expiresAt = now.AddDays(_options.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = TokenService.HashRefreshToken(token),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });

        return new IssuedRefreshToken(token, expiresAt);
    }

    private Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
}

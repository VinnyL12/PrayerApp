namespace PrayerApp.Domain.Entities;

/// <summary>
/// A single-use refresh token. Only the SHA-256 hash is stored, so a database leak
/// doesn't hand out working tokens. Each refresh revokes the presented token and
/// issues a new one in the same family; presenting an already-revoked token means
/// a copy was stolen, and the whole family is revoked.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    public Guid UserId { get; set; }

    public required byte[] TokenHash { get; set; }

    // Every token descended from one login shares a FamilyId.
    public Guid FamilyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public User User { get; set; } = null!;
}

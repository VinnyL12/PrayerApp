using Microsoft.AspNetCore.Identity;

namespace PrayerApp.Domain.Entities;

/// <summary>
/// Application user. IdentityUser supplies Email, PasswordHash, lockout fields, etc.;
/// we only add what Prayer App needs. Keyed by Guid rather than Identity's default string.
/// </summary>
public class User : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<CommunityMembership> Memberships { get; } = [];

    public ICollection<Post> Posts { get; } = [];

    public ICollection<Acknowledgment> Acknowledgments { get; } = [];

    public ICollection<Notification> Notifications { get; } = [];

    public ICollection<RefreshToken> RefreshTokens { get; } = [];
}

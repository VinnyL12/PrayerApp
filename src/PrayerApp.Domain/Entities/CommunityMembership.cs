namespace PrayerApp.Domain.Entities;

/// <summary>
/// Join table between User and Community, with its own data (join date).
/// Primary key is the composite (UserId, CommunityId), so a user can join a community once.
/// </summary>
public class CommunityMembership
{
    public Guid UserId { get; set; }

    public int CommunityId { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    public User User { get; set; } = null!;

    public Community Community { get; set; } = null!;
}

using PrayerApp.Shared.Enums;

namespace PrayerApp.Domain.Entities;

public class Community
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public CommunityCategory Category { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Member count is computed from Memberships rather than stored: communities are
    // few and the count isn't on a hot path, so a counter isn't worth keeping in sync.
    public ICollection<CommunityMembership> Memberships { get; } = [];

    public ICollection<Post> Posts { get; } = [];
}

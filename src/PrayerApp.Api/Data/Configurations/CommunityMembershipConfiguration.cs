using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data.Configurations;

public class CommunityMembershipConfiguration : IEntityTypeConfiguration<CommunityMembership>
{
    public void Configure(EntityTypeBuilder<CommunityMembership> builder)
    {
        // Composite primary key. The underlying B-tree is sorted by UserId first, so it
        // answers "which communities is this user in?" directly. It can't help with
        // "who is in this community?" (leftmost-prefix rule), so EF adds a separate
        // index on CommunityId for that foreign key.
        builder.HasKey(m => new { m.UserId, m.CommunityId });

        // Memberships are pure join data: deleting either side removes them.
        builder.HasOne(m => m.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Community)
            .WithMany(c => c.Memberships)
            .HasForeignKey(m => m.CommunityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

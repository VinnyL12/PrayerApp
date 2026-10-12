using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data.Configurations;

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.Property(p => p.Body).HasMaxLength(2000);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Posts_AcknowledgmentCount_NonNegative", "[AcknowledgmentCount] >= 0"));

        // Feed query: WHERE CommunityId = @c AND Status = Active ORDER BY CreatedAt DESC, Id DESC.
        // Equality columns first, then the sort columns, so SQL Server can seek straight
        // to one community's active posts and read them already in feed order. Id is the
        // tie-breaker that keeps keyset pagination stable.
        builder.HasIndex(p => new { p.CommunityId, p.Status, p.CreatedAt, p.Id })
            .IsDescending(false, false, true, true);

        // Restrict: deleting a user or community must never silently delete posts.
        // (Also required: User -> Post -> Acknowledgment plus User -> Acknowledgment
        // would be two cascade paths, which SQL Server rejects.)
        builder.HasOne(p => p.Author)
            .WithMany(u => u.Posts)
            .HasForeignKey(p => p.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Community)
            .WithMany(c => c.Posts)
            .HasForeignKey(p => p.CommunityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Posts;

namespace PrayerApp.Api.Data.Configurations;

public class PostUpdateConfiguration : IEntityTypeConfiguration<PostUpdate>
{
    public void Configure(EntityTypeBuilder<PostUpdate> builder)
    {
        builder.Property(u => u.Body).HasMaxLength(PostUpdateRules.MaxLength);

        // A post's timeline, newest first, and its latest update for feed cards.
        builder.HasIndex(u => new { u.PostId, u.CreatedAt }).IsDescending(false, true);

        builder.HasOne(u => u.Post)
            .WithMany(p => p.Updates)
            .HasForeignKey(u => u.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

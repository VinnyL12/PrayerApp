using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data.Configurations;

public class AcknowledgmentConfiguration : IEntityTypeConfiguration<Acknowledgment>
{
    public void Configure(EntityTypeBuilder<Acknowledgment> builder)
    {
        // One acknowledgment per user per post, enforced by the database. The API will
        // still check first for a friendly error, but this is the guarantee that holds
        // under concurrent requests (two taps arriving at once).
        builder.HasIndex(a => new { a.PostId, a.UserId }).IsUnique();

        // Velocity: COUNT(*) WHERE PostId = @p AND CreatedAt >= now - 24h is a range seek.
        builder.HasIndex(a => new { a.PostId, a.CreatedAt });

        // Hard-deleting a post removes its acknowledgments (archiving keeps them).
        builder.HasOne(a => a.Post)
            .WithMany(p => p.Acknowledgments)
            .HasForeignKey(a => a.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: cascading here would delete rows without decrementing
        // Post.AcknowledgmentCount. Account deletion will be an explicit workflow.
        builder.HasOne(a => a.User)
            .WithMany(u => u.Acknowledgments)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

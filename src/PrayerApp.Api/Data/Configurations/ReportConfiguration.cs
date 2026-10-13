using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.Property(r => r.Details).HasMaxLength(500);

        // One report per member per post; the final guard against a double submit.
        builder.HasIndex(r => new { r.PostId, r.ReporterUserId }).IsUnique();

        // The queue and the auto-hide check both ask "open reports for this post". A
        // filtered index holds only open rows, so it stays small however many reports
        // have been resolved over time.
        builder.HasIndex(r => r.PostId)
            .HasFilter("[ResolvedAt] IS NULL")
            .HasDatabaseName("IX_Reports_PostId_Open");

        builder.HasOne(r => r.Post)
            .WithMany(p => p.Reports)
            .HasForeignKey(r => r.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, like every other user reference: deleting a user never silently
        // deletes the record of what they did.
        builder.HasOne(r => r.Reporter)
            .WithMany()
            .HasForeignKey(r => r.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ResolvedBy)
            .WithMany()
            .HasForeignKey(r => r.ResolvedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

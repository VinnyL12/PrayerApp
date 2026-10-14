using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data.Configurations;

public class OutboxNotificationConfiguration : IEntityTypeConfiguration<OutboxNotification>
{
    public const int LastErrorMaxLength = 1000;

    public void Configure(EntityTypeBuilder<OutboxNotification> builder)
    {
        builder.ToTable("NotificationOutbox");

        // The clustered key is an ever-increasing identity, so rows are read oldest first
        // straight off the clustered index. No foreign keys, on purpose: a queue row must
        // never block deleting a post or user, and the processor already drops events whose
        // acknowledgment is gone.
        builder.Property(e => e.LastError).HasMaxLength(LastErrorMaxLength);

        // Withdrawing a response deletes its pending row by (post, actor). A seek touches
        // only that row; a scan would also wait on unrelated rows the processor has locked.
        builder.HasIndex(e => new { e.PostId, e.ActorUserId });
    }
}

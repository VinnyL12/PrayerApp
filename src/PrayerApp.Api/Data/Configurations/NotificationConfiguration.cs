using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        // Inbox query: WHERE RecipientUserId = @me AND IsRead = 0 ORDER BY CreatedAt DESC.
        builder.HasIndex(n => new { n.RecipientUserId, n.IsRead, n.CreatedAt })
            .IsDescending(false, false, true);

        // A user's own notifications go with them.
        builder.HasOne(n => n.Recipient)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: Recipient already cascades from Users, and SQL Server allows only
        // one cascade path from a table to another.
        builder.HasOne(n => n.Actor)
            .WithMany()
            .HasForeignKey(n => n.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Post)
            .WithMany()
            .HasForeignKey(n => n.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, not Cascade: Post -> PostUpdate -> Notification would be a second
        // cascade path from Posts to Notifications next to Post -> Notification, which SQL
        // Server rejects. Deleting an update removes its notifications explicitly instead.
        builder.HasOne(n => n.PostUpdate)
            .WithMany()
            .HasForeignKey(n => n.PostUpdateId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

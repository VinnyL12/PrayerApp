using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data.Configurations;

public class PostImageConfiguration : IEntityTypeConfiguration<PostImage>
{
    public void Configure(EntityTypeBuilder<PostImage> builder)
    {
        // The id is generated in C# (Guid.NewGuid) so it's random. Nonclustered, because
        // random keys inserted into a clustered index land on random pages and split them.
        builder.HasKey(i => i.Id).IsClustered(false);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.ContentType).HasMaxLength(32);

        // Unique: at most one image per post. Clustered instead of the GUID: PostId only
        // grows, so new rows append at the end, and it's the column the post join uses.
        builder.HasIndex(i => i.PostId).IsUnique().IsClustered();

        // An image has no meaning without its post.
        builder.HasOne(i => i.Post)
            .WithOne(p => p.Image)
            .HasForeignKey<PostImage>(i => i.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

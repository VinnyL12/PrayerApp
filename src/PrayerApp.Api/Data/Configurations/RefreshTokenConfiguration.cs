using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        // SHA-256 output is always 32 bytes -> binary(32).
        builder.Property(t => t.TokenHash).HasMaxLength(32).IsFixedLength();

        // Every refresh looks a token up by its hash.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Reuse detection revokes a whole family at once.
        builder.HasIndex(t => t.FamilyId);

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

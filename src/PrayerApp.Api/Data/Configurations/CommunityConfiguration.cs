using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Data.Configurations;

public class CommunityConfiguration : IEntityTypeConfiguration<Community>
{
    // Seed data must be deterministic: a value like DateTimeOffset.UtcNow would differ
    // on every build and EF would generate a new "update seed row" migration each time.
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Community> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.Description).HasMaxLength(500);

        builder.HasIndex(c => c.Name).IsUnique();

        builder.HasData(
            Seed(1, "General", "Anything on your heart that doesn't fit elsewhere.", CommunityCategory.General),
            Seed(2, "Health & Healing", "Illness, recovery, surgery, and caring for someone who is unwell.", CommunityCategory.Health),
            Seed(3, "Grief & Loss", "Losing someone or something, and learning to carry it.", CommunityCategory.Grief),
            Seed(4, "Family & Relationships", "Marriage, parenting, family strain, and friendships.", CommunityCategory.Family),
            Seed(5, "Work & Finances", "Job searches, money pressure, and decisions about work.", CommunityCategory.WorkAndFinances),
            Seed(6, "Mental & Emotional Wellbeing", "Anxiety, depression, loneliness, and stress.", CommunityCategory.MentalWellbeing),
            Seed(7, "Gratitude & Answered Prayers", "Share what went right and give thanks.", CommunityCategory.Gratitude));
    }

    private static Community Seed(int id, string name, string description, CommunityCategory category) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        Category = category,
        CreatedAt = SeededAt,
    };
}

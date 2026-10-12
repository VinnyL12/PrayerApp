namespace PrayerApp.Shared.Enums;

/// <summary>
/// Broad topic a community belongs to. For the MVP each seeded community has its
/// own category; the split exists so future communities (e.g. "Cancer Support")
/// can be grouped under an existing category like Health.
/// </summary>
public enum CommunityCategory
{
    General = 1,
    Health = 2,
    Grief = 3,
    Family = 4,
    WorkAndFinances = 5,
    MentalWellbeing = 6,
    Gratitude = 7,
}

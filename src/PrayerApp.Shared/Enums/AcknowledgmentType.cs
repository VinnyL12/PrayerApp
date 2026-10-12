namespace PrayerApp.Shared.Enums;

/// <summary>
/// The fixed set of preset responses a user can give to a post.
/// Stored in the database as an int, so the numeric values are part of the
/// schema: never renumber or reuse a value, only append new ones.
/// </summary>
public enum AcknowledgmentType
{
    PrayingForYou = 1,
    SendingStrength = 2,
    HoldingYouInMyThoughts = 3,
    YoureNotAlone = 4,
    SendingLove = 5,
}

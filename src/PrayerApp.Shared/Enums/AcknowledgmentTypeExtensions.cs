namespace PrayerApp.Shared.Enums;

public static class AcknowledgmentTypeExtensions
{
    /// <summary>User-facing text for each preset response.</summary>
    public static string ToLabel(this AcknowledgmentType type) => type switch
    {
        AcknowledgmentType.PrayingForYou => "Praying for you",
        AcknowledgmentType.SendingStrength => "Sending strength",
        AcknowledgmentType.HoldingYouInMyThoughts => "Holding you in my thoughts",
        AcknowledgmentType.YoureNotAlone => "You're not alone",
        AcknowledgmentType.SendingLove => "Sending love",
        // Enums are just named ints: (AcknowledgmentType)42 compiles, so the
        // compiler can never treat this switch as exhaustive. Fail loudly instead.
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown acknowledgment type."),
    };
}

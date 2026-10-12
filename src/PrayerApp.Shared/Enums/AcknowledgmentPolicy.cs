namespace PrayerApp.Shared.Enums;

/// <summary>
/// Which preset responses fit which posts. Lives in Shared so there is exactly one copy
/// of the rule: the API enforces it and the client uses it to decide which buttons to show.
/// </summary>
public static class AcknowledgmentPolicy
{
    /// <summary>For someone who is struggling, waiting or hurting.</summary>
    public static readonly IReadOnlyList<AcknowledgmentType> Support =
    [
        AcknowledgmentType.PrayingForYou,
        AcknowledgmentType.SendingStrength,
        AcknowledgmentType.HoldingYouInMyThoughts,
        AcknowledgmentType.YoureNotAlone,
        AcknowledgmentType.SendingLove,
    ];

    /// <summary>For good news and answered prayers.</summary>
    public static readonly IReadOnlyList<AcknowledgmentType> Celebration =
    [
        AcknowledgmentType.CelebratingWithYou,
        AcknowledgmentType.RejoicingWithYou,
        AcknowledgmentType.ThankfulWithYou,
        AcknowledgmentType.SendingLove,
    ];

    public static IReadOnlyList<AcknowledgmentType> AllowedFor(CommunityCategory category, PostStatus status) =>
        category switch
        {
            // Never celebratory, whatever the post's status: an "Answered" grief post is
            // still about a loss.
            CommunityCategory.Grief => Support,
            CommunityCategory.Gratitude => Celebration,
            _ when status == PostStatus.Answered => Celebration,
            _ => Support,
        };

    public static bool IsAllowed(AcknowledgmentType type, CommunityCategory category, PostStatus status) =>
        AllowedFor(category, status).Contains(type);
}

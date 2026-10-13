namespace PrayerApp.Shared.Enums;

/// <summary>Why someone reported a post. Stored as an int: append only, never renumber.</summary>
public enum ReportReason
{
    Scam = 1,
    Spam = 2,
    Harassment = 3,
    Inappropriate = 4,
    Other = 5,
}

public static class ReportReasonExtensions
{
    public static string ToLabel(this ReportReason reason) => reason switch
    {
        ReportReason.Scam => "Scam or suspicious link",
        ReportReason.Spam => "Spam or advertising",
        ReportReason.Harassment => "Harassment or hate",
        ReportReason.Inappropriate => "Inappropriate image or content",
        ReportReason.Other => "Something else",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown report reason."),
    };
}

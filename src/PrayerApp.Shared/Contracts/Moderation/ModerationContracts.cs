using System.ComponentModel.DataAnnotations;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Shared.Contracts.Moderation;

public sealed record ReportRequest
{
    [Required]
    public required ReportReason Reason { get; set; }

    [MaxLength(500, ErrorMessage = "Keep it under 500 characters.")]
    public string? Details { get; set; }
}

/// <param name="OpenCount">Posts waiting for a decision, for the nav badge.</param>
public sealed record ModerationSummary(int OpenCount);

/// <summary>
/// One reported post in the admins' queue. Deliberately has no reporter fields: admins
/// see what was reported and why, never by whom.
/// </summary>
/// <param name="Details">The optional notes reporters wrote, oldest first.</param>
public sealed record ModerationItem(
    PostDto Post,
    int OpenReports,
    IReadOnlyDictionary<ReportReason, int> ByReason,
    IReadOnlyList<string> Details,
    DateTimeOffset FirstReportedAt);

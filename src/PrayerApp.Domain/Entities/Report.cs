using PrayerApp.Shared.Enums;

namespace PrayerApp.Domain.Entities;

/// <summary>
/// One member's report of one post. (PostId, ReporterUserId) is unique: a member reports a
/// post at most once. Reporter identity is internal only, never shown to admins or
/// authors, so reporting someone can't start a feud.
/// </summary>
public class Report
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public Guid ReporterUserId { get; set; }

    public ReportReason Reason { get; set; }

    public string? Details { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null while open. Set, with Resolution, when an admin decides.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    public ReportResolution? Resolution { get; set; }

    /// <summary>Which admin decided. Kept for accountability; not exposed by the API.</summary>
    public Guid? ResolvedByUserId { get; set; }

    public Post Post { get; set; } = null!;

    public User Reporter { get; set; } = null!;

    public User? ResolvedBy { get; set; }
}

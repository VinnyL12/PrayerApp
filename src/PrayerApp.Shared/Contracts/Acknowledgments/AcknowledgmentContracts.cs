using System.ComponentModel.DataAnnotations;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Shared.Contracts.Acknowledgments;

public sealed record AcknowledgeRequest
{
    [Required]
    public required AcknowledgmentType Type { get; init; }
}

public sealed record AcknowledgmentResult(int PostId, AcknowledgmentType Type, int AcknowledgmentCount);

/// <summary>Author-only view: how many people chose each response. Never who.</summary>
public sealed record AcknowledgmentSummary(int PostId, int Total, IReadOnlyDictionary<AcknowledgmentType, int> ByType);

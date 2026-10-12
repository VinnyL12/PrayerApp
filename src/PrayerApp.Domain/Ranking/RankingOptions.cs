namespace PrayerApp.Domain.Ranking;

/// <summary>
/// Tunable constants for <see cref="FeedRanker"/>. Bound from the "Ranking" config
/// section so they can be adjusted without a code change.
/// </summary>
public sealed class RankingOptions
{
    /// <summary>Weight of each acknowledgment received in the last 24 hours.</summary>
    public double VelocityWeight { get; init; } = 1.0;

    /// <summary>
    /// Head start for urgent posts, in "acknowledgment equivalents": with the default 3,
    /// an urgent post ranks like a normal post that already has 3 more recent responses.
    /// </summary>
    public double UrgencyWeight { get; init; } = 3.0;

    /// <summary>How fast scores decay with age. Higher means fresher posts dominate.</summary>
    public double Gravity { get; init; } = 1.5;

    /// <summary>Added to age so brand-new posts don't divide by ~0 and spike to infinity.</summary>
    public double AgeOffsetHours { get; init; } = 2.0;
}

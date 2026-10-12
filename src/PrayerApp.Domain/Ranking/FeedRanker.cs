namespace PrayerApp.Domain.Ranking;

/// <summary>What the ranker needs to know about a post. Nothing else.</summary>
public readonly record struct RankingInput(int PostId, DateTimeOffset CreatedAt, int RecentAcknowledgments, bool IsUrgent);

public readonly record struct RankedPost(int PostId, double Score);

/// <summary>
/// Scores and orders posts for a community feed. Pure functions: no database, no clock
/// (the caller passes "now"), no framework, so every behavior is unit-testable.
/// </summary>
public static class FeedRanker
{
    /// <summary>
    /// Feed order: higher score first; ties broken by higher PostId (newer) first.
    /// A total order, so every pair of distinct posts has exactly one correct sequence,
    /// which keyset pagination depends on.
    /// </summary>
    public static readonly Comparer<RankedPost> FeedOrder = Comparer<RankedPost>.Create((x, y) =>
    {
        var byScore = y.Score.CompareTo(x.Score);
        return byScore != 0 ? byScore : y.PostId.CompareTo(x.PostId);
    });

    // The reverse of feed order. PriorityQueue is a min-heap, so with this comparer its
    // root is the *worst* post currently held, the one to evict when a better one arrives.
    private static readonly Comparer<RankedPost> WorstFirst =
        Comparer<RankedPost>.Create((x, y) => FeedOrder.Compare(y, x));

    /// <summary>
    ///                 VelocityWeight * (acks in last 24h + 1)  +  UrgencyWeight * urgent
    ///   score = ───────────────────────────────────────────────────────────────────────
    ///                           (age in hours + AgeOffsetHours) ^ Gravity
    ///
    /// Urgency sits in the numerator so it decays with age like everything else: an urgent
    /// post gets a head start, not a permanent spot at the top.
    /// </summary>
    public static double Score(RankingInput post, DateTimeOffset now, RankingOptions options)
    {
        // Clamp: a post timestamped slightly in the future (clock skew) counts as brand new.
        var ageHours = Math.Max(0, (now - post.CreatedAt).TotalHours);

        var numerator = options.VelocityWeight * (post.RecentAcknowledgments + 1)
            + (post.IsUrgent ? options.UrgencyWeight : 0);

        return numerator / Math.Pow(ageHours + options.AgeOffsetHours, options.Gravity);
    }

    /// <summary>
    /// The best <paramref name="k"/> posts in feed order, optionally only those ranked
    /// strictly after <paramref name="after"/> (the last post of the previous page).
    ///
    /// Uses a bounded min-heap: O(N log k) time and O(k) memory for N candidates, versus
    /// O(N log N) time and O(N) memory to sort everything and take the first k. For a
    /// 20-post page out of 500 candidates, log k ≈ 4.3 versus log N ≈ 9.
    /// </summary>
    public static IReadOnlyList<RankedPost> TopK(
        IEnumerable<RankingInput> candidates,
        DateTimeOffset now,
        int k,
        RankingOptions options,
        RankedPost? after = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);
        if (k == 0)
        {
            return [];
        }

        var heap = new PriorityQueue<RankedPost, RankedPost>(k, WorstFirst);

        foreach (var candidate in candidates)
        {
            var ranked = new RankedPost(candidate.PostId, Score(candidate, now, options));

            // Already shown on an earlier page.
            if (after is { } previous && FeedOrder.Compare(ranked, previous) <= 0)
            {
                continue;
            }

            if (heap.Count < k)
            {
                heap.Enqueue(ranked, ranked);
            }
            else
            {
                // Push, then pop the worst: O(log k). If the new post is worse than
                // everything held, it's popped straight back out and the heap is unchanged.
                heap.EnqueueDequeue(ranked, ranked);
            }
        }

        // The heap pops worst-first, so fill the result from the back.
        var result = new RankedPost[heap.Count];
        for (var i = result.Length - 1; i >= 0; i--)
        {
            result[i] = heap.Dequeue();
        }

        return result;
    }
}

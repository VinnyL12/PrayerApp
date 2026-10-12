using PrayerApp.Domain.Ranking;

namespace PrayerApp.Tests.Domain;

public class FeedRankerTests
{
    private static readonly RankingOptions Options = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static RankingInput Post(int id, double hoursOld, int recentAcks = 0, bool urgent = false) =>
        new(id, Now.AddHours(-hoursOld), recentAcks, urgent);

    private static double Score(RankingInput post) => FeedRanker.Score(post, Now, Options);

    // ---- Score ---------------------------------------------------------------------

    [Fact]
    public void More_recent_acknowledgments_score_higher()
    {
        Assert.True(Score(Post(1, hoursOld: 5, recentAcks: 10)) > Score(Post(2, hoursOld: 5, recentAcks: 2)));
    }

    [Fact]
    public void Older_posts_score_lower_with_equal_engagement()
    {
        Assert.True(Score(Post(1, hoursOld: 1, recentAcks: 3)) > Score(Post(2, hoursOld: 30, recentAcks: 3)));
    }

    [Fact]
    public void Urgent_posts_get_a_head_start()
    {
        Assert.True(Score(Post(1, hoursOld: 5, urgent: true)) > Score(Post(2, hoursOld: 5)));
    }

    [Fact]
    public void Urgency_decays_so_an_old_urgent_post_loses_to_a_fresh_normal_one()
    {
        var weekOldUrgent = Post(1, hoursOld: 24 * 7, urgent: true);
        var hourOldNormal = Post(2, hoursOld: 1);

        Assert.True(Score(hourOldNormal) > Score(weekOldUrgent));
    }

    [Fact]
    public void Velocity_can_beat_freshness()
    {
        // A day-old post getting lots of support outranks a quiet post from an hour ago.
        Assert.True(Score(Post(1, hoursOld: 24, recentAcks: 40)) > Score(Post(2, hoursOld: 1)));
    }

    [Fact]
    public void Future_timestamps_are_treated_as_brand_new()
    {
        Assert.Equal(Score(Post(1, hoursOld: 0)), Score(Post(1, hoursOld: -3)));
    }

    [Fact]
    public void Brand_new_post_with_no_acknowledgments_has_a_finite_positive_score()
    {
        var score = Score(Post(1, hoursOld: 0));

        Assert.True(double.IsFinite(score));
        Assert.True(score > 0);
    }

    // ---- TopK ----------------------------------------------------------------------

    [Fact]
    public void TopK_matches_a_full_sort_on_random_input()
    {
        // Property test: the heap-based O(N log k) result must equal the obvious
        // O(N log N) sort-everything answer, across many random inputs.
        var random = new Random(12345);

        for (var trial = 0; trial < 200; trial++)
        {
            var candidates = Enumerable.Range(1, random.Next(0, 300))
                .Select(id => Post(id, hoursOld: random.NextDouble() * 200, recentAcks: random.Next(0, 30), urgent: random.Next(10) == 0))
                .ToList();
            var k = random.Next(1, 40);

            var expected = candidates
                .Select(c => new RankedPost(c.PostId, Score(c)))
                .Order(FeedRanker.FeedOrder)
                .Take(k);

            Assert.Equal(expected, FeedRanker.TopK(candidates, Now, k, Options));
        }
    }

    [Fact]
    public void Paging_with_after_visits_every_post_exactly_once_in_order()
    {
        var random = new Random(678);
        var candidates = Enumerable.Range(1, 103)
            .Select(id => Post(id, hoursOld: random.NextDouble() * 100, recentAcks: random.Next(0, 10)))
            .ToList();

        var pages = new List<IReadOnlyList<RankedPost>>();
        RankedPost? after = null;
        while (true)
        {
            var page = FeedRanker.TopK(candidates, Now, k: 10, Options, after);
            if (page.Count == 0)
            {
                break;
            }

            pages.Add(page);
            after = page[^1];
        }

        var allIds = pages.SelectMany(p => p).Select(p => p.PostId).ToList();
        var expected = FeedRanker.TopK(candidates, Now, k: candidates.Count, Options).Select(p => p.PostId);

        Assert.Equal(11, pages.Count);
        Assert.Equal(expected, allIds);
    }

    [Fact]
    public void Ties_are_broken_by_newest_id_first()
    {
        // Identical inputs except id produce identical scores.
        var candidates = new[] { Post(5, hoursOld: 3), Post(9, hoursOld: 3), Post(7, hoursOld: 3) };

        var ranked = FeedRanker.TopK(candidates, Now, k: 3, Options);

        Assert.Equal([9, 7, 5], ranked.Select(r => r.PostId));
    }

    [Fact]
    public void Fewer_candidates_than_k_returns_them_all()
    {
        var ranked = FeedRanker.TopK([Post(1, 1), Post(2, 2)], Now, k: 20, Options);

        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void K_of_zero_returns_nothing_and_negative_k_throws()
    {
        Assert.Empty(FeedRanker.TopK([Post(1, 1)], Now, k: 0, Options));
        Assert.Throws<ArgumentOutOfRangeException>(() => FeedRanker.TopK([Post(1, 1)], Now, k: -1, Options));
    }
}

using System.Buffers.Text;
using System.Text;
using PrayerApp.Domain.Ranking;

namespace PrayerApp.Api.Common;

/// <summary>
/// Cursor for the ranked feed: the moment the first page was ranked (AsOf), plus the
/// score and id of the last post shown.
///
/// Scores change constantly (posts age, acknowledgments arrive), so ranking "now" on
/// page 2 could reshuffle posts across the page boundary and show some twice or never.
/// Instead every page is ranked as of the same instant: only posts and acknowledgments
/// that existed at AsOf count, and age is measured from AsOf. That ranking is
/// reproducible from the database, so the next page is simply "everything ranked after
/// the last post", with no server-side session state.
/// </summary>
public readonly record struct FeedCursor(DateTimeOffset AsOf, RankedPost Last)
{
    public string Encode() => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
        // The score is stored as its exact 64-bit pattern: a decimal round trip could
        // change the last bit and break the strict "ranked after" comparison.
        $"{AsOf.UtcTicks}:{BitConverter.DoubleToInt64Bits(Last.Score)}:{Last.PostId}"));

    public static bool TryDecode(string? value, out FeedCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value)).Split(':');
            if (parts.Length == 3
                && long.TryParse(parts[0], out var ticks)
                && ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks
                && long.TryParse(parts[1], out var scoreBits)
                && int.TryParse(parts[2], out var postId))
            {
                var score = BitConverter.Int64BitsToDouble(scoreBits);
                if (double.IsFinite(score))
                {
                    cursor = new FeedCursor(new DateTimeOffset(ticks, TimeSpan.Zero), new RankedPost(postId, score));
                    return true;
                }
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}

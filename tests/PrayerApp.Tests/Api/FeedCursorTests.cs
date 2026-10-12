using PrayerApp.Api.Common;
using PrayerApp.Domain.Ranking;

namespace PrayerApp.Tests.Api;

public class FeedCursorTests
{
    [Fact]
    public void Round_trips_exactly()
    {
        // Tick-precision timestamp and a score with no short decimal form: both must
        // survive encoding bit-for-bit, or the "ranked after" comparison drifts.
        var original = new FeedCursor(
            new DateTimeOffset(2026, 10, 12, 9, 30, 15, TimeSpan.Zero).AddTicks(1234567),
            new RankedPost(42, 1.0 / 3.0));

        Assert.True(FeedCursor.TryDecode(original.Encode(), out var decoded));
        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("!!!")]
    [InlineData("MTIzOmFiYw")]             // "123:abc"
    [InlineData("MTIzOjkyMjMzNzIwMzY4NTQ3NzU4MDc6MQ")] // NaN score bits
    public void Rejects_malformed_input(string? value)
    {
        Assert.False(FeedCursor.TryDecode(value, out _));
    }
}

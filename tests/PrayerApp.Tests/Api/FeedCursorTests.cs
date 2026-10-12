using PrayerApp.Api.Common;

namespace PrayerApp.Tests.Api;

public class FeedCursorTests
{
    [Fact]
    public void Round_trips_with_full_tick_precision()
    {
        // SQL Server datetimeoffset stores 100ns ticks; a millisecond-precision cursor
        // would skip or repeat posts created within the same millisecond.
        var original = new FeedCursor(new DateTimeOffset(2026, 10, 12, 9, 30, 15, TimeSpan.Zero).AddTicks(1234567), 42);

        Assert.True(FeedCursor.TryDecode(original.Encode(), out var decoded));
        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("!!!")]
    [InlineData("MTIzOmFiYw")] // "123:abc"
    public void Rejects_malformed_input(string? value)
    {
        Assert.False(FeedCursor.TryDecode(value, out _));
    }
}

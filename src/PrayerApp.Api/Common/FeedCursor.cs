using System.Buffers.Text;
using System.Text;

namespace PrayerApp.Api.Common;

/// <summary>
/// Keyset pagination cursor: the (CreatedAt, Id) of the last post on a page.
/// The next page is "posts strictly older than this one", with Id breaking ties
/// between posts created in the same tick. Opaque to clients (base64url).
/// </summary>
public readonly record struct FeedCursor(DateTimeOffset CreatedAt, int Id)
{
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{CreatedAt.UtcTicks}:{Id}"));

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
            if (parts.Length == 2 && long.TryParse(parts[0], out var ticks) && int.TryParse(parts[1], out var id)
                && ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks)
            {
                cursor = new FeedCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}

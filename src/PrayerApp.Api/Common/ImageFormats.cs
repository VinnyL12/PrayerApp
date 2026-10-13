namespace PrayerApp.Api.Common;

/// <summary>
/// Identifies an image by its first bytes ("magic numbers"), never by the file name or
/// the Content-Type the client sent, both of which the client controls. Anything else is
/// refused, SVG included: it's XML that can carry script.
/// </summary>
public static class ImageFormats
{
    private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> Riff => "RIFF"u8;
    private static ReadOnlySpan<byte> WebP => "WEBP"u8;

    /// <returns>The MIME type to store and serve, or null if it isn't a supported image.</returns>
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(Jpeg))
        {
            return "image/jpeg";
        }

        if (data.StartsWith(Png))
        {
            return "image/png";
        }

        // RIFF container: "RIFF", 4-byte size, then the format tag.
        if (data.Length >= 12 && data.StartsWith(Riff) && data[8..12].SequenceEqual(WebP))
        {
            return "image/webp";
        }

        return null;
    }
}

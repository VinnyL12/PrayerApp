namespace PrayerApp.Domain.Entities;

/// <summary>
/// The one image a post can have. A table of its own so the feed's scans over Posts never
/// drag image bytes along; a post's image is read only when someone actually views it.
/// </summary>
public class PostImage
{
    /// <summary>
    /// Random (v4), deliberately: it's the image's public URL (/images/{Id}), so it has to
    /// be unguessable. A sequential id would let anyone walk every image in the database.
    /// </summary>
    public Guid Id { get; set; }

    public int PostId { get; set; }

    /// <summary>Detected from the file's bytes on upload, never taken from the client.</summary>
    public required string ContentType { get; set; }

    public required byte[] Data { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Post Post { get; set; } = null!;
}

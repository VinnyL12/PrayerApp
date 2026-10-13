namespace PrayerApp.Shared.Contracts.Posts;

/// <summary>Limits for a post's image, shared by the composer and the upload endpoint.</summary>
public static class PostImageRules
{
    /// <summary>Upload limit. Photos are resized in the browser first, so most land far below it.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>Longest side, in pixels, after the browser resizes a photo.</summary>
    public const int MaxDimension = 1600;

    /// <summary>What the file picker offers. The server checks the bytes, not this list.</summary>
    public const string AcceptAttribute = "image/jpeg,image/png,image/webp";
}

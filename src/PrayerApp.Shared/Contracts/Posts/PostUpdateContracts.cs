using System.ComponentModel.DataAnnotations;

namespace PrayerApp.Shared.Contracts.Posts;

public static class PostUpdateRules
{
    public const int MaxLength = 1000;

    /// <summary>Enough for a long journey, few enough that supporters aren't flooded.</summary>
    public const int MaxPerPost = 20;
}

public sealed record CreatePostUpdateRequest
{
    [Required(ErrorMessage = "Write something before posting an update."), MaxLength(PostUpdateRules.MaxLength)]
    public required string Body { get; set; }
}

public sealed record PostUpdateDto(int Id, string Body, DateTimeOffset CreatedAt);

/// <param name="Notified">How many supporters were notified, so the author knows it reached people.</param>
public sealed record PostUpdateCreated(PostUpdateDto Update, int Notified);

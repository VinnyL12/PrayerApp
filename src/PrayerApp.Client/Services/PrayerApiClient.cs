using System.Net.Http.Json;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Admin;
using PrayerApp.Shared.Contracts.Communities;
using PrayerApp.Shared.Contracts.Moderation;
using PrayerApp.Shared.Contracts.Notifications;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Client.Services;

/// <summary>
/// Typed client for the API. Its HttpClient comes pre-wired with BearerTokenHandler
/// (see Program.cs), so no method here thinks about tokens.
/// </summary>
public sealed class PrayerApiClient(HttpClient http)
{
    // Communities

    public Task<ApiResult<List<CommunityDto>>> GetCommunitiesAsync() =>
        GetAsync<List<CommunityDto>>("/communities");

    public Task<ApiResult<CommunityDto>> GetCommunityAsync(int id) =>
        GetAsync<CommunityDto>($"/communities/{id}");

    public Task<ApiResult> JoinAsync(int communityId) =>
        SendAsync(HttpMethod.Put, $"/communities/{communityId}/membership");

    public Task<ApiResult> LeaveAsync(int communityId) =>
        SendAsync(HttpMethod.Delete, $"/communities/{communityId}/membership");

    // Posts

    public Task<ApiResult<FeedPage>> GetFeedAsync(int communityId, string? cursor = null) =>
        GetAsync<FeedPage>($"/communities/{communityId}/posts"
            + (cursor is null ? "" : $"?cursor={Uri.EscapeDataString(cursor)}"));

    public Task<ApiResult<PostDto>> CreatePostAsync(int communityId, CreatePostRequest request) =>
        SendAsync<PostDto>(HttpMethod.Post, $"/communities/{communityId}/posts", request);

    public Task<ApiResult<PostDto>> GetPostAsync(int id) =>
        GetAsync<PostDto>($"/posts/{id}");

    public Task<ApiResult<List<PostDto>>> GetMyPostsAsync() =>
        GetAsync<List<PostDto>>("/me/posts");

    /// <summary>Attaches (or replaces) a post's image, as multipart/form-data like an HTML file input.</summary>
    public Task<ApiResult<PostDto>> UploadPostImageAsync(int postId, byte[] image, string contentType, string fileName)
    {
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        // "image" must match the endpoint's IFormFile parameter name.
        var form = new MultipartFormDataContent { { file, "image", fileName } };
        return SendAsync<PostDto>(HttpMethod.Put, $"/posts/{postId}/image", form);
    }

    /// <summary>
    /// Absolute URL for an &lt;img&gt; tag. The browser fetches it directly, without our
    /// HttpClient, so it must point at the API's origin rather than the client's.
    /// </summary>
    public string ImageUrl(Guid imageId) => new Uri(http.BaseAddress!, $"images/{imageId}").ToString();

    public Task<ApiResult<PostDto>> UpdateStatusAsync(int postId, PostStatus status) =>
        SendAsync<PostDto>(HttpMethod.Patch, $"/posts/{postId}/status", new UpdatePostStatusRequest { Status = status });

    // Acknowledgments

    public Task<ApiResult<AcknowledgmentResult>> AcknowledgeAsync(int postId, AcknowledgmentType type) =>
        SendAsync<AcknowledgmentResult>(HttpMethod.Post, $"/posts/{postId}/acknowledgments", new AcknowledgeRequest { Type = type });

    public Task<ApiResult<AcknowledgmentResult>> ChangeAcknowledgmentAsync(int postId, AcknowledgmentType type) =>
        SendAsync<AcknowledgmentResult>(HttpMethod.Put, $"/posts/{postId}/acknowledgments/mine", new AcknowledgeRequest { Type = type });

    public Task<ApiResult<AcknowledgmentRemovedResult>> RemoveAcknowledgmentAsync(int postId) =>
        SendAsync<AcknowledgmentRemovedResult>(HttpMethod.Delete, $"/posts/{postId}/acknowledgments/mine");

    public Task<ApiResult<AcknowledgmentSummary>> GetAcknowledgmentSummaryAsync(int postId) =>
        GetAsync<AcknowledgmentSummary>($"/posts/{postId}/acknowledgments/summary");

    // Notifications

    public Task<ApiResult<NotificationList>> GetNotificationsAsync(bool unreadOnly = false, int limit = 50) =>
        GetAsync<NotificationList>($"/me/notifications?unreadOnly={unreadOnly}&limit={limit}");

    public Task<ApiResult> MarkNotificationReadAsync(int id) =>
        SendAsync(HttpMethod.Post, $"/me/notifications/{id}/read");

    public Task<ApiResult> MarkAllNotificationsReadAsync() =>
        SendAsync(HttpMethod.Post, "/me/notifications/read-all");

    // Moderation

    public Task<ApiResult> ReportPostAsync(int postId, ReportReason reason, string? details) =>
        SendAsync(HttpMethod.Post, $"/posts/{postId}/reports", new ReportRequest { Reason = reason, Details = details });

    public Task<ApiResult<ModerationSummary>> GetModerationSummaryAsync() =>
        GetAsync<ModerationSummary>("/moderation/summary");

    public Task<ApiResult<List<ModerationItem>>> GetModerationQueueAsync() =>
        GetAsync<List<ModerationItem>>("/moderation/queue");

    public Task<ApiResult<PostDto>> HidePostAsync(int postId) =>
        SendAsync<PostDto>(HttpMethod.Post, $"/moderation/posts/{postId}/hide");

    public Task<ApiResult<PostDto>> RestorePostAsync(int postId) =>
        SendAsync<PostDto>(HttpMethod.Post, $"/moderation/posts/{postId}/restore");

    /// <summary>
    /// A hidden post's image for an admin, as a data: URL. Its public URL is off, so this
    /// goes through HttpClient (with the bearer token) instead of a plain &lt;img src&gt;.
    /// </summary>
    public async Task<string?> GetModerationImageDataUrlAsync(int postId)
    {
        try
        {
            using var response = await http.GetAsync($"/moderation/posts/{postId}/image");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var type = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            return $"data:{type};base64,{Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync())}";
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Admin

    public Task<ApiResult<List<AdminUserDto>>> GetAdminUsersAsync(string? search) =>
        GetAsync<List<AdminUserDto>>("/admin/users"
            + (string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search.Trim())}"));

    public Task<ApiResult> PromoteToAdminAsync(Guid userId) =>
        SendAsync(HttpMethod.Put, $"/admin/users/{userId}/admin");

    public Task<ApiResult> RemoveAdminAsync(Guid userId) =>
        SendAsync(HttpMethod.Delete, $"/admin/users/{userId}/admin");

    // Plumbing

    private Task<ApiResult<T>> GetAsync<T>(string url) => SendAsync<T>(HttpMethod.Get, url);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        try
        {
            using var response = await http.SendAsync(CreateRequest(method, url, body));
            if (!response.IsSuccessStatusCode)
            {
                return await ApiResult<T>.FailureFromAsync(response);
            }

            return ApiResult<T>.Ok((await response.Content.ReadFromJsonAsync<T>(ApiJson.Options))!);
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.NetworkFailure();
        }
    }

    private async Task<ApiResult> SendAsync(HttpMethod method, string url, object? body = null)
    {
        try
        {
            using var response = await http.SendAsync(CreateRequest(method, url, body));
            return response.IsSuccessStatusCode ? ApiResult.Success : await ApiResult.FailureFromAsync(response);
        }
        catch (HttpRequestException)
        {
            return ApiResult.NetworkFailure();
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, object? body) => new(method, url)
    {
        Content = body switch
        {
            null => null,
            // Already encoded, e.g. a multipart upload.
            HttpContent content => content,
            _ => JsonContent.Create(body, body.GetType(), options: ApiJson.Options),
        },
    };
}

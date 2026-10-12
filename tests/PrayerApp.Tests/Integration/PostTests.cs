using System.Net;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class PostTests(ApiFactory factory)
{
    [Fact]
    public async Task Non_member_cannot_post_or_read_the_feed()
    {
        var communityId = await factory.CreateCommunityAsync();
        var outsider = await factory.RegisterAsync();

        var post = await outsider.Client.PostJsonAsync($"/communities/{communityId}/posts", NewPost("Hello"));
        var feed = await outsider.Client.GetAsync($"/communities/{communityId}/posts");

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, feed.StatusCode);
    }

    [Fact]
    public async Task Anonymous_post_hides_the_author_from_others_but_not_from_the_author()
    {
        var (communityId, author, reader) = await CommunityWithTwoMembersAsync();

        var created = await (await author.Client.PostJsonAsync(
            $"/communities/{communityId}/posts", NewPost("Please pray for my family", anonymous: true)))
            .ReadAsync<PostDto>();

        var asAuthor = await (await author.Client.GetAsync($"/posts/{created.Id}")).ReadAsync<PostDto>();
        var asReader = await (await reader.Client.GetAsync($"/posts/{created.Id}")).ReadAsync<PostDto>();

        Assert.Equal("Author", asAuthor.AuthorDisplayName);
        Assert.True(asAuthor.IsMine);
        Assert.Null(asReader.AuthorDisplayName);
        Assert.False(asReader.IsMine);
    }

    [Fact]
    public async Task Feed_pages_through_every_post_newest_first_without_duplicates()
    {
        var (communityId, author, reader) = await CommunityWithTwoMembersAsync();

        var createdIds = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            var post = await (await author.Client.PostJsonAsync(
                $"/communities/{communityId}/posts", NewPost($"Post {i}"))).ReadAsync<PostDto>();
            createdIds.Add(post.Id);
        }

        var seen = new List<int>();
        var pageSizes = new List<int>();
        string? cursor = null;
        do
        {
            var url = $"/communities/{communityId}/posts?limit=3" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await (await reader.Client.GetAsync(url)).ReadAsync<FeedPage>();
            seen.AddRange(page.Posts.Select(p => p.Id));
            pageSizes.Add(page.Posts.Count);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal([3, 3, 1], pageSizes);
        Assert.Equal(createdIds.AsEnumerable().Reverse(), seen);
    }

    [Fact]
    public async Task Author_controls_status_and_archived_posts_disappear_for_everyone_else()
    {
        var (communityId, author, reader) = await CommunityWithTwoMembersAsync();
        var post = await (await author.Client.PostJsonAsync(
            $"/communities/{communityId}/posts", NewPost("Job interview Friday"))).ReadAsync<PostDto>();
        var statusUrl = $"/posts/{post.Id}/status";

        // Only the author can change status; to anyone else the post "doesn't exist" for writes.
        var hijack = await reader.Client.PatchJsonAsync(statusUrl, new UpdatePostStatusRequest { Status = PostStatus.Archived });
        Assert.Equal(HttpStatusCode.NotFound, hijack.StatusCode);

        var answered = await author.Client.PatchJsonAsync(statusUrl, new UpdatePostStatusRequest { Status = PostStatus.Answered });
        Assert.Equal(PostStatus.Answered, (await answered.ReadAsync<PostDto>()).Status);

        var backwards = await author.Client.PatchJsonAsync(statusUrl, new UpdatePostStatusRequest { Status = PostStatus.Active });
        Assert.Equal(HttpStatusCode.Conflict, backwards.StatusCode);

        var archived = await author.Client.PatchJsonAsync(statusUrl, new UpdatePostStatusRequest { Status = PostStatus.Archived });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await reader.Client.GetAsync($"/posts/{post.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.Client.GetAsync($"/posts/{post.Id}")).StatusCode);
    }

    [Fact]
    public async Task Invalid_cursor_is_rejected()
    {
        var (communityId, _, reader) = await CommunityWithTwoMembersAsync();

        var response = await reader.Client.GetAsync($"/communities/{communityId}/posts?cursor=not-a-cursor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<(int CommunityId, TestUser Author, TestUser Reader)> CommunityWithTwoMembersAsync()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await factory.RegisterAsync("Author");
        var reader = await factory.RegisterAsync("Reader");

        await author.Client.PutAsync($"/communities/{communityId}/membership", null);
        await reader.Client.PutAsync($"/communities/{communityId}/membership", null);

        return (communityId, author, reader);
    }

    private static CreatePostRequest NewPost(string body, bool anonymous = false) =>
        new() { Body = body, IsAnonymous = anonymous };
}

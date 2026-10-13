using System.Net;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Moderation;
using PrayerApp.Shared.Contracts.Notifications;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class PostUpdateTests(ApiFactory factory)
{
    [Fact]
    public async Task Authors_post_updates_that_show_on_the_post_newest_first()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);

        await AddUpdateAsync(c.Author, postId, "First news");
        var second = await (await AddUpdateAsync(c.Author, postId, "  Better news  ")).ReadAsync<PostUpdateCreated>();

        Assert.Equal("Better news", second.Update.Body);

        var updates = await (await c.Member.Client.GetAsync($"/posts/{postId}/updates")).ReadAsync<List<PostUpdateDto>>();
        Assert.Equal(["Better news", "First news"], updates.Select(u => u.Body));

        var post = await (await c.Member.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal(2, post.UpdateCount);
        Assert.Equal("Better news", post.LatestUpdate?.Body);
    }

    [Fact]
    public async Task Everyone_who_responded_is_notified_and_no_one_else()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        var supporters = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => JoinedUserAsync(c.Id)));
        var withdrew = await JoinedUserAsync(c.Id);

        foreach (var supporter in supporters.Append(withdrew))
        {
            await Acknowledge(supporter, postId);
        }
        await withdrew.Client.DeleteAsync($"/posts/{postId}/acknowledgments/mine");

        var created = await (await AddUpdateAsync(c.Author, postId, "Home from the hospital")).ReadAsync<PostUpdateCreated>();

        Assert.Equal(3, created.Notified);
        foreach (var supporter in supporters)
        {
            var notice = Assert.Single(await UpdateNotificationsAsync(supporter));
            Assert.Equal(postId, notice.PostId);
            Assert.False(notice.IsRead);
        }

        // Not the one who withdrew, not a member who never responded, not the author.
        Assert.Empty(await UpdateNotificationsAsync(withdrew));
        Assert.Empty(await UpdateNotificationsAsync(c.Member));
        Assert.Empty(await UpdateNotificationsAsync(c.Author));
    }

    [Fact]
    public async Task Only_the_author_can_add_or_delete_and_outsiders_cannot_read()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        var updateId = (await (await AddUpdateAsync(c.Author, postId, "News")).ReadAsync<PostUpdateCreated>()).Update.Id;
        var outsider = await factory.RegisterAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await AddUpdateAsync(c.Member, postId, "Not mine")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Member.Client.DeleteAsync($"/posts/{postId}/updates/{updateId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/posts/{postId}/updates")).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_update_takes_its_notifications_with_it()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        await Acknowledge(c.Member, postId);
        var kept = (await (await AddUpdateAsync(c.Author, postId, "Kept")).ReadAsync<PostUpdateCreated>()).Update.Id;
        var removed = (await (await AddUpdateAsync(c.Author, postId, "Oops, too personal")).ReadAsync<PostUpdateCreated>()).Update.Id;

        Assert.Equal(HttpStatusCode.NoContent, (await c.Author.Client.DeleteAsync($"/posts/{postId}/updates/{removed}")).StatusCode);

        var updates = await (await c.Member.Client.GetAsync($"/posts/{postId}/updates")).ReadAsync<List<PostUpdateDto>>();
        Assert.Equal([kept], updates.Select(u => u.Id));
        Assert.Single(await UpdateNotificationsAsync(c.Member));
        Assert.Equal(HttpStatusCode.NotFound, (await c.Author.Client.DeleteAsync($"/posts/{postId}/updates/{removed}")).StatusCode);
    }

    [Fact]
    public async Task Archived_and_hidden_posts_cannot_get_updates()
    {
        var c = await CommunityAsync();
        var archived = await CreatePostAsync(c.Author, c.Id);
        var hidden = await CreatePostAsync(c.Author, c.Id);

        await c.Author.Client.PatchJsonAsync($"/posts/{archived}/status", new UpdatePostStatusRequest { Status = PostStatus.Archived });
        foreach (var reporter in await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => JoinedUserAsync(c.Id))))
        {
            await reporter.Client.PostJsonAsync($"/posts/{hidden}/reports", new ReportRequest { Reason = ReportReason.Spam });
        }

        Assert.Equal(HttpStatusCode.Conflict, (await AddUpdateAsync(c.Author, archived, "News")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AddUpdateAsync(c.Author, hidden, "News")).StatusCode);
    }

    [Fact]
    public async Task Updates_are_validated_and_capped()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await AddUpdateAsync(c.Author, postId, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await AddUpdateAsync(c.Author, postId, new string('a', PostUpdateRules.MaxLength + 1))).StatusCode);

        for (var i = 0; i < PostUpdateRules.MaxPerPost; i++)
        {
            (await AddUpdateAsync(c.Author, postId, $"Update {i}")).EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await AddUpdateAsync(c.Author, postId, "One too many")).StatusCode);
    }

    private sealed record TestCommunity(int Id, TestUser Author, TestUser Member);

    private async Task<TestCommunity> CommunityAsync()
    {
        var id = await factory.CreateCommunityAsync();
        return new TestCommunity(id, await JoinedUserAsync(id), await JoinedUserAsync(id));
    }

    private async Task<TestUser> JoinedUserAsync(int communityId)
    {
        var user = await factory.RegisterAsync();
        await user.Client.PutAsync($"/communities/{communityId}/membership", null);
        return user;
    }

    private static async Task<int> CreatePostAsync(TestUser author, int communityId)
    {
        var response = await author.Client.PostJsonAsync($"/communities/{communityId}/posts",
            new CreatePostRequest { Body = "Surgery on Monday" });
        return (await response.ReadAsync<PostDto>()).Id;
    }

    private static Task<HttpResponseMessage> AddUpdateAsync(TestUser author, int postId, string body) =>
        author.Client.PostJsonAsync($"/posts/{postId}/updates", new CreatePostUpdateRequest { Body = body });

    private static Task<HttpResponseMessage> Acknowledge(TestUser user, int postId) =>
        user.Client.PostJsonAsync($"/posts/{postId}/acknowledgments", new AcknowledgeRequest { Type = AcknowledgmentType.PrayingForYou });

    private static async Task<List<NotificationDto>> UpdateNotificationsAsync(TestUser user) =>
        (await (await user.Client.GetAsync("/me/notifications?limit=50")).ReadAsync<NotificationList>())
            .Items.Where(n => n.Type == NotificationType.PostUpdated).ToList();
}

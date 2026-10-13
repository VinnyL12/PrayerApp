using System.Net;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Notifications;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class NotificationTests(ApiFactory factory)
{
    [Fact]
    public async Task Acknowledgment_notifies_the_author_without_revealing_who()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();

        await Acknowledge(supporter, postId);
        var list = await WaitForNotificationsAsync(author, count: 1);

        var notification = Assert.Single(list.Items);
        Assert.Equal(NotificationType.PostAcknowledged, notification.Type);
        Assert.Equal(postId, notification.PostId);
        Assert.False(notification.IsRead);
        Assert.Equal(1, list.UnreadCount);

        // Privacy check on the raw JSON, not just the DTO: the supporter's id and name
        // must not appear anywhere in what the author receives.
        var raw = await (await author.Client.GetAsync("/me/notifications")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(supporter.User.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Supporter", raw);
    }

    [Fact]
    public async Task Many_acknowledgments_each_produce_a_notification()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId, "Author");
        var postId = await CreatePostAsync(author, communityId);
        var supporters = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => JoinedUserAsync(communityId, "Supporter")));

        await Task.WhenAll(supporters.Select(s => Acknowledge(s, postId)));

        var list = await WaitForNotificationsAsync(author, count: 12);
        Assert.Equal(12, list.UnreadCount);
    }

    [Fact]
    public async Task Marking_read_updates_the_unread_count_and_only_works_on_your_own()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();
        await Acknowledge(supporter, postId);
        var notification = (await WaitForNotificationsAsync(author, count: 1)).Items[0];

        var stranger = await factory.RegisterAsync();
        var hijack = await stranger.Client.PostAsync($"/me/notifications/{notification.Id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, hijack.StatusCode);

        var read = await author.Client.PostAsync($"/me/notifications/{notification.Id}/read", null);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);

        var after = await (await author.Client.GetAsync("/me/notifications")).ReadAsync<NotificationList>();
        Assert.Equal(0, after.UnreadCount);
        Assert.True(after.Items[0].IsRead);
    }

    [Fact]
    public async Task Read_all_clears_every_unread_notification()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId, "Author");
        var postId = await CreatePostAsync(author, communityId);
        foreach (var supporter in await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => JoinedUserAsync(communityId, "Supporter"))))
        {
            await Acknowledge(supporter, postId);
        }

        await WaitForNotificationsAsync(author, count: 3);
        await author.Client.PostAsync("/me/notifications/read-all", null);

        var unread = await (await author.Client.GetAsync("/me/notifications?unreadOnly=true")).ReadAsync<NotificationList>();
        Assert.Empty(unread.Items);
        Assert.Equal(0, unread.UnreadCount);
    }

    [Fact]
    public async Task Removing_a_response_removes_its_notification()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();
        await Acknowledge(supporter, postId);
        await WaitForNotificationsAsync(author, count: 1);

        await supporter.Client.DeleteAsync($"/posts/{postId}/acknowledgments/mine");

        var list = await (await author.Client.GetAsync("/me/notifications")).ReadAsync<NotificationList>();
        Assert.Empty(list.Items);
        Assert.Equal(0, list.UnreadCount);
    }

    [Fact]
    public async Task A_response_withdrawn_before_the_pipeline_runs_never_notifies()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();
        var witness = await JoinedUserAsync((await (await author.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>()).CommunityId, "Witness");

        await Acknowledge(supporter, postId);
        await supporter.Client.DeleteAsync($"/posts/{postId}/acknowledgments/mine");

        // The queue is FIFO with one consumer, so once the witness's notification exists,
        // the supporter's event has been handled too, whichever side of the delete it fell.
        await Acknowledge(witness, postId);
        await WaitForNotificationsAsync(author, count: 1);
    }

    [Fact]
    public async Task Responding_again_after_removing_notifies_once()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();
        var witness = await JoinedUserAsync((await (await author.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>()).CommunityId, "Witness");

        // Two events for the same (post, supporter) may reach the pipeline.
        await Acknowledge(supporter, postId);
        await supporter.Client.DeleteAsync($"/posts/{postId}/acknowledgments/mine");
        await Acknowledge(supporter, postId);

        await Acknowledge(witness, postId);
        await WaitForNotificationsAsync(author, count: 2);
    }

    /// <summary>
    /// The pipeline is asynchronous by design, so the test polls (with a deadline)
    /// instead of expecting the row the instant the acknowledgment returns.
    /// </summary>
    private static async Task<NotificationList> WaitForNotificationsAsync(TestUser user, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var list = await (await user.Client.GetAsync("/me/notifications?limit=50")).ReadAsync<NotificationList>();
            if (list.Items.Count >= count || DateTime.UtcNow > deadline)
            {
                Assert.Equal(count, list.Items.Count);
                return list;
            }

            await Task.Delay(50);
        }
    }

    private async Task<(TestUser Author, TestUser Supporter, int PostId)> PostWithSupporterAsync()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId, "Author");
        var supporter = await JoinedUserAsync(communityId, "Supporter");
        return (author, supporter, await CreatePostAsync(author, communityId));
    }

    private async Task<TestUser> JoinedUserAsync(int communityId, string displayName)
    {
        var user = await factory.RegisterAsync(displayName);
        await user.Client.PutAsync($"/communities/{communityId}/membership", null);
        return user;
    }

    private static async Task<int> CreatePostAsync(TestUser author, int communityId)
    {
        var response = await author.Client.PostJsonAsync(
            $"/communities/{communityId}/posts", new CreatePostRequest { Body = "Please keep my dad in your prayers" });
        return (await response.ReadAsync<PostDto>()).Id;
    }

    private static Task<HttpResponseMessage> Acknowledge(TestUser user, int postId) =>
        user.Client.PostJsonAsync($"/posts/{postId}/acknowledgments", new AcknowledgeRequest { Type = AcknowledgmentType.PrayingForYou });
}

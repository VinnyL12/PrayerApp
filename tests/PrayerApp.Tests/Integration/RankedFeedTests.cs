using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class RankedFeedTests(ApiFactory factory)
{
    [Fact]
    public async Task Supported_and_urgent_posts_rise_above_newer_quiet_ones()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId);

        var supported = await CreatePostAsync(author, communityId, "Supported");
        var urgent = await CreatePostAsync(author, communityId, "Urgent", urgent: true);
        var quiet = await CreatePostAsync(author, communityId, "Quiet, but newest");

        foreach (var supporter in await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => JoinedUserAsync(communityId))))
        {
            await supporter.Client.PostJsonAsync($"/posts/{supported}/acknowledgments",
                new AcknowledgeRequest { Type = AcknowledgmentType.PrayingForYou });
        }

        var feed = await (await author.Client.GetAsync($"/communities/{communityId}/posts")).ReadAsync<FeedPage>();

        // 5 recent acks > urgency head start of 3 > nothing; all are minutes old.
        Assert.Equal([supported, urgent, quiet], feed.Posts.Select(p => p.Id));
    }

    [Fact]
    public async Task Pages_stay_consistent_when_scores_change_mid_scroll()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId);
        var supporter = await JoinedUserAsync(communityId);

        var postIds = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            postIds.Add(await CreatePostAsync(author, communityId, $"Post {i}"));
        }

        var page1 = await (await supporter.Client.GetAsync($"/communities/{communityId}/posts?limit=3")).ReadAsync<FeedPage>();

        // Between page loads, the lowest-ranked post gets a burst of support. Ranked "now",
        // it would jump to page 1, which this reader has already seen, and be missed.
        var lastPlace = postIds[0];
        await supporter.Client.PostJsonAsync($"/posts/{lastPlace}/acknowledgments",
            new AcknowledgeRequest { Type = AcknowledgmentType.SendingStrength });

        var page2 = await (await supporter.Client.GetAsync(
            $"/communities/{communityId}/posts?limit=3&cursor={page1.NextCursor}")).ReadAsync<FeedPage>();

        var seen = page1.Posts.Concat(page2.Posts).Select(p => p.Id).ToList();
        Assert.Equal(6, seen.Count);
        Assert.Equal(postIds.Order(), seen.Order());
        Assert.Null(page2.NextCursor);

        // A fresh feed load (new cursor session) does reflect the new support.
        var fresh = await (await supporter.Client.GetAsync($"/communities/{communityId}/posts?limit=3")).ReadAsync<FeedPage>();
        Assert.Equal(lastPlace, fresh.Posts[0].Id);
    }

    [Fact]
    public async Task Posts_created_after_the_first_page_wait_for_the_next_refresh()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId);
        for (var i = 0; i < 4; i++)
        {
            await CreatePostAsync(author, communityId, $"Post {i}");
        }

        var page1 = await (await author.Client.GetAsync($"/communities/{communityId}/posts?limit=2")).ReadAsync<FeedPage>();
        var late = await CreatePostAsync(author, communityId, "Arrived mid-scroll");
        var page2 = await (await author.Client.GetAsync(
            $"/communities/{communityId}/posts?limit=2&cursor={page1.NextCursor}")).ReadAsync<FeedPage>();

        Assert.DoesNotContain(late, page1.Posts.Concat(page2.Posts).Select(p => p.Id));
    }

    [Fact]
    public async Task Answered_posts_stay_in_the_feed_and_archived_ones_leave()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId);
        var answered = await CreatePostAsync(author, communityId, "Answered");
        var archived = await CreatePostAsync(author, communityId, "Archived");
        var active = await CreatePostAsync(author, communityId, "Active");

        await author.Client.PatchJsonAsync($"/posts/{answered}/status", new UpdatePostStatusRequest { Status = PostStatus.Answered });
        await author.Client.PatchJsonAsync($"/posts/{archived}/status", new UpdatePostStatusRequest { Status = PostStatus.Archived });

        var feed = await (await author.Client.GetAsync($"/communities/{communityId}/posts")).ReadAsync<FeedPage>();

        Assert.Equal([active, answered], feed.Posts.Select(p => p.Id));
    }

    private async Task<TestUser> JoinedUserAsync(int communityId)
    {
        var user = await factory.RegisterAsync();
        await user.Client.PutAsync($"/communities/{communityId}/membership", null);
        return user;
    }

    private static async Task<int> CreatePostAsync(TestUser author, int communityId, string body, bool urgent = false)
    {
        var response = await author.Client.PostJsonAsync(
            $"/communities/{communityId}/posts", new CreatePostRequest { Body = body, IsUrgent = urgent });
        return (await response.ReadAsync<PostDto>()).Id;
    }
}

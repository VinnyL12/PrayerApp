using System.Net;
using System.Net.Http.Headers;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Moderation;
using PrayerApp.Shared.Contracts.Notifications;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class ModerationTests(ApiFactory factory)
{
    // Matches ModerationOptions.DefaultAutoHideThreshold.
    private const int Threshold = 3;

    [Fact]
    public async Task Reporting_hides_the_post_from_the_reporter_only()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        var reporter = await JoinedUserAsync(c.Id);
        var bystander = await JoinedUserAsync(c.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await ReportAsync(reporter, postId)).StatusCode);

        Assert.DoesNotContain(postId, await FeedIdsAsync(reporter, c.Id));
        Assert.Contains(postId, await FeedIdsAsync(bystander, c.Id));

        var post = await (await reporter.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.True(post.ReportedByMe);
        Assert.Equal(ModerationStatus.Visible, post.ModerationStatus);
    }

    [Fact]
    public async Task Reports_follow_the_same_rules_as_responses()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        var reporter = await JoinedUserAsync(c.Id);
        var outsider = await factory.RegisterAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await ReportAsync(c.Author, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReportAsync(outsider, postId)).StatusCode);

        // A double tap, times ten: exactly one report counts.
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ReportAsync(reporter, postId)));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var unknown = await reporter.Client.PostAsync($"/posts/{postId}/reports",
            new StringContent("""{"reason":"Boring"}""", MediaTypeHeaderValue.Parse("application/json")));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Enough_reports_put_a_post_under_review_for_everyone_but_its_author()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        var bystander = await JoinedUserAsync(c.Id);
        var reporters = await Task.WhenAll(Enumerable.Range(0, Threshold).Select(_ => JoinedUserAsync(c.Id)));

        // One short of the threshold: still visible.
        foreach (var reporter in reporters[..^1])
        {
            await ReportAsync(reporter, postId);
        }
        Assert.Contains(postId, await FeedIdsAsync(bystander, c.Id));

        await ReportAsync(reporters[^1], postId);

        Assert.DoesNotContain(postId, await FeedIdsAsync(bystander, c.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await bystander.Client.GetAsync($"/posts/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bystander.Client.PostJsonAsync($"/posts/{postId}/acknowledgments",
            new AcknowledgeRequest { Type = AcknowledgmentType.PrayingForYou })).StatusCode);

        // The author still sees it, labelled.
        var own = await (await c.Author.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal(ModerationStatus.UnderReview, own.ModerationStatus);
    }

    [Fact]
    public async Task Simultaneous_reports_still_cross_the_threshold()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        var reporters = await Task.WhenAll(Enumerable.Range(0, Threshold + 2).Select(_ => JoinedUserAsync(c.Id)));

        // If the threshold check ran inside each report's transaction, concurrent reports
        // could each count only themselves (or deadlock), and the post would stay up.
        var responses = await Task.WhenAll(reporters.Select(r => ReportAsync(r, postId)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        var own = await (await c.Author.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal(ModerationStatus.UnderReview, own.ModerationStatus);
    }

    [Fact]
    public async Task Hidden_posts_turn_their_image_url_off()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        var file = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var imageId = (await (await c.Author.Client.PutAsync($"/posts/{postId}/image",
            new MultipartFormDataContent { { file, "image", "photo.jpg" } })).ReadAsync<PostDto>()).ImageId;

        (await c.Moderator.Client.PostAsync($"/moderation/posts/{postId}/hide", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateApiClient().GetAsync($"/images/{imageId}")).StatusCode);

        // Admins still see it, through their own authenticated route. Nobody else can.
        var forAdmin = await c.Moderator.Client.GetAsync($"/moderation/posts/{postId}/image");
        Assert.Equal(HttpStatusCode.OK, forAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Author.Client.GetAsync($"/moderation/posts/{postId}/image")).StatusCode);
    }

    [Fact]
    public async Task The_queue_shows_reasons_and_notes_but_never_who_reported()
    {
        var c = await CommunityAsync();
        var quiet = await CreatePostAsync(c.Author, c.Id);
        var loud = await CreatePostAsync(c.Author, c.Id, anonymous: true);
        var a = await JoinedUserAsync(c.Id, "Reporter Alpha");
        var b = await JoinedUserAsync(c.Id, "Reporter Beta");

        await ReportAsync(a, quiet, ReportReason.Spam);
        await ReportAsync(a, loud, ReportReason.Scam, "Asking for money");
        await ReportAsync(b, loud, ReportReason.Scam);

        var queue = await QueueAsync(c.Moderator, c.Id);

        // Most-reported first.
        Assert.Equal([loud, quiet], queue.Select(i => i.Post.Id));
        var top = queue[0];
        Assert.Equal(2, top.OpenReports);
        Assert.Equal(2, top.ByReason[ReportReason.Scam]);
        Assert.Equal(["Asking for money"], top.Details);

        // Anonymous stays anonymous to moderators, and no reporter appears anywhere.
        Assert.Null(top.Post.AuthorDisplayName);
        var raw = await (await c.Moderator.Client.GetAsync("/moderation/queue")).Content.ReadAsStringAsync();
        foreach (var reporter in new[] { a, b })
        {
            Assert.DoesNotContain(reporter.User.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(reporter.User.DisplayName, raw);
        }

        var summary = await (await c.Moderator.Client.GetAsync("/moderation/summary")).ReadAsync<ModerationSummary>();
        // At least these two; the queue spans every community in the shared test database.
        Assert.True(summary.OpenCount >= 2);
    }

    [Fact]
    public async Task Only_admins_can_moderate()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        await ReportAsync(await JoinedUserAsync(c.Id), postId);
        var member = await JoinedUserAsync(c.Id);

        foreach (var url in new[] { "/moderation/summary", "/moderation/queue", $"/moderation/posts/{postId}/image" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.GetAsync(url)).StatusCode);
        }

        foreach (var url in new[] { $"/moderation/posts/{postId}/hide", $"/moderation/posts/{postId}/restore" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.PostAsync(url, null)).StatusCode);
        }

        // An admin moderates every community, including ones they never joined.
        var outsideAdmin = await factory.RegisterAsync("Admin elsewhere");
        await factory.MakeAdminAsync(outsideAdmin.User.Id);
        Assert.Contains(await QueueAsync(outsideAdmin, c.Id), i => i.Post.Id == postId);
    }

    [Fact]
    public async Task Hiding_resolves_the_reports_and_tells_the_author()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        await ReportAsync(await JoinedUserAsync(c.Id), postId);

        var hidden = await (await c.Moderator.Client.PostAsync($"/moderation/posts/{postId}/hide", null)).ReadAsync<PostDto>();

        Assert.Equal(ModerationStatus.Hidden, hidden.ModerationStatus);
        Assert.Empty(await QueueAsync(c.Moderator, c.Id));

        var notifications = await (await c.Author.Client.GetAsync("/me/notifications")).ReadAsync<NotificationList>();
        var notice = Assert.Single(notifications.Items);
        Assert.Equal(NotificationType.PostHidden, notice.Type);
        Assert.Equal(postId, notice.PostId);

        // No hint of which moderator.
        var raw = await (await c.Author.Client.GetAsync("/me/notifications")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(c.Moderator.User.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Keeping_a_post_restores_it_and_needs_fresh_reports_to_hide_it_again()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);
        var bystander = await JoinedUserAsync(c.Id);
        foreach (var reporter in await Task.WhenAll(Enumerable.Range(0, Threshold).Select(_ => JoinedUserAsync(c.Id))))
        {
            await ReportAsync(reporter, postId);
        }

        var kept = await (await c.Moderator.Client.PostAsync($"/moderation/posts/{postId}/restore", null)).ReadAsync<PostDto>();

        Assert.Equal(ModerationStatus.Visible, kept.ModerationStatus);
        Assert.Contains(postId, await FeedIdsAsync(bystander, c.Id));
        Assert.Empty(await QueueAsync(c.Moderator, c.Id));

        // The dismissed reports no longer count: one new report doesn't re-hide it.
        await ReportAsync(await JoinedUserAsync(c.Id), postId);
        Assert.Contains(postId, await FeedIdsAsync(bystander, c.Id));
    }

    [Fact]
    public async Task Admins_can_hide_unreported_posts_and_undo_it()
    {
        var c = await CommunityAsync();
        var postId = await CreatePostAsync(c.Author, c.Id);

        await c.Moderator.Client.PostAsync($"/moderation/posts/{postId}/hide", null);
        Assert.DoesNotContain(postId, await FeedIdsAsync(c.Moderator, c.Id));

        await c.Moderator.Client.PostAsync($"/moderation/posts/{postId}/restore", null);
        Assert.Contains(postId, await FeedIdsAsync(c.Moderator, c.Id));

        // The "your post was hidden" notice went away with the hide.
        var notifications = await (await c.Author.Client.GetAsync("/me/notifications")).ReadAsync<NotificationList>();
        Assert.Empty(notifications.Items);
    }

    /// <param name="Moderator">An admin, and a member, so it can also read the community's feed.</param>
    private sealed record TestCommunity(int Id, TestUser Author, TestUser Moderator);

    private async Task<TestCommunity> CommunityAsync()
    {
        var id = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(id, "Author");
        var moderator = await JoinedUserAsync(id, "Moderator");
        await factory.MakeAdminAsync(moderator.User.Id);
        return new TestCommunity(id, author, moderator);
    }

    private async Task<TestUser> JoinedUserAsync(int communityId, string displayName = "Member")
    {
        var user = await factory.RegisterAsync(displayName);
        await user.Client.PutAsync($"/communities/{communityId}/membership", null);
        return user;
    }

    private static async Task<int> CreatePostAsync(TestUser author, int communityId, bool anonymous = false)
    {
        var response = await author.Client.PostJsonAsync($"/communities/{communityId}/posts",
            new CreatePostRequest { Body = "Please pray for us", IsAnonymous = anonymous });
        return (await response.ReadAsync<PostDto>()).Id;
    }

    private static Task<HttpResponseMessage> ReportAsync(
        TestUser user, int postId, ReportReason reason = ReportReason.Spam, string? details = null) =>
        user.Client.PostJsonAsync($"/posts/{postId}/reports", new ReportRequest { Reason = reason, Details = details });

    private static async Task<List<int>> FeedIdsAsync(TestUser user, int communityId) =>
        (await (await user.Client.GetAsync($"/communities/{communityId}/posts")).ReadAsync<FeedPage>())
            .Posts.Select(p => p.Id).ToList();

    /// <summary>The caller's queue, optionally narrowed to one community.</summary>
    private static async Task<List<ModerationItem>> QueueAsync(TestUser moderator, int? communityId = null)
    {
        var queue = await (await moderator.Client.GetAsync("/moderation/queue")).ReadAsync<List<ModerationItem>>();
        return queue.Where(i => communityId is null || i.Post.CommunityId == communityId).ToList();
    }
}

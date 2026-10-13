using System.Net;
using System.Net.Http.Json;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class AcknowledgmentTests(ApiFactory factory)
{
    [Fact]
    public async Task Acknowledging_increments_the_count_and_a_second_attempt_conflicts()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var supporter = await JoinedUserAsync(communityId);

        var first = await Acknowledge(supporter, postId, AcknowledgmentType.SendingStrength);
        var second = await Acknowledge(supporter, postId, AcknowledgmentType.SendingLove);

        Assert.Equal(1, (await first.ReadAsync<AcknowledgmentResult>()).AcknowledgmentCount);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var post = await (await supporter.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal(1, post.AcknowledgmentCount);
        Assert.Equal(AcknowledgmentType.SendingStrength, post.MyAcknowledgment);
    }

    [Fact]
    public async Task Authors_cannot_acknowledge_their_own_post()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);

        var response = await Acknowledge(author, postId, AcknowledgmentType.PrayingForYou);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Non_members_cannot_see_or_acknowledge_a_post()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var outsider = await factory.RegisterAsync();

        var response = await Acknowledge(outsider, postId, AcknowledgmentType.PrayingForYou);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Simultaneous_duplicate_requests_from_one_user_produce_exactly_one_acknowledgment()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var supporter = await JoinedUserAsync(communityId);

        // A double-tap, times ten. Every request passes the "already acknowledged?"
        // reasoning at the same moment; only the unique index can stop the extras.
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Acknowledge(supporter, postId, AcknowledgmentType.PrayingForYou)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var post = await (await author.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal(1, post.AcknowledgmentCount);
    }

    [Fact]
    public async Task Simultaneous_acknowledgments_from_many_users_are_all_counted()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var supporters = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => JoinedUserAsync(communityId)));

        // With a read-modify-write increment in C#, some of these would be lost updates.
        var responses = await Task.WhenAll(supporters
            .Select(s => Acknowledge(s, postId, AcknowledgmentType.SendingLove)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var summary = await (await author.Client.GetAsync($"/posts/{postId}/acknowledgments/summary"))
            .ReadAsync<AcknowledgmentSummary>();
        var post = await (await author.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();

        // The stored counter matches the actual rows.
        Assert.Equal(15, post.AcknowledgmentCount);
        Assert.Equal(15, summary.Total);
    }

    [Fact]
    public async Task Summary_counts_by_type_and_is_visible_only_to_the_author()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var a = await JoinedUserAsync(communityId);
        var b = await JoinedUserAsync(communityId);
        var c = await JoinedUserAsync(communityId);

        await Acknowledge(a, postId, AcknowledgmentType.PrayingForYou);
        await Acknowledge(b, postId, AcknowledgmentType.PrayingForYou);
        await Acknowledge(c, postId, AcknowledgmentType.YoureNotAlone);

        var summaryUrl = $"/posts/{postId}/acknowledgments/summary";
        var summary = await (await author.Client.GetAsync(summaryUrl)).ReadAsync<AcknowledgmentSummary>();

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.ByType[AcknowledgmentType.PrayingForYou]);
        Assert.Equal(1, summary.ByType[AcknowledgmentType.YoureNotAlone]);
        Assert.Equal(0, summary.ByType[AcknowledgmentType.SendingLove]);

        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync(summaryUrl)).StatusCode);
    }

    [Fact]
    public async Task Changing_a_response_swaps_its_type_without_changing_the_count()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var supporter = await JoinedUserAsync(communityId);
        await Acknowledge(supporter, postId, AcknowledgmentType.PrayingForYou);

        var changed = await Change(supporter, postId, AcknowledgmentType.SendingLove);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(1, (await changed.ReadAsync<AcknowledgmentResult>()).AcknowledgmentCount);

        var post = await (await supporter.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal(AcknowledgmentType.SendingLove, post.MyAcknowledgment);

        var summary = await (await author.Client.GetAsync($"/posts/{postId}/acknowledgments/summary"))
            .ReadAsync<AcknowledgmentSummary>();
        Assert.Equal(1, summary.ByType[AcknowledgmentType.SendingLove]);
        Assert.Equal(0, summary.ByType[AcknowledgmentType.PrayingForYou]);
    }

    [Fact]
    public async Task Changing_follows_the_same_rules_as_responding()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var supporter = await JoinedUserAsync(communityId);

        // Nothing to change yet.
        Assert.Equal(HttpStatusCode.NotFound, (await Change(supporter, postId, AcknowledgmentType.SendingLove)).StatusCode);

        await Acknowledge(supporter, postId, AcknowledgmentType.PrayingForYou);

        // Celebration doesn't fit an active post in a general community.
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(supporter, postId, AcknowledgmentType.CelebratingWithYou)).StatusCode);

        // Once it's answered, it does.
        await SetStatus(author, postId, PostStatus.Answered);
        Assert.Equal(HttpStatusCode.OK, (await Change(supporter, postId, AcknowledgmentType.CelebratingWithYou)).StatusCode);

        // Archived posts are frozen: hidden from everyone but the author, so 404, not 409.
        await SetStatus(author, postId, PostStatus.Archived);
        Assert.Equal(HttpStatusCode.NotFound, (await Change(supporter, postId, AcknowledgmentType.SendingLove)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Remove(supporter, postId)).StatusCode);
    }

    [Fact]
    public async Task Removing_a_response_decrements_the_count_and_allows_responding_again()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var supporter = await JoinedUserAsync(communityId);
        await Acknowledge(supporter, postId, AcknowledgmentType.PrayingForYou);

        var removed = await Remove(supporter, postId);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(0, (await removed.ReadAsync<AcknowledgmentRemovedResult>()).AcknowledgmentCount);

        var post = await (await supporter.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Null(post.MyAcknowledgment);
        Assert.Equal(0, post.AcknowledgmentCount);

        Assert.Equal(HttpStatusCode.NotFound, (await Remove(supporter, postId)).StatusCode);

        var again = await Acknowledge(supporter, postId, AcknowledgmentType.SendingStrength);
        Assert.Equal(1, (await again.ReadAsync<AcknowledgmentResult>()).AcknowledgmentCount);
    }

    [Fact]
    public async Task Simultaneous_removes_from_one_user_decrement_exactly_once()
    {
        var (communityId, author) = await CommunityWithAuthorAsync();
        var postId = await CreatePostAsync(author, communityId);
        var supporter = await JoinedUserAsync(communityId);
        var other = await JoinedUserAsync(communityId);
        await Acknowledge(supporter, postId, AcknowledgmentType.PrayingForYou);
        await Acknowledge(other, postId, AcknowledgmentType.PrayingForYou);

        // A decrement based on an earlier "do they have one?" read would run ten times
        // here and drive the count negative. Only the DELETE that removed a row counts.
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Remove(supporter, postId)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.NotFound));

        var post = await (await author.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal(1, post.AcknowledgmentCount);
    }

    private async Task<(int CommunityId, TestUser Author)> CommunityWithAuthorAsync()
    {
        var communityId = await factory.CreateCommunityAsync();
        return (communityId, await JoinedUserAsync(communityId));
    }

    private async Task<TestUser> JoinedUserAsync(int communityId)
    {
        var user = await factory.RegisterAsync();
        await user.Client.PutAsync($"/communities/{communityId}/membership", null);
        return user;
    }

    private static async Task<int> CreatePostAsync(TestUser author, int communityId)
    {
        var response = await author.Client.PostJsonAsync(
            $"/communities/{communityId}/posts", new CreatePostRequest { Body = "Surgery on Monday" });
        return (await response.ReadAsync<PostDto>()).Id;
    }

    private static Task<HttpResponseMessage> Acknowledge(TestUser user, int postId, AcknowledgmentType type) =>
        user.Client.PostJsonAsync($"/posts/{postId}/acknowledgments", new AcknowledgeRequest { Type = type });

    private static Task<HttpResponseMessage> Change(TestUser user, int postId, AcknowledgmentType type) =>
        user.Client.PutAsJsonAsync($"/posts/{postId}/acknowledgments/mine", new AcknowledgeRequest { Type = type }, TestUsers.Json);

    private static Task<HttpResponseMessage> Remove(TestUser user, int postId) =>
        user.Client.DeleteAsync($"/posts/{postId}/acknowledgments/mine");

    private static async Task SetStatus(TestUser author, int postId, PostStatus status) =>
        (await author.Client.PatchJsonAsync($"/posts/{postId}/status", new UpdatePostStatusRequest { Status = status }))
            .EnsureSuccessStatusCode();
}

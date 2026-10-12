using System.Net;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class AcknowledgmentPolicyEnforcementTests(ApiFactory factory)
{
    [Fact]
    public async Task Celebration_is_rejected_on_a_grief_post()
    {
        var (supporter, postId) = await PostInAsync(CommunityCategory.Grief);

        var response = await Acknowledge(supporter, postId, AcknowledgmentType.CelebratingWithYou);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Gratitude_posts_take_celebration_and_reject_support()
    {
        var (supporter, postId) = await PostInAsync(CommunityCategory.Gratitude);

        Assert.Equal(HttpStatusCode.BadRequest, (await Acknowledge(supporter, postId, AcknowledgmentType.PrayingForYou)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Acknowledge(supporter, postId, AcknowledgmentType.RejoicingWithYou)).StatusCode);
    }

    [Fact]
    public async Task Posts_report_their_category_so_clients_can_offer_the_right_responses()
    {
        var (supporter, postId) = await PostInAsync(CommunityCategory.Gratitude);

        var post = await (await supporter.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();

        Assert.Equal(CommunityCategory.Gratitude, post.CommunityCategory);
    }

    private async Task<(TestUser Supporter, int PostId)> PostInAsync(CommunityCategory category)
    {
        var communityId = await factory.CreateCommunityAsync(category);
        var author = await factory.RegisterAsync();
        var supporter = await factory.RegisterAsync();
        await author.Client.PutAsync($"/communities/{communityId}/membership", null);
        await supporter.Client.PutAsync($"/communities/{communityId}/membership", null);

        var post = await (await author.Client.PostJsonAsync(
            $"/communities/{communityId}/posts", new CreatePostRequest { Body = "Test post" })).ReadAsync<PostDto>();

        return (supporter, post.Id);
    }

    private static Task<HttpResponseMessage> Acknowledge(TestUser user, int postId, AcknowledgmentType type) =>
        user.Client.PostJsonAsync($"/posts/{postId}/acknowledgments", new AcknowledgeRequest { Type = type });
}

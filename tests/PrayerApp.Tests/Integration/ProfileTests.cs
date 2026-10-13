using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Contracts.Profile;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class ProfileTests(ApiFactory factory)
{
    [Fact]
    public async Task Profile_counts_your_own_activity()
    {
        var communityId = await factory.CreateCommunityAsync();
        var me = await JoinedUserAsync(communityId, "Jordan");
        var other = await JoinedUserAsync(communityId, "Sam");
        var third = await JoinedUserAsync(communityId, "Riley");

        var mine = await CreatePostAsync(me, communityId);
        await CreatePostAsync(me, communityId);
        var theirs = await CreatePostAsync(other, communityId);

        await Acknowledge(other, mine);
        await Acknowledge(third, mine);
        await Acknowledge(me, theirs);

        var profile = await (await me.Client.GetAsync("/me/profile")).ReadAsync<ProfileDto>();

        Assert.Equal("Jordan", profile.DisplayName);
        Assert.Equal(me.User.Email, profile.Email);
        Assert.False(profile.IsAdmin);
        Assert.Equal(2, profile.Posts);
        Assert.Equal(1, profile.ResponsesGiven);
        Assert.Equal(2, profile.ResponsesReceived);
        Assert.Equal(1, profile.Communities);
    }

    [Fact]
    public async Task A_new_account_has_an_empty_profile_and_admins_are_marked()
    {
        var user = await factory.RegisterAsync();
        await factory.MakeAdminAsync(user.User.Id);

        var profile = await (await user.Client.GetAsync("/me/profile")).ReadAsync<ProfileDto>();

        Assert.True(profile.IsAdmin);
        Assert.False(profile.IsOwner);
        Assert.Equal(0, profile.Posts);
        Assert.Equal(0, profile.ResponsesReceived);
    }

    private async Task<TestUser> JoinedUserAsync(int communityId, string name)
    {
        var user = await factory.RegisterAsync(name);
        await user.Client.PutAsync($"/communities/{communityId}/membership", null);
        return user;
    }

    private static async Task<int> CreatePostAsync(TestUser author, int communityId)
    {
        var response = await author.Client.PostJsonAsync($"/communities/{communityId}/posts", new CreatePostRequest { Body = "Please pray" });
        return (await response.ReadAsync<PostDto>()).Id;
    }

    private static Task<HttpResponseMessage> Acknowledge(TestUser user, int postId) =>
        user.Client.PostJsonAsync($"/posts/{postId}/acknowledgments", new AcknowledgeRequest { Type = AcknowledgmentType.PrayingForYou });
}

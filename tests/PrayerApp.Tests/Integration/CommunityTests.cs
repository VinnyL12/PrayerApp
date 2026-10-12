using System.Net;
using PrayerApp.Shared.Contracts.Communities;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class CommunityTests(ApiFactory factory)
{
    [Fact]
    public async Task List_includes_the_seeded_communities()
    {
        var alice = await factory.RegisterAsync();

        var communities = await (await alice.Client.GetAsync("/communities")).ReadAsync<List<CommunityDto>>();

        Assert.Contains(communities, c => c.Name == "General");
        Assert.Contains(communities, c => c.Name == "Gratitude & Answered Prayers");
        Assert.All(communities, c => Assert.False(c.IsMember));
    }

    [Fact]
    public async Task Join_is_idempotent_and_leave_removes_membership()
    {
        var communityId = await factory.CreateCommunityAsync();
        var alice = await factory.RegisterAsync();
        var url = $"/communities/{communityId}/membership";

        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsync(url, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.PutAsync(url, null)).StatusCode);

        var joined = await (await alice.Client.GetAsync($"/communities/{communityId}")).ReadAsync<CommunityDto>();
        Assert.True(joined.IsMember);
        Assert.Equal(1, joined.MemberCount);

        Assert.Equal(HttpStatusCode.NoContent, (await alice.Client.DeleteAsync(url)).StatusCode);

        var left = await (await alice.Client.GetAsync($"/communities/{communityId}")).ReadAsync<CommunityDto>();
        Assert.False(left.IsMember);
        Assert.Equal(0, left.MemberCount);
    }

    [Fact]
    public async Task Joining_an_unknown_community_is_not_found()
    {
        var alice = await factory.RegisterAsync();

        var response = await alice.Client.PutAsync("/communities/999999/membership", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

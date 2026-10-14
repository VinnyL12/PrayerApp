using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Moderation;
using PrayerApp.Shared.Contracts.Notifications;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

/// <summary>
/// The SignalR hub, over a real WebSocket through the in-memory test server, with the
/// token in the query string exactly as the browser sends it.
/// </summary>
[Collection("Api")]
public class LiveUpdatesTests(ApiFactory factory)
{
    // Long enough for the background notification writer; a pass is usually milliseconds.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    // How long to wait before concluding a hint is NOT coming.
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Connecting_requires_a_valid_token()
    {
        await using var anonymous = Connect(token: null);
        await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());

        await using var forged = Connect(token: "not-a-jwt");
        await Assert.ThrowsAnyAsync<Exception>(() => forged.StartAsync());
    }

    [Fact]
    public async Task A_response_sends_the_author_a_hint_and_nobody_else()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();
        await using var authorLive = await ProbeAsync(author);
        await using var supporterLive = await ProbeAsync(supporter);

        await Acknowledge(supporter, postId);

        await authorLive.WaitForAsync(LiveUpdateEvents.NotificationsChanged);
        await supporterLive.AssertNothingAsync();

        // The hint is a cue to refetch, and by the time it arrives the row is there to fetch.
        var list = await (await author.Client.GetAsync("/me/notifications")).ReadAsync<NotificationList>();
        Assert.Equal(1, list.UnreadCount);
    }

    [Fact]
    public async Task Withdrawing_a_response_tells_the_author_their_count_changed()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();
        await using var authorLive = await ProbeAsync(author);

        await Acknowledge(supporter, postId);
        await authorLive.WaitForAsync(LiveUpdateEvents.NotificationsChanged);

        await supporter.Client.DeleteAsync($"/posts/{postId}/acknowledgments/mine");
        await authorLive.WaitForAsync(LiveUpdateEvents.NotificationsChanged);
    }

    [Fact]
    public async Task A_post_update_hints_every_supporter_from_one_insert()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId, "Author");
        var postId = await CreatePostAsync(author, communityId);
        var supporters = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => JoinedUserAsync(communityId, "Supporter")));
        foreach (var supporter in supporters)
        {
            await Acknowledge(supporter, postId);
        }

        var probes = await Task.WhenAll(supporters.Select(ProbeAsync));

        var created = await (await author.Client.PostJsonAsync($"/posts/{postId}/updates",
            new CreatePostUpdateRequest { Body = "Surgery went well, thank you all." })).ReadAsync<PostUpdateCreated>();

        Assert.Equal(3, created.Notified);
        await Task.WhenAll(probes.Select(p => p.WaitForAsync(LiveUpdateEvents.NotificationsChanged)));

        foreach (var probe in probes)
        {
            await probe.DisposeAsync();
        }
    }

    [Fact]
    public async Task Reports_reach_admins_only()
    {
        var (author, reporter, postId) = await PostWithSupporterAsync();
        var admin = await factory.RegisterAsync("Admin");
        await factory.MakeAdminAsync(admin.User.Id);

        await using var adminLive = await ProbeAsync(admin);
        await using var memberLive = await ProbeAsync(author);

        await reporter.Client.PostJsonAsync($"/posts/{postId}/reports", new ReportRequest { Reason = ReportReason.Spam });

        await adminLive.WaitForAsync(LiveUpdateEvents.ModerationChanged);
        await memberLive.AssertNothingAsync();
    }

    [Fact]
    public async Task Hiding_a_post_tells_its_author_and_admins()
    {
        var (author, _, postId) = await PostWithSupporterAsync();
        var admin = await factory.RegisterAsync("Admin");
        await factory.MakeAdminAsync(admin.User.Id);

        await using var adminLive = await ProbeAsync(admin);
        await using var authorLive = await ProbeAsync(author);

        await admin.Client.PostAsync($"/moderation/posts/{postId}/hide", null);

        await authorLive.WaitForAsync(LiveUpdateEvents.NotificationsChanged);
        await adminLive.WaitForAsync(LiveUpdateEvents.ModerationChanged);
    }

    // --- Helpers -----------------------------------------------------------------------

    private HubConnection Connect(string? token)
    {
        var url = $"https://localhost{LiveUpdateEvents.HubPath}";
        if (token is not null)
        {
            url += $"?access_token={Uri.EscapeDataString(token)}";
        }

        return new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                // Straight to WebSockets over the test server's in-memory socket, so the
                // token travels in the URL as it does from a browser.
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, ct) =>
                    await factory.Server.CreateWebSocketClient().ConnectAsync(context.Uri, ct);
            })
            .Build();
    }

    private async Task<HubProbe> ProbeAsync(TestUser user)
    {
        var connection = Connect(user.Client.DefaultRequestHeaders.Authorization!.Parameter);
        var probe = new HubProbe(connection);
        await connection.StartAsync();
        return probe;
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

    /// <summary>Records every hint a connection receives, in arrival order.</summary>
    private sealed class HubProbe : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        private readonly Channel<string> _received = Channel.CreateUnbounded<string>();

        public HubProbe(HubConnection connection)
        {
            _connection = connection;
            foreach (var name in new[] { LiveUpdateEvents.NotificationsChanged, LiveUpdateEvents.ModerationChanged })
            {
                connection.On(name, () => _received.Writer.TryWrite(name));
            }
        }

        public async Task WaitForAsync(string name)
        {
            using var timeout = new CancellationTokenSource(Deadline);
            while (await _received.Reader.ReadAsync(timeout.Token) != name)
            {
            }
        }

        public async Task AssertNothingAsync()
        {
            await Task.Delay(Quiet);
            Assert.False(_received.Reader.TryRead(out var name), $"Unexpected hint: {name}");
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}

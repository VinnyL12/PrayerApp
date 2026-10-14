using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PrayerApp.Api.Data;
using PrayerApp.Api.LiveUpdates;
using PrayerApp.Api.Notifications;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Acknowledgments;
using PrayerApp.Shared.Contracts.Notifications;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

/// <summary>
/// The transactional outbox behind response notifications: rows survive a restart, a bad
/// row is set aside without blocking the rest, and withdrawing a response leaves nothing.
/// </summary>
[Collection("Api")]
public class NotificationOutboxTests(ApiFactory factory)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_response_passes_through_the_outbox_and_leaves_it_empty()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();

        await Acknowledge(supporter, postId);

        await WaitForUnreadAsync(author, 1);
        await WaitUntilAsync(async db => !await db.NotificationOutbox.AnyAsync(e => e.PostId == postId));
    }

    [Fact]
    public async Task Rows_committed_before_a_restart_are_delivered_when_the_processor_starts()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();

        // The state a crash leaves behind: the acknowledgment and its outbox row committed,
        // the process gone before processing it. Nothing rings the signal.
        await WithDbAsync(async db =>
        {
            db.Acknowledgments.Add(new Acknowledgment
            {
                PostId = postId, UserId = supporter.User.Id, Type = AcknowledgmentType.PrayingForYou, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.NotificationOutbox.Add(Pending(author.User.Id, supporter.User.Id, postId));
            await db.SaveChangesAsync();
            await SyncCounterAsync(db, postId);
        });

        // A new process: its processor drains the table as it starts, without being signalled.
        var restarted = new NotificationProcessor(
            new NotificationSignal(),
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<LiveUpdatePublisher>(),
            TimeProvider.System,
            NullLogger<NotificationProcessor>.Instance);

        await restarted.StartAsync(CancellationToken.None);
        try
        {
            await WaitForUnreadAsync(author, 1);
        }
        finally
        {
            await restarted.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_row_that_keeps_failing_is_set_aside_and_the_rest_are_delivered()
    {
        var (author, supporter, postId) = await PostWithSupporterAsync();
        var other = await JoinedUserAsync(await CommunityOfAsync(postId), "Other");

        long poisonId = 0;
        await WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.Acknowledgments.AddRange(
                new Acknowledgment { PostId = postId, UserId = supporter.User.Id, Type = AcknowledgmentType.PrayingForYou, CreatedAt = now },
                new Acknowledgment { PostId = postId, UserId = other.User.Id, Type = AcknowledgmentType.PrayingForYou, CreatedAt = now });

            // A recipient that doesn't exist: the Notifications foreign key rejects it every
            // time, so this row fails any batch it's in. Its neighbour is fine.
            var poison = Pending(Guid.NewGuid(), other.User.Id, postId);
            db.NotificationOutbox.AddRange(poison, Pending(author.User.Id, supporter.User.Id, postId));
            await db.SaveChangesAsync();
            await SyncCounterAsync(db, postId);
            poisonId = poison.Id;
        });

        factory.Services.GetRequiredService<NotificationSignal>().Ring();

        await WaitForUnreadAsync(author, 1);
        await WaitUntilAsync(async db => await db.NotificationOutbox.AnyAsync(e => e.Id == poisonId && e.FailedAt != null));

        await WithDbAsync(async db =>
        {
            var parked = await db.NotificationOutbox.SingleAsync(e => e.Id == poisonId);
            Assert.Contains("FOREIGN KEY", parked.LastError);
        });
    }

    [Fact]
    public async Task Responding_then_withdrawing_at_once_never_leaves_a_notification()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId, "Author");
        var postId = await CreatePostAsync(author, communityId);
        var supporters = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => JoinedUserAsync(communityId, "Supporter")));

        // Each withdraw races the processor for its own pending row.
        await Task.WhenAll(supporters.Select(async s =>
        {
            await Acknowledge(s, postId);
            await s.Client.DeleteAsync($"/posts/{postId}/acknowledgments/mine");
        }));

        await WaitUntilAsync(async db => !await db.NotificationOutbox.AnyAsync(e => e.PostId == postId));
        var list = await (await author.Client.GetAsync("/me/notifications?limit=50")).ReadAsync<NotificationList>();
        Assert.Empty(list.Items);
    }

    // --- Helpers -----------------------------------------------------------------------

    private static OutboxNotification Pending(Guid recipient, Guid actor, int postId) => new()
    {
        Type = NotificationType.PostAcknowledged,
        RecipientUserId = recipient,
        ActorUserId = actor,
        PostId = postId,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Acknowledgments inserted directly bypass the endpoint's counter update; keep the invariant.</summary>
    private static Task SyncCounterAsync(AppDbContext db, int postId) =>
        db.Posts.Where(p => p.Id == postId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AcknowledgmentCount, p => p.Acknowledgments.Count()));

    private async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task WaitUntilAsync(Func<AppDbContext, Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Deadline;
        while (true)
        {
            var met = false;
            await WithDbAsync(async db => met = await condition(db));
            if (met)
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the outbox.");
            await Task.Delay(50);
        }
    }

    private static async Task WaitForUnreadAsync(TestUser user, int count)
    {
        var deadline = DateTime.UtcNow + Deadline;
        while (true)
        {
            var list = await (await user.Client.GetAsync("/me/notifications?limit=50")).ReadAsync<NotificationList>();
            if (list.UnreadCount >= count || DateTime.UtcNow > deadline)
            {
                Assert.Equal(count, list.UnreadCount);
                return;
            }

            await Task.Delay(50);
        }
    }

    private async Task<int> CommunityOfAsync(int postId)
    {
        var communityId = 0;
        await WithDbAsync(async db => communityId = await db.Posts.Where(p => p.Id == postId).Select(p => p.CommunityId).SingleAsync());
        return communityId;
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

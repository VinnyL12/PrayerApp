using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrayerApp.Api.Data;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class DemoDataSeederTests(ApiFactory factory)
{
    [Fact]
    public async Task Seeds_consistent_data_once()
    {
        await DemoDataSeeder.SeedAsync(factory.Services, "PrayerDemo2026");
        var first = await SnapshotAsync();

        // Second run is a no-op.
        await DemoDataSeeder.SeedAsync(factory.Services, "PrayerDemo2026");
        var second = await SnapshotAsync();

        Assert.True(first.DemoPosts > 0);
        Assert.Equal(first, second);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The denormalized counter matches the real rows for every seeded post.
        var mismatches = await db.Posts
            .Where(p => p.Author.Email!.EndsWith("@prayerapp.local"))
            .CountAsync(p => p.AcknowledgmentCount != p.Acknowledgments.Count());
        Assert.Equal(0, mismatches);

        // Nobody acknowledged their own post, and no acknowledgment predates its post.
        Assert.False(await db.Acknowledgments.AnyAsync(a => a.UserId == a.Post.AuthorId));
        Assert.False(await db.Acknowledgments.AnyAsync(a => a.CreatedAt < a.Post.CreatedAt));

        // Moderators have something to review: one post already auto-hidden, more reported.
        Assert.True(await db.Posts.AnyAsync(p => p.ModerationStatus == ModerationStatus.UnderReview));
        Assert.True(await db.Reports.CountAsync(r => r.ResolvedAt == null) >= 4);
        Assert.False(await db.Reports.AnyAsync(r => r.ReporterUserId == r.Post.AuthorId));
        Assert.False(await db.Reports.AnyAsync(r => r.Reporter.Email == DemoDataSeeder.DemoEmail));

        // Some posts have follow-ups, and only people who responded were notified of them.
        Assert.True(await db.PostUpdates.CountAsync() >= 6);
        Assert.False(await db.PostUpdates.AnyAsync(u => u.CreatedAt < u.Post.CreatedAt));
        Assert.True(await db.Notifications.AnyAsync(n => n.Type == NotificationType.PostUpdated));
        Assert.False(await db.Notifications.AnyAsync(n => n.Type == NotificationType.PostUpdated
            && !db.Acknowledgments.Any(a => a.PostId == n.PostId && a.UserId == n.RecipientUserId)));
    }

    [Fact]
    public async Task Demo_user_can_sign_in()
    {
        await DemoDataSeeder.SeedAsync(factory.Services, "PrayerDemo2026");
        var client = factory.CreateApiClient();

        var response = await client.PostJsonAsync("/auth/login",
            new PrayerApp.Shared.Contracts.Auth.LoginRequest { Email = DemoDataSeeder.DemoEmail, Password = "PrayerDemo2026" });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Seeds_personal_accounts_once()
    {
        var first = await factory.RegisterAsync("First");
        var second = await factory.RegisterAsync("Second");
        // An unregistered email is skipped, not an error.
        string[] accounts = [first.User.Email, second.User.Email, $"{Guid.NewGuid():N}@example.com"];

        await DemoDataSeeder.SeedAsync(factory.Services, "PrayerDemo2026", accounts);
        var seeded = await PersonalSnapshotAsync(first.User.Id, second.User.Id);

        // Second run is a no-op.
        await DemoDataSeeder.SeedAsync(factory.Services, "PrayerDemo2026", accounts);
        Assert.Equal(seeded, await PersonalSnapshotAsync(first.User.Id, second.User.Id));

        Assert.True(seeded.Posts > 20);
        Assert.True(seeded.ResponsesGiven > 20);
        Assert.True(seeded.Unread > 0);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var id in new[] { first.User.Id, second.User.Id })
        {
            Assert.Equal(7, await db.CommunityMemberships.CountAsync(m => m.UserId == id));
            Assert.Equal(3, await db.Posts.Where(p => p.AuthorId == id).Select(p => p.Status).Distinct().CountAsync());
        }

        // The two accounts responded to each other.
        Assert.True(await db.Acknowledgments.AnyAsync(a => a.UserId == first.User.Id && a.Post.AuthorId == second.User.Id));
        Assert.True(await db.Acknowledgments.AnyAsync(a => a.UserId == second.User.Id && a.Post.AuthorId == first.User.Id));

        // Every counter still matches its rows, including on posts the accounts responded to.
        Assert.Equal(0, await db.Posts.CountAsync(p => p.AcknowledgmentCount != p.Acknowledgments.Count()));
        Assert.False(await db.Acknowledgments.AnyAsync(a => a.UserId == a.Post.AuthorId));
        Assert.False(await db.Acknowledgments.AnyAsync(a => a.CreatedAt < a.Post.CreatedAt));
    }

    private async Task<(int Posts, int ResponsesGiven, int Unread)> PersonalSnapshotAsync(params Guid[] userIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (
            await db.Posts.CountAsync(p => userIds.Contains(p.AuthorId)),
            await db.Acknowledgments.CountAsync(a => userIds.Contains(a.UserId)),
            await db.Notifications.CountAsync(n => userIds.Contains(n.RecipientUserId) && !n.IsRead));
    }

    private async Task<(int DemoPosts, int Acks, int Notifications)> SnapshotAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (
            await db.Posts.CountAsync(p => p.Author.Email!.EndsWith("@prayerapp.local")),
            await db.Acknowledgments.CountAsync(),
            await db.Notifications.CountAsync());
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrayerApp.Api.Data;

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

using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrayerApp.Api.Admin;
using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Enums;
using Testcontainers.MsSql;

namespace PrayerApp.Tests.Integration;

/// <summary>
/// Boots the real API in memory against a throwaway SQL Server container. One instance
/// is shared by every test in the "Api" collection, so the container starts once per run.
/// Tests isolate themselves by creating their own users (and communities when they need
/// an empty one) instead of resetting the database between tests.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" (not Development) so user secrets and dev-only config aren't loaded.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _sql.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Admin:Emails:0", OwnerEmail);
    }

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        // Same migrations as production, so tests also prove the migrations apply cleanly.
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            // https so the Secure refresh cookie is valid; cookies are handled manually
            // in tests so we can copy, replay and race them deliberately.
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    /// <summary>Configured as an owner (Admin:Emails), the way a real deployment bootstraps its first admin.</summary>
    public const string OwnerEmail = "owner@prayerapp.test";

    /// <summary>
    /// Makes a user an admin directly in the database, like a promotion by another admin.
    /// No new token needed: the Admin policy checks the database on every request.
    /// </summary>
    public async Task MakeAdminAsync(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = AdminRole.Id });
        await db.SaveChangesAsync();
    }

    /// <summary>A fresh, empty community, for tests that need a predictable feed or member count.</summary>
    public async Task<int> CreateCommunityAsync(CommunityCategory category = CommunityCategory.General)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var community = new Community
        {
            Name = $"Test community {Guid.NewGuid():N}",
            Description = "Created by an integration test.",
            Category = category,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Communities.Add(community);
        await db.SaveChangesAsync();
        return community.Id;
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

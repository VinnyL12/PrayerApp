using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Domain.Entities;

namespace PrayerApp.Api.Data;

/// <summary>
/// The EF Core unit of work. IdentityDbContext adds the AspNetUsers/AspNetRoles/etc.
/// tables; the DbSets below are our own tables.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Community> Communities => Set<Community>();
    public DbSet<CommunityMembership> CommunityMemberships => Set<CommunityMembership>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostImage> PostImages => Set<PostImage>();
    public DbSet<PostUpdate> PostUpdates => Set<PostUpdate>();
    public DbSet<Acknowledgment> Acknowledgments => Set<Acknowledgment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxNotification> NotificationOutbox => Set<OutboxNotification>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity's own table configuration must run first.
        base.OnModelCreating(builder);

        // Picks up every IEntityTypeConfiguration<T> class in this assembly.
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

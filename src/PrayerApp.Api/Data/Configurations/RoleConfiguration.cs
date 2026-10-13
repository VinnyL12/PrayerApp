using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrayerApp.Api.Admin;
using PrayerApp.Shared.Contracts.Auth;

namespace PrayerApp.Api.Data.Configurations;

/// <summary>
/// Identity's AspNetRoles table already existed (IdentityDbContext creates it); this seeds
/// its one row. HasData puts the insert in a migration, so the role exists in every
/// database, test containers included, with no startup code racing to create it.
/// </summary>
public class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder) =>
        builder.HasData(new IdentityRole<Guid>
        {
            Id = AdminRole.Id,
            Name = Roles.Admin,
            NormalizedName = Roles.Admin.ToUpperInvariant(),
            // Fixed, not Guid.NewGuid(): a new value on every build would look like a
            // model change and generate an empty migration each time.
            ConcurrencyStamp = "b0f7c2de-3a51-4c8e-9d62-7a1e5f4b8c30",
        });
}

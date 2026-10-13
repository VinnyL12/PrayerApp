namespace PrayerApp.Api.Admin;

/// <summary>
/// The one stored role. Seeded with a fixed id (see RoleConfiguration), so queries can
/// match AspNetUserRoles.RoleId directly instead of joining AspNetRoles by name.
/// </summary>
public static class AdminRole
{
    public static readonly Guid Id = Guid.Parse("6c1d3b2a-9f4e-4a8b-b5d1-2e7f0a9c4d11");
}

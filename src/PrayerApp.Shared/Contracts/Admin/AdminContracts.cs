namespace PrayerApp.Shared.Contracts.Admin;

/// <param name="IsOwner">Configured in Admin:Emails: always an admin, and can't be demoted.</param>
public sealed record AdminUserDto(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsAdmin,
    bool IsOwner,
    DateTimeOffset CreatedAt);

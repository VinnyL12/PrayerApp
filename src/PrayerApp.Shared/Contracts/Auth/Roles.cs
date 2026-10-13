namespace PrayerApp.Shared.Contracts.Auth;

/// <summary>
/// The app has two kinds of account: users, and admins who can also moderate every
/// community and manage other admins. "User" isn't a stored role, it's the default.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
}

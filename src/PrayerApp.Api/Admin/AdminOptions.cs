namespace PrayerApp.Api.Admin;

/// <summary>Bound from the "Admin" configuration section.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Owners: accounts that are always admins, whatever the database says, and can't be
    /// demoted. Configuration is the way in for the very first admin, and a guarantee that
    /// the app can never end up with nobody able to manage it.
    /// </summary>
    public string[] Emails { get; set; } = [];
}

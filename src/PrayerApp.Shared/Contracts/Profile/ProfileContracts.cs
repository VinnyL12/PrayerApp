namespace PrayerApp.Shared.Contracts.Profile;

/// <summary>The signed-in user's own profile. Only ever returned to that user.</summary>
/// <param name="IsOwner">An admin set in configuration (Admin:Emails).</param>
/// <param name="ResponsesGiven">Responses this user has left on other people's posts.</param>
/// <param name="ResponsesReceived">Responses on this user's own posts. A count, never who.</param>
public sealed record ProfileDto(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsAdmin,
    bool IsOwner,
    DateTimeOffset MemberSince,
    int Posts,
    int ResponsesGiven,
    int ResponsesReceived,
    int Communities);

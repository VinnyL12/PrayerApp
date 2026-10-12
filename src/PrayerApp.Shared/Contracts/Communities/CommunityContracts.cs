using PrayerApp.Shared.Enums;

namespace PrayerApp.Shared.Contracts.Communities;

public sealed record CommunityDto(
    int Id,
    string Name,
    string Description,
    CommunityCategory Category,
    int MemberCount,
    bool IsMember);

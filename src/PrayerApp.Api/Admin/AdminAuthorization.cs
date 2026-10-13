using Microsoft.AspNetCore.Authorization;
using PrayerApp.Api.Common;

namespace PrayerApp.Api.Admin;

public static class Policies
{
    public const string Admin = "Admin";
}

/// <summary>Marker for the Admin policy: "the caller is an admin right now".</summary>
public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Checks the database on every admin request instead of trusting a role claim in the
/// JWT. A claim is a copy made when the token was issued; a demoted admin's token would
/// keep its "Admin" claim for up to 15 minutes. Admin requests are rare, so one indexed
/// query each is a cheap price for revocation that takes effect immediately.
/// </summary>
public sealed class AdminAuthorizationHandler(AdminAccess access) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && await access.IsAdminAsync(context.User.GetUserId()))
        {
            context.Succeed(requirement);
        }
    }
}

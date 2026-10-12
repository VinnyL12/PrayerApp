using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace PrayerApp.Api.Common;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated user's id from the JWT "sub" claim. Only call this on
    /// endpoints that require authorization, where the claim is guaranteed present.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Authenticated principal has no 'sub' claim."));
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Shared.Contracts.Notifications;

namespace PrayerApp.Api.Notifications;

public static class NotificationEndpoints
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;
    private const int ExcerptLength = 80;

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me/notifications").WithTags("Notifications");

        group.MapGet("/", List);
        group.MapPost("/{id:int}/read", MarkRead);
        group.MapPost("/read-all", MarkAllRead);

        return app;
    }

    private static async Task<Ok<NotificationList>> List(
        bool? unreadOnly, int? limit, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();
        var mine = db.Notifications.Where(n => n.RecipientUserId == me);

        if (unreadOnly == true)
        {
            mine = mine.Where(n => !n.IsRead);
        }

        var items = await mine
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit))
            .Select(n => new NotificationDto(
                n.Id,
                n.Type,
                n.PostId,
                n.Post.Body.Length > ExcerptLength ? n.Post.Body.Substring(0, ExcerptLength) + "…" : n.Post.Body,
                n.IsRead,
                n.CreatedAt))
            .ToListAsync(ct);

        // Served by the (RecipientUserId, IsRead, CreatedAt) index: a seek, not a scan.
        var unreadCount = await db.Notifications.CountAsync(n => n.RecipientUserId == me && !n.IsRead, ct);

        return TypedResults.Ok(new NotificationList(items, unreadCount));
    }

    private static async Task<Results<NoContent, NotFound>> MarkRead(
        int id, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        // Filtering on recipient makes someone else's notification indistinguishable from a missing one.
        var updated = await db.Notifications
            .Where(n => n.Id == id && n.RecipientUserId == me)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

        return updated == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<NoContent> MarkAllRead(AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        await db.Notifications
            .Where(n => n.RecipientUserId == me && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

        return TypedResults.NoContent();
    }
}

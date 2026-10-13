using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Api.Common;
using PrayerApp.Api.Data;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Posts;

/// <summary>
/// A post's single image: the author uploads, replaces or removes it; anyone holding its
/// URL can view it.
///
/// Why the view endpoint is anonymous: an &lt;img&gt; tag can't send the bearer token, which
/// lives in the client's memory. So the URL itself is the permission (a "capability URL"):
/// /images/{random GUID}, only ever handed out inside a PostDto to people who can see the
/// post. Like a signed S3 link, someone who can see the image could share the URL, but no
/// one can guess one. Archiving or hiding the post turns the URL off. Admins reviewing
/// a hidden post load its image through the moderation endpoints instead.
/// </summary>
public static class PostImageEndpoints
{
    public static IEndpointRouteBuilder MapPostImageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Images");

        group.MapPut("/posts/{postId:int}/image", Upload)
            // Antiforgery tokens defend cookie-authenticated forms against cross-site
            // posts. This endpoint authenticates with a bearer token that a browser never
            // attaches by itself, so there's nothing to forge.
            .DisableAntiforgery()
            // Reject oversized bodies before reading them. A little headroom over the image
            // limit for the multipart boundaries and headers.
            .WithMetadata(new RequestSizeLimitAttribute(PostImageRules.MaxBytes + 64 * 1024));
        group.MapDelete("/posts/{postId:int}/image", Remove);
        group.MapGet("/images/{id:guid}", View).AllowAnonymous();

        return app;
    }

    private static async Task<Results<Ok<PostDto>, NotFound, ProblemHttpResult, ValidationProblem>> Upload(
        int postId,
        IFormFile image,
        AppDbContext db,
        ClaimsPrincipal principal,
        TimeProvider time,
        CancellationToken ct)
    {
        var me = principal.GetUserId();

        // Someone else's post looks identical to a missing one.
        var post = await db.Posts.Include(p => p.Image)
            .SingleOrDefaultAsync(p => p.Id == postId && p.AuthorId == me, ct);
        if (post is null)
        {
            return TypedResults.NotFound();
        }

        if (post.Status == PostStatus.Archived)
        {
            return TypedResults.Problem("This post is archived.", statusCode: StatusCodes.Status409Conflict);
        }

        if (image.Length is 0 or > PostImageRules.MaxBytes)
        {
            return InvalidImage($"Images must be under {PostImageRules.MaxBytes / (1024 * 1024)} MB.");
        }

        // At most 5 MB, so buffering in memory is fine and gives the sniffer the whole file.
        using var buffer = new MemoryStream((int)image.Length);
        await image.CopyToAsync(buffer, ct);
        var data = buffer.ToArray();

        if (ImageFormats.Detect(data) is not { } contentType)
        {
            return InvalidImage("Only JPEG, PNG or WebP images can be attached.");
        }

        // Replacing gets a new id, so the old URL stops working and no browser can show a
        // stale cached copy under the new one. EF deletes the old row (an orphaned
        // dependent) before inserting the new one, so the unique PostId index holds.
        post.Image = new PostImage
        {
            Id = Guid.NewGuid(),
            ContentType = contentType,
            Data = data,
            CreatedAt = time.GetUtcNow(),
        };
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await db.Posts.Where(p => p.Id == postId).ToDto(me).SingleAsync(ct));
    }

    private static async Task<Results<Ok<PostDto>, NotFound>> Remove(
        int postId, AppDbContext db, ClaimsPrincipal principal, CancellationToken ct)
    {
        var me = principal.GetUserId();

        if (!await db.Posts.AnyAsync(p => p.Id == postId && p.AuthorId == me, ct))
        {
            return TypedResults.NotFound();
        }

        // Allowed even on archived posts: taking a photo down should never be blocked.
        await db.PostImages.Where(i => i.PostId == postId).ExecuteDeleteAsync(ct);

        return TypedResults.Ok(await db.Posts.Where(p => p.Id == postId).ToDto(me).SingleAsync(ct));
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> View(
        Guid id, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var image = await db.PostImages
            .Where(i => i.Id == id
                && i.Post.Status != PostStatus.Archived
                && i.Post.ModerationStatus == ModerationStatus.Visible)
            .Select(i => new { i.ContentType, i.Data })
            .SingleOrDefaultAsync(ct);

        if (image is null)
        {
            return TypedResults.NotFound();
        }

        // The bytes behind a URL never change (a new upload gets a new id), so browsers may
        // cache it for good. "private" keeps shared caches such as proxies from storing it.
        context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        // Never let a browser guess a different type than the one we detected.
        context.Response.Headers.XContentTypeOptions = "nosniff";

        return TypedResults.File(image.Data, image.ContentType);
    }

    private static ValidationProblem InvalidImage(string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["image"] = [message] });
}

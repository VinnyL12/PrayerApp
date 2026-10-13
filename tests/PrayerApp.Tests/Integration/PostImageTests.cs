using System.Net;
using System.Net.Http.Headers;
using PrayerApp.Shared.Contracts.Posts;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Integration;

[Collection("Api")]
public class PostImageTests(ApiFactory factory)
{
    // Only the leading "magic" bytes matter to the server's format check.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 5, 6, 7, 8];

    [Fact]
    public async Task Author_attaches_an_image_that_members_and_img_tags_can_load()
    {
        var (author, member, postId) = await PostWithMemberAsync();

        var response = await Upload(author, postId, Png, "image/png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var imageId = (await response.ReadAsync<PostDto>()).ImageId;
        Assert.NotNull(imageId);

        // Members see the id in the post itself.
        var seen = await (await member.Client.GetAsync($"/posts/{postId}")).ReadAsync<PostDto>();
        Assert.Equal(imageId, seen.ImageId);

        // The image URL works without a token, the way an <img> tag requests it.
        var image = await factory.CreateApiClient().GetAsync($"/images/{imageId}");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task The_stored_type_comes_from_the_bytes_not_the_client()
    {
        var (author, _, postId) = await PostWithMemberAsync();

        // A JPEG labelled as PNG is stored and served as what it really is.
        var imageId = (await (await Upload(author, postId, Jpeg, "image/png")).ReadAsync<PostDto>()).ImageId;

        var image = await factory.CreateApiClient().GetAsync($"/images/{imageId}");
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "image/svg+xml")]
    [InlineData("<html><script>alert(1)</script></html>", "image/png")]
    [InlineData("just some text", "image/jpeg")]
    public async Task Files_that_are_not_supported_images_are_refused(string content, string claimedType)
    {
        var (author, _, postId) = await PostWithMemberAsync();

        var response = await Upload(author, postId, System.Text.Encoding.UTF8.GetBytes(content), claimedType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_images_are_refused()
    {
        var (author, _, postId) = await PostWithMemberAsync();
        var huge = new byte[PostImageRules.MaxBytes + 1];
        Png.CopyTo(huge, 0);

        var response = await Upload(author, postId, huge, "image/png");

        // 413 where the server enforces the request size limit, 400 from our own check.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
    }

    [Fact]
    public async Task Only_the_author_can_attach_an_image()
    {
        var (_, member, postId) = await PostWithMemberAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Upload(member, postId, Png, "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.Client.DeleteAsync($"/posts/{postId}/image")).StatusCode);
    }

    [Fact]
    public async Task Replacing_an_image_retires_the_old_url()
    {
        var (author, _, postId) = await PostWithMemberAsync();
        var first = (await (await Upload(author, postId, Png, "image/png")).ReadAsync<PostDto>()).ImageId;

        var second = (await (await Upload(author, postId, Jpeg, "image/jpeg")).ReadAsync<PostDto>()).ImageId;

        Assert.NotEqual(first, second);
        var anonymous = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/images/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/images/{second}")).StatusCode);
    }

    [Fact]
    public async Task Removing_or_archiving_turns_the_url_off()
    {
        var (author, _, postId) = await PostWithMemberAsync();
        var anonymous = factory.CreateApiClient();

        var removedId = (await (await Upload(author, postId, Png, "image/png")).ReadAsync<PostDto>()).ImageId;
        var afterRemove = await (await author.Client.DeleteAsync($"/posts/{postId}/image")).ReadAsync<PostDto>();
        Assert.Null(afterRemove.ImageId);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/images/{removedId}")).StatusCode);

        var archivedId = (await (await Upload(author, postId, Png, "image/png")).ReadAsync<PostDto>()).ImageId;
        await author.Client.PatchJsonAsync($"/posts/{postId}/status", new UpdatePostStatusRequest { Status = PostStatus.Archived });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/images/{archivedId}")).StatusCode);

        // And archived posts can't get a new one.
        Assert.Equal(HttpStatusCode.Conflict, (await Upload(author, postId, Png, "image/png")).StatusCode);
    }

    [Fact]
    public async Task Posts_carry_a_normalized_link_and_reject_unsafe_ones()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId);
        var url = $"/communities/{communityId}/posts";

        var created = await author.Client.PostJsonAsync(url, new CreatePostRequest { Body = "Rent help", Link = "gofundme.com/f/rent" });
        Assert.Equal("https://gofundme.com/f/rent", (await created.ReadAsync<PostDto>()).Link);

        var unsafeLink = await author.Client.PostJsonAsync(url, new CreatePostRequest { Body = "Click", Link = "javascript:alert(1)" });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeLink.StatusCode);
    }

    private async Task<(TestUser Author, TestUser Member, int PostId)> PostWithMemberAsync()
    {
        var communityId = await factory.CreateCommunityAsync();
        var author = await JoinedUserAsync(communityId);
        var member = await JoinedUserAsync(communityId);
        var post = await author.Client.PostJsonAsync($"/communities/{communityId}/posts", new CreatePostRequest { Body = "Our new home" });
        return (author, member, (await post.ReadAsync<PostDto>()).Id);
    }

    private async Task<TestUser> JoinedUserAsync(int communityId)
    {
        var user = await factory.RegisterAsync();
        await user.Client.PutAsync($"/communities/{communityId}/membership", null);
        return user;
    }

    private static Task<HttpResponseMessage> Upload(TestUser user, int postId, byte[] bytes, string contentType)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return user.Client.PutAsync($"/posts/{postId}/image", new MultipartFormDataContent { { file, "image", "photo" } });
    }
}

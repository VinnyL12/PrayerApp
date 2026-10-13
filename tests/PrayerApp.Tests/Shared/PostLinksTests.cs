using PrayerApp.Shared.Contracts.Posts;

namespace PrayerApp.Tests.Shared;

public class PostLinksTests
{
    [Theory]
    [InlineData("https://www.gofundme.com/f/help-the-reyes-family", "https://www.gofundme.com/f/help-the-reyes-family")]
    [InlineData("  https://cash.app/$jordan  ", "https://cash.app/$jordan")]
    [InlineData("gofundme.com/f/x", "https://gofundme.com/f/x")]
    public void Valid_links_are_normalized(string input, string expected)
    {
        Assert.True(PostLinks.TryNormalize(input, out var link, out _));
        Assert.Equal(expected, link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_means_no_link(string? input)
    {
        Assert.True(PostLinks.TryNormalize(input, out var link, out _));
        Assert.Null(link);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("http://gofundme.com/f/x")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://gofundme.com@scam.example/f/x")]
    [InlineData("https://localhost/x")]
    [InlineData("not a link")]
    public void Unsafe_or_malformed_links_are_refused(string input)
    {
        Assert.False(PostLinks.TryNormalize(input, out var link, out var error));
        Assert.Null(link);
        Assert.NotNull(error);
    }

    [Fact]
    public void Overlong_links_are_refused() =>
        Assert.False(PostLinks.TryNormalize("https://example.com/" + new string('a', PostLinks.MaxLength), out _, out _));

    [Theory]
    [InlineData("https://www.gofundme.com/f/x", "Support via GoFundMe", "gofundme.com")]
    [InlineData("https://cash.app/$jordan", "Support via Cash App", "cash.app")]
    [InlineData("https://account.venmo.com/u/jordan", "Support via Venmo", "account.venmo.com")]
    [InlineData("https://paypal.me/jordan", "Support via PayPal", "paypal.me")]
    // Ends with the right letters, but it's someone else's domain.
    [InlineData("https://notgofundme.com/f/x", "Open link", "notgofundme.com")]
    // A Cyrillic "е" lookalike shows in its punycode form, and gets no platform label.
    [InlineData("https://gofundmе.com/f/x", "Open link", "xn--gofundm-ehg.com")]
    public void Links_are_labelled_by_their_real_host(string link, string title, string host)
    {
        Assert.Equal(title, PostLinks.Title(link));
        Assert.Equal(host, PostLinks.Host(link));
    }
}

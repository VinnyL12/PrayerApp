namespace PrayerApp.Shared.Contracts.Posts;

/// <summary>
/// Rules for the optional link on a post. One copy: the API enforces them and the client
/// uses them to validate as you type and to label links on post cards.
/// </summary>
public static class PostLinks
{
    public const int MaxLength = 2048;

    // Recognized by host, so the card can say "Support via Cash App" rather than a raw URL.
    private static readonly (string Domain, string Name)[] KnownPlatforms =
    [
        ("cash.app", "Cash App"),
        ("gofundme.com", "GoFundMe"),
        ("venmo.com", "Venmo"),
        ("paypal.com", "PayPal"),
        ("paypal.me", "PayPal"),
    ];

    /// <summary>
    /// Validates and normalizes a link. Blank means "no link" and is valid. "gofundme.com/f/x"
    /// is accepted and becomes "https://gofundme.com/f/x".
    /// </summary>
    public static bool TryNormalize(string? input, out string? link, out string? error)
    {
        link = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var text = input.Trim();

        // No scheme at all: assume https. Anything containing ':' is left alone, so
        // "javascript:alert(1)" can't sneak through by having "https://" glued on.
        if (!text.Contains(':'))
        {
            text = "https://" + text;
        }

        if (text.Length > MaxLength)
        {
            error = $"Links can be at most {MaxLength} characters.";
            return false;
        }

        // https only: no javascript:, data: or file: links, and no unencrypted pages
        // asking for money.
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Contains('.'))
        {
            error = "Enter a full web address that starts with https://.";
            return false;
        }

        // "https://gofundme.com@scam.example" goes to scam.example: everything before the @
        // is a username. Real links never need one, so refuse it outright.
        if (uri.UserInfo.Length > 0)
        {
            error = "That link hides where it really goes.";
            return false;
        }

        link = uri.AbsoluteUri;
        return true;
    }

    /// <summary>The host without "www.", shown under every link so readers see where it goes.</summary>
    public static string Host(string link)
    {
        // IdnHost is the ASCII (punycode) form: a lookalike such as "gofundmе.com" with a
        // Cyrillic "е" shows as "xn--gofundm-..." instead of passing for the real site.
        var host = new Uri(link).IdnHost.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>"Support via GoFundMe" for a known platform, otherwise "Open link".</summary>
    public static string Title(string link)
    {
        var host = Host(link);

        foreach (var (domain, name) in KnownPlatforms)
        {
            // Exact domain or a subdomain of it. A plain EndsWith would also match
            // "notgofundme.com", which anyone can register.
            if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal))
            {
                return $"Support via {name}";
            }
        }

        return "Open link";
    }
}

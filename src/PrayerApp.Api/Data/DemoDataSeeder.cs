using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Data;

/// <summary>
/// Development-only sample data so the app looks alive on first run: a handful of users,
/// posts of different ages across communities, and acknowledgments spread over time so
/// the feed ranking has something to rank. Runs once; skipped if the demo user exists.
///
/// Enabled by "Demo:SeedOnStartup" in appsettings.Development.json. Never runs in tests
/// (they use the "Testing" environment) or outside Development.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoEmail = "demo@prayerapp.local";

    private const string DemoDomain = "@prayerapp.local";

    private sealed record DemoPost(
        string Author,
        string Community,
        string Body,
        double HoursAgo,
        bool Urgent = false,
        bool Anonymous = false,
        PostStatus Status = PostStatus.Active,
        // How many acknowledgments, and over how many hours after posting they arrived.
        int Acks = 0,
        double AckSpreadHours = 12);

    private static readonly (string Key, string DisplayName)[] Users =
    [
        ("demo", "Jordan Reyes"),
        ("maria", "Maria Lopez"),
        ("james", "James Carter"),
        ("priya", "Priya Shah"),
        ("daniel", "Daniel Kim"),
        ("ruth", "Ruth Adler"),
        ("samuel", "Samuel Okafor"),
        ("hana", "Hana Yusuf"),
    ];

    // Which communities each user belongs to (by seeded name).
    private static readonly Dictionary<string, string[]> Memberships = new()
    {
        ["demo"] = ["General", "Health & Healing", "Family & Relationships", "Gratitude & Answered Prayers", "Work & Finances"],
        ["maria"] = ["Health & Healing", "Family & Relationships", "General", "Gratitude & Answered Prayers"],
        ["james"] = ["Work & Finances", "General", "Health & Healing"],
        ["priya"] = ["Mental & Emotional Wellbeing", "Health & Healing", "Family & Relationships", "General"],
        ["daniel"] = ["Grief & Loss", "General", "Work & Finances", "Health & Healing"],
        ["ruth"] = ["Grief & Loss", "Gratitude & Answered Prayers", "Family & Relationships", "Health & Healing"],
        ["samuel"] = ["General", "Work & Finances", "Gratitude & Answered Prayers", "Health & Healing", "Family & Relationships"],
        ["hana"] = ["Mental & Emotional Wellbeing", "Health & Healing", "General", "Family & Relationships"],
    };

    private static readonly DemoPost[] Posts =
    [
        // Health & Healing: shows urgency, velocity and age competing in the ranking.
        new("maria", "Health & Healing",
            "My dad goes in for heart surgery tomorrow at 7am. He's scared and trying not to show it. Please keep him and the surgical team in your prayers.",
            HoursAgo: 5, Urgent: true, Acks: 6, AckSpreadHours: 4),
        new("hana", "Health & Healing",
            "Starting chemo next week. I'm hopeful, but the waiting is the hardest part. Strength for the road ahead would mean a lot.",
            HoursAgo: 20, Acks: 7, AckSpreadHours: 18),
        new("priya", "Health & Healing",
            "Asking for healing for my grandmother, who fell last week and broke her hip. She's 89 and stubborn, which I'm choosing to see as a good sign.",
            HoursAgo: 52, Acks: 5, AckSpreadHours: 30),
        new("daniel", "Health & Healing",
            "Waiting on biopsy results. Should hear Friday. Trying to stay calm.",
            HoursAgo: 2, Anonymous: true, Acks: 1, AckSpreadHours: 1),
        new("demo", "Health & Healing",
            "My sister has been in the hospital for a week with pneumonia. She's improving slowly. Prayers for full recovery and for her kids at home.",
            HoursAgo: 30, Acks: 5, AckSpreadHours: 24),

        // Family & Relationships
        new("ruth", "Family & Relationships",
            "My son and I haven't spoken in two years. I sent him a letter this week. Praying he reads it with an open heart.",
            HoursAgo: 9, Acks: 4, AckSpreadHours: 8),
        new("samuel", "Family & Relationships",
            "We're bringing our foster daughter home on Saturday. Excited and nervous in equal measure. Prayers for a gentle transition for her.",
            HoursAgo: 26, Acks: 5, AckSpreadHours: 20),
        new("maria", "Family & Relationships",
            "Please pray for patience. Caring for my mom with dementia while working full-time is wearing me down.",
            HoursAgo: 70, Anonymous: true, Acks: 3, AckSpreadHours: 40),

        // General
        new("james", "General",
            "Moving across the country next month for a fresh start. Asking for peace about leaving everything familiar behind.",
            HoursAgo: 14, Acks: 3, AckSpreadHours: 10),
        new("hana", "General",
            "Some days are just heavy for no particular reason. If you're having one of those days too, you're not alone.",
            HoursAgo: 4, Acks: 5, AckSpreadHours: 3),
        new("priya", "General",
            "Grateful for this community. Reading everyone's posts reminds me how much we all carry quietly.",
            HoursAgo: 40, Acks: 2, AckSpreadHours: 20),

        // Work & Finances
        new("james", "Work & Finances",
            "Laid off on Monday after nine years. Two kids, a mortgage, and a lot of uncertainty. Praying for the right door to open.",
            HoursAgo: 18, Urgent: true, Acks: 6, AckSpreadHours: 16),
        new("demo", "Work & Finances",
            "Final-round interview on Thursday for a job I really want. Praying for clarity and calm.",
            HoursAgo: 6, Acks: 3, AckSpreadHours: 5),

        // Grief & Loss
        new("daniel", "Grief & Loss",
            "Today would have been my wife's 50th birthday. First one without her. Holding on to the good memories.",
            HoursAgo: 7, Acks: 2, AckSpreadHours: 6),
        new("ruth", "Grief & Loss",
            "Lost our family dog of 14 years this morning. It's amazing how much space a small dog takes up in a home.",
            HoursAgo: 33, Acks: 2, AckSpreadHours: 20),

        // Mental & Emotional Wellbeing
        new("priya", "Mental & Emotional Wellbeing",
            "Started therapy this month after putting it off for years. It's harder than I expected, but I think it's helping.",
            HoursAgo: 11, Acks: 1, AckSpreadHours: 6),
        new("hana", "Mental & Emotional Wellbeing",
            "Anxiety has been loud lately. Asking for quiet mornings and steady breathing.",
            HoursAgo: 28, Anonymous: true, Acks: 1, AckSpreadHours: 10),

        // Gratitude & Answered Prayers
        new("ruth", "Gratitude & Answered Prayers",
            "Thank you to everyone who prayed for my husband's recovery last month. He walked the whole block today without his cane!",
            HoursAgo: 15, Acks: 5, AckSpreadHours: 12),
        new("samuel", "Gratitude & Answered Prayers",
            "Our church food drive collected over 2,000 meals this weekend. Thankful for every hand that helped.",
            HoursAgo: 46, Acks: 4, AckSpreadHours: 24),

        // The demo user's answered post: shows the Answered state and a full response breakdown.
        new("demo", "Gratitude & Answered Prayers",
            "Update: my dad's scans came back clear. Thank you all for holding our family up these past few weeks.",
            HoursAgo: 60, Status: PostStatus.Answered, Acks: 6, AckSpreadHours: 30),
    ];

    public static async Task SeedAsync(IServiceProvider services, string password, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<AppDbContext>();
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var time = provider.GetRequiredService<TimeProvider>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoDataSeeder));

        if (await userManager.FindByEmailAsync(DemoEmail) is not null)
        {
            return;
        }

        var now = time.GetUtcNow();
        var communities = await db.Communities.ToDictionaryAsync(c => c.Name, c => c.Id, ct);

        // Users go through UserManager so passwords are hashed exactly like real sign-ups.
        var users = new Dictionary<string, User>();
        foreach (var (key, displayName) in Users)
        {
            var user = new User
            {
                UserName = key + DemoDomain,
                Email = key + DemoDomain,
                DisplayName = displayName,
                CreatedAt = now.AddDays(-14),
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Demo seed failed: " + string.Join(" ", result.Errors.Select(e => e.Description)));
            }

            users[key] = user;
        }

        foreach (var (key, names) in Memberships)
        {
            db.CommunityMemberships.AddRange(names.Select(name => new CommunityMembership
            {
                UserId = users[key].Id,
                CommunityId = communities[name],
                JoinedAt = now.AddDays(-13),
            }));
        }

        // A fixed seed makes the demo identical on every reset.
        var random = new Random(2026);
        foreach (var spec in Posts)
        {
            var author = users[spec.Author];
            var communityId = communities[spec.Community];
            var createdAt = now.AddHours(-spec.HoursAgo);

            // Acknowledgers: other members of the same community.
            var supporters = Memberships
                .Where(m => m.Key != spec.Author && m.Value.Contains(spec.Community))
                .Select(m => users[m.Key])
                .OrderBy(_ => random.Next())
                .Take(spec.Acks)
                .ToList();

            var post = new Post
            {
                AuthorId = author.Id,
                CommunityId = communityId,
                Body = spec.Body,
                IsUrgent = spec.Urgent,
                IsAnonymous = spec.Anonymous,
                Status = spec.Status,
                CreatedAt = createdAt,
                // Kept equal to the number of Acknowledgment rows, as the API guarantees.
                AcknowledgmentCount = supporters.Count,
            };
            db.Posts.Add(post);

            for (var i = 0; i < supporters.Count; i++)
            {
                // Spread responses across the window, never into the future.
                var offsetHours = spec.AckSpreadHours * (i + 1) / (supporters.Count + 1);
                var ackedAt = createdAt.AddHours(Math.Min(offsetHours, spec.HoursAgo * 0.95));
                var type = Enum.GetValues<AcknowledgmentType>()[random.Next(5)];

                post.Acknowledgments.Add(new Acknowledgment
                {
                    UserId = supporters[i].Id,
                    Type = type,
                    CreatedAt = ackedAt,
                });

                db.Notifications.Add(new Notification
                {
                    Recipient = author,
                    Post = post,
                    ActorUserId = supporters[i].Id,
                    Type = NotificationType.PostAcknowledged,
                    CreatedAt = ackedAt,
                    // Leave the demo user's most recent few unread so the badge shows.
                    IsRead = spec.Author != "demo" || ackedAt < now.AddHours(-8),
                });
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo data: {Users} users, {Posts} posts. Sign in as {Email}.",
            Users.Length, Posts.Length, DemoEmail);
    }
}

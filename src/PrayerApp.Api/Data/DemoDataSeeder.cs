using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PrayerApp.Domain.Entities;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Api.Data;

/// <summary>
/// Development-only sample data so the app looks alive on first run: demo users, posts of
/// different ages across communities, and acknowledgments spread over time so the feed
/// ranking has something to rank.
///
/// Content comes in waves. Each wave is seeded once, and skipped if its first user already
/// exists, so a database seeded by an older version of this file picks up only the newer
/// waves instead of needing a reset.
///
/// Optionally, "Demo:PersonalAccounts" (user secrets, so real emails stay out of the repo)
/// lists accounts you registered yourself. Each one gets its own posts, responses from the
/// demo users, notifications, and responses it gave to other people's posts.
///
/// Enabled by "Demo:SeedOnStartup" in appsettings.Development.json. Never runs in tests
/// (they use the "Testing" environment) or outside Development.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoEmail = "demo@prayerapp.local";

    private const string DemoDomain = "@prayerapp.local";

    // Placeholder author for PersonalPosts: each one is assigned to a configured account.
    private const string Self = "self";

    // How many other people's posts each personal account responds to.
    private const int ResponsesPerPersonalAccount = 30;

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

    private sealed record Wave(
        (string Key, string DisplayName)[] Users,
        // Which communities each of this wave's users belongs to (by seeded name).
        Dictionary<string, string[]> Memberships,
        DemoPost[] Posts);

    private static readonly Wave[] Waves =
    [
        new(
            Users:
            [
                ("demo", "Jordan Reyes"),
                ("maria", "Maria Lopez"),
                ("james", "James Carter"),
                ("priya", "Priya Shah"),
                ("daniel", "Daniel Kim"),
                ("ruth", "Ruth Adler"),
                ("samuel", "Samuel Okafor"),
                ("hana", "Hana Yusuf"),
            ],
            Memberships: new()
            {
                ["demo"] = ["General", "Health & Healing", "Family & Relationships", "Gratitude & Answered Prayers", "Work & Finances"],
                ["maria"] = ["Health & Healing", "Family & Relationships", "General", "Gratitude & Answered Prayers"],
                ["james"] = ["Work & Finances", "General", "Health & Healing"],
                ["priya"] = ["Mental & Emotional Wellbeing", "Health & Healing", "Family & Relationships", "General"],
                ["daniel"] = ["Grief & Loss", "General", "Work & Finances", "Health & Healing"],
                ["ruth"] = ["Grief & Loss", "Gratitude & Answered Prayers", "Family & Relationships", "Health & Healing"],
                ["samuel"] = ["General", "Work & Finances", "Gratitude & Answered Prayers", "Health & Healing", "Family & Relationships"],
                ["hana"] = ["Mental & Emotional Wellbeing", "Health & Healing", "General", "Family & Relationships"],
            },
            Posts:
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
            ]),

        // Wave 2: more people and more posts, so every community feed has depth and Load more.
        new(
            Users:
            [
                ("elena", "Elena Petrova"),
                ("marcus", "Marcus Bell"),
                ("aisha", "Aisha Rahman"),
                ("tom", "Tom Nguyen"),
                ("leah", "Leah Goldberg"),
                ("carlos", "Carlos Mendes"),
                ("noah", "Noah Bennett"),
                ("mei", "Mei Tanaka"),
            ],
            Memberships: new()
            {
                ["elena"] = ["Health & Healing", "Family & Relationships", "Gratitude & Answered Prayers", "General"],
                ["marcus"] = ["Work & Finances", "General", "Mental & Emotional Wellbeing", "Grief & Loss"],
                ["aisha"] = ["Family & Relationships", "Health & Healing", "Gratitude & Answered Prayers", "Mental & Emotional Wellbeing"],
                ["tom"] = ["General", "Work & Finances", "Grief & Loss", "Health & Healing"],
                ["leah"] = ["Grief & Loss", "Family & Relationships", "Mental & Emotional Wellbeing", "Gratitude & Answered Prayers"],
                ["carlos"] = ["Work & Finances", "Family & Relationships", "General", "Health & Healing"],
                ["noah"] = ["Mental & Emotional Wellbeing", "General", "Gratitude & Answered Prayers", "Work & Finances"],
                ["mei"] = ["Health & Healing", "Grief & Loss", "Family & Relationships", "General", "Mental & Emotional Wellbeing"],
            },
            Posts:
            [
                // Health & Healing
                new("elena", "Health & Healing",
                    "My husband was diagnosed with type 1 diabetes at 41. We're learning a whole new way of living. Prayers for patience while we figure it out.",
                    HoursAgo: 12, Acks: 6, AckSpreadHours: 10),
                new("tom", "Health & Healing",
                    "Knee replacement on Monday. The surgery I can handle; it's the six weeks of not working that worries me.",
                    HoursAgo: 36, Acks: 4, AckSpreadHours: 24),
                new("mei", "Health & Healing",
                    "Our newborn is in the NICU, born seven weeks early. She's tiny and fighting hard. Please pray for her lungs to grow strong.",
                    HoursAgo: 3, Urgent: true, Acks: 7, AckSpreadHours: 3),
                new("carlos", "Health & Healing",
                    "Six months sober today. Some days it still feels like walking a tightrope. Asking for strength to keep going.",
                    HoursAgo: 22, Anonymous: true, Acks: 5, AckSpreadHours: 16),
                new("aisha", "Health & Healing",
                    "My mother's MRI is tomorrow morning. The headaches won't stop and the doctors want to rule things out. Praying it's nothing serious.",
                    HoursAgo: 8, Acks: 4, AckSpreadHours: 6),
                new("samuel", "Health & Healing",
                    "Please pray for my pastor, who collapsed during service on Sunday. He's stable now, but they're still running tests.",
                    HoursAgo: 75, Acks: 6, AckSpreadHours: 48),

                // Family & Relationships
                new("aisha", "Family & Relationships",
                    "My teenage son is pulling away from us and I don't know how to reach him. Praying for wisdom and for an open door.",
                    HoursAgo: 16, Acks: 5, AckSpreadHours: 12),
                new("leah", "Family & Relationships",
                    "We've been trying to have a baby for three years. Another negative test this week. Holding on to hope, barely.",
                    HoursAgo: 31, Anonymous: true, Acks: 6, AckSpreadHours: 24),
                new("carlos", "Family & Relationships",
                    "My parents are moving in with us next month. I love them, and I'm also nervous about what this does to our small house and our marriage.",
                    HoursAgo: 58, Acks: 3, AckSpreadHours: 30),
                new("elena", "Family & Relationships",
                    "Our daughter leaves for college on Friday. The house is going to be so quiet. Prayers for her to find good friends quickly.",
                    HoursAgo: 90, Acks: 5, AckSpreadHours: 50),

                // General
                new("noah", "General",
                    "First week at a new church and I don't know anyone yet. Praying for the courage to keep showing up.",
                    HoursAgo: 6, Acks: 4, AckSpreadHours: 5),
                new("tom", "General",
                    "Hurricane season has our town on edge again. Prayers for everyone on the coast this week.",
                    HoursAgo: 10, Urgent: true, Acks: 6, AckSpreadHours: 8),
                new("mei", "General",
                    "Asking for prayers for my neighbor, who lives alone and just lost her sight in one eye. I'm helping where I can.",
                    HoursAgo: 44, Acks: 3, AckSpreadHours: 30),
                new("marcus", "General",
                    "Volunteering at the shelter tonight. Pray that we have enough beds and that everyone who comes in feels seen.",
                    HoursAgo: 1.5, Acks: 2, AckSpreadHours: 1),

                // Work & Finances
                new("marcus", "Work & Finances",
                    "Opened my bakery eighteen months ago and the numbers aren't working. Deciding this month whether to close. Praying for clarity.",
                    HoursAgo: 20, Acks: 5, AckSpreadHours: 16),
                new("carlos", "Work & Finances",
                    "Our rent went up 30% at renewal. We have until the end of the month to figure out where to go.",
                    HoursAgo: 4, Urgent: true, Acks: 5, AckSpreadHours: 3),
                new("noah", "Work & Finances",
                    "Defending my dissertation next Tuesday after six years. Asking for a steady voice and a clear mind.",
                    HoursAgo: 28, Acks: 4, AckSpreadHours: 20),
                new("tom", "Work & Finances",
                    "Working two jobs to cover my mom's medical bills. I'm tired in a way sleep doesn't fix.",
                    HoursAgo: 100, Anonymous: true, Acks: 5, AckSpreadHours: 60),

                // Grief & Loss
                new("leah", "Grief & Loss",
                    "My father passed on Tuesday. The funeral is Sunday. I keep picking up my phone to call him.",
                    HoursAgo: 14, Acks: 6, AckSpreadHours: 12),
                new("marcus", "Grief & Loss",
                    "A year ago today we lost our best friend in a car accident. His kids are doing better than any of us expected. Holding them close today.",
                    HoursAgo: 38, Acks: 4, AckSpreadHours: 24),
                new("mei", "Grief & Loss",
                    "Miscarriage at eleven weeks. We hadn't told anyone yet, so it feels like grieving in secret.",
                    HoursAgo: 60, Anonymous: true, Acks: 5, AckSpreadHours: 40),
                new("tom", "Grief & Loss",
                    "Visiting my grandmother's grave for the first time since the funeral. She taught me how to pray.",
                    HoursAgo: 120, Acks: 3, AckSpreadHours: 50),

                // Mental & Emotional Wellbeing
                new("noah", "Mental & Emotional Wellbeing",
                    "Burnout hit hard this year. Taking a two-week leave starting tomorrow and trying not to feel guilty about it.",
                    HoursAgo: 9, Acks: 5, AckSpreadHours: 7),
                new("aisha", "Mental & Emotional Wellbeing",
                    "Postpartum has been much darker than anyone told me it would be. I've started seeing someone. Prayers for light.",
                    HoursAgo: 24, Anonymous: true, Acks: 6, AckSpreadHours: 18),
                new("leah", "Mental & Emotional Wellbeing",
                    "Panic attacks are back after two good years. Trying to remember that setbacks aren't failures.",
                    HoursAgo: 50, Acks: 4, AckSpreadHours: 30),
                new("marcus", "Mental & Emotional Wellbeing",
                    "Winter is coming, and so are the gray months for me. Asking for prayers in advance this year.",
                    HoursAgo: 80, Acks: 3, AckSpreadHours: 40),

                // Gratitude & Answered Prayers
                new("elena", "Gratitude & Answered Prayers",
                    "Our adoption was finalized this morning! After two years of paperwork, he is officially ours. Thank you for every prayer.",
                    HoursAgo: 5, Acks: 6, AckSpreadHours: 4),
                new("noah", "Gratitude & Answered Prayers",
                    "Got the job! Six months of searching, and it came through the week our savings ran out. Grateful beyond words.",
                    HoursAgo: 19, Acks: 5, AckSpreadHours: 14),
                new("aisha", "Gratitude & Answered Prayers",
                    "My brother came home from deployment safe last night. Watching my kids run to him is something I'll never forget.",
                    HoursAgo: 42, Acks: 6, AckSpreadHours: 30),
                new("leah", "Gratitude & Answered Prayers",
                    "One year cancer-free today. I couldn't have walked this road without the people who carried me.",
                    HoursAgo: 66, Acks: 7, AckSpreadHours: 40),
            ]),
    ];

    /// <summary>
    /// Posts for the accounts in "Demo:PersonalAccounts". Dealt out round-robin, so with two
    /// accounts each gets every other post: a mix of communities, ages and every status.
    /// </summary>
    private static readonly DemoPost[] PersonalPosts =
    [
        new(Self, "Health & Healing",
            "Got a call from my doctor asking me to come back in about my bloodwork. Appointment is Thursday. Trying not to spiral before then.",
            HoursAgo: 3, Urgent: true, Acks: 8, AckSpreadHours: 3),
        new(Self, "Work & Finances",
            "Big presentation to leadership tomorrow morning. A promotion is riding on it. Praying for a clear head and steady nerves.",
            HoursAgo: 5, Urgent: true, Acks: 7, AckSpreadHours: 4),
        new(Self, "Family & Relationships",
            "My brother and I had a falling out over our dad's care. Haven't talked in a month. Praying for the humility to call first.",
            HoursAgo: 9, Acks: 6, AckSpreadHours: 8),
        new(Self, "General",
            "Long week. Grateful for a quiet Sunday and for a community that shows up for each other.",
            HoursAgo: 14, Acks: 5, AckSpreadHours: 10),
        new(Self, "Mental & Emotional Wellbeing",
            "I've been running on empty for weeks and finally told someone. Asking for rest that actually restores.",
            HoursAgo: 18, Anonymous: true, Acks: 7, AckSpreadHours: 14),
        new(Self, "Grief & Loss",
            "My grandfather passed last night, peacefully, with family around him. He was the steadiest man I've ever known.",
            HoursAgo: 22, Acks: 7, AckSpreadHours: 18),
        new(Self, "Gratitude & Answered Prayers",
            "The offer came through! Thank you to everyone who prayed me through those interviews.",
            HoursAgo: 26, Acks: 8, AckSpreadHours: 20),
        new(Self, "Health & Healing",
            "My mom starts radiation on Monday. Five days a week for six weeks. Praying for her strength and for the side effects to be gentle.",
            HoursAgo: 30, Acks: 9, AckSpreadHours: 24),
        new(Self, "Work & Finances",
            "Our car died on the highway and the repair estimate is more than it's worth. Praying for a way through this month.",
            HoursAgo: 34, Acks: 6, AckSpreadHours: 24),
        new(Self, "Family & Relationships",
            "Proposing to my girlfriend this weekend. I've got the ring and a plan. Prayers that the weather holds and my voice doesn't crack.",
            HoursAgo: 40, Acks: 6, AckSpreadHours: 30),
        new(Self, "General",
            "Starting a new Bible study with a few guys from work. Pray that it becomes something real and not just another meeting.",
            HoursAgo: 46, Acks: 5, AckSpreadHours: 30),
        new(Self, "Mental & Emotional Wellbeing",
            "Six weeks of therapy so far. Learning that saying no is not the same as letting people down.",
            HoursAgo: 52, Acks: 5, AckSpreadHours: 36),
        new(Self, "Health & Healing",
            "Tore my ACL playing pickup basketball. Surgery is in two weeks. Praying for a smooth recovery and a lot of patience.",
            HoursAgo: 60, Acks: 7, AckSpreadHours: 40),
        new(Self, "Grief & Loss",
            "It's been a year since we lost my uncle. Our family is getting together this weekend to remember him.",
            HoursAgo: 66, Acks: 5, AckSpreadHours: 40),
        new(Self, "Work & Finances",
            "Paid off the last of my student loans today. Took seven years. Grateful, and a little stunned.",
            HoursAgo: 72, Status: PostStatus.Answered, Acks: 8, AckSpreadHours: 48),
        new(Self, "Family & Relationships",
            "My little sister is getting married next month and money is tight for my parents. Praying it all comes together.",
            HoursAgo: 80, Acks: 5, AckSpreadHours: 48),
        new(Self, "General",
            "Moving into my first apartment on my own next week. Excited, nervous, and slightly terrified of the grocery bill.",
            HoursAgo: 90, Acks: 6, AckSpreadHours: 50),
        new(Self, "Gratitude & Answered Prayers",
            "My nephew was born this morning, healthy and loud. Everyone is doing well. What a gift.",
            HoursAgo: 96, Acks: 9, AckSpreadHours: 60),
        new(Self, "Health & Healing",
            "Asking for prayers for my best friend's dad, who's in the ICU after a stroke. The next 48 hours matter a lot.",
            HoursAgo: 110, Acks: 8, AckSpreadHours: 60),
        new(Self, "Mental & Emotional Wellbeing",
            "Social media has been making me miserable, so I deleted the apps for a month. Day three. Pray I stick with it.",
            HoursAgo: 120, Acks: 4, AckSpreadHours: 60),
        new(Self, "Work & Finances",
            "Starting my own business on the side. It's scary to bet on yourself. Asking for wisdom with every decision.",
            HoursAgo: 135, Acks: 6, AckSpreadHours: 72),
        new(Self, "Family & Relationships",
            "My grandma is moving into assisted living. She's handling it better than the rest of us. Prayers for an easy adjustment.",
            HoursAgo: 150, Acks: 6, AckSpreadHours: 80),
        new(Self, "Grief & Loss",
            "Our neighbor's son died in an accident last week. He was nineteen. I don't know what to say to them, so I'm asking here.",
            HoursAgo: 165, Acks: 7, AckSpreadHours: 90),
        new(Self, "General",
            "Finished my first half marathon! Slow, but I finished. Thanks for the encouragement.",
            HoursAgo: 180, Status: PostStatus.Answered, Acks: 7, AckSpreadHours: 90),
        new(Self, "Health & Healing",
            "The bloodwork came back normal. Thank you for praying with me through a stressful week.",
            HoursAgo: 200, Status: PostStatus.Answered, Acks: 9, AckSpreadHours: 100),
        new(Self, "Mental & Emotional Wellbeing",
            "Sleep has been rough for a month. Praying for quiet nights and a calmer mind.",
            HoursAgo: 220, Anonymous: true, Acks: 5, AckSpreadHours: 100),
        new(Self, "Gratitude & Answered Prayers",
            "Celebrated ten years with my best friends this weekend. Thankful for people who stick around.",
            HoursAgo: 240, Acks: 7, AckSpreadHours: 110),
        new(Self, "Work & Finances",
            "Got laid off this morning along with half my team. Not sure what's next. Praying for peace and a new direction.",
            HoursAgo: 260, Acks: 9, AckSpreadHours: 120),
        new(Self, "Family & Relationships",
            "My cousin is deploying overseas for nine months. Praying for his safety and for his wife and kids at home.",
            HoursAgo: 290, Acks: 7, AckSpreadHours: 140),
        new(Self, "Health & Healing",
            "Back surgery went well. Up and walking the same day. Thank you for the prayers and the meals.",
            HoursAgo: 320, Status: PostStatus.Answered, Acks: 8, AckSpreadHours: 150),
        new(Self, "General",
            "Pray for our youth group's mission trip. Twenty kids, two vans, and a lot of hope.",
            HoursAgo: 350, Acks: 6, AckSpreadHours: 160),
        new(Self, "Grief & Loss",
            "Put my childhood dog down today. Fifteen years. The house feels wrong without her.",
            HoursAgo: 380, Acks: 7, AckSpreadHours: 170),
        new(Self, "Mental & Emotional Wellbeing",
            "Turning thirty this week and feeling behind on everything. Asking for perspective and a little grace for myself.",
            HoursAgo: 410, Acks: 5, AckSpreadHours: 180),
        new(Self, "Work & Finances",
            "Found a new role! Starts in two weeks, better pay and a shorter commute. Answered prayer.",
            HoursAgo: 440, Status: PostStatus.Answered, Acks: 9, AckSpreadHours: 200),
        new(Self, "Gratitude & Answered Prayers",
            "Our church finally raised enough to fix the roof before winter. Thank you to everyone who gave and prayed.",
            HoursAgo: 470, Acks: 8, AckSpreadHours: 220),
        new(Self, "Family & Relationships",
            "My dad and I went fishing for the first time in years. We actually talked. Small thing, big deal.",
            HoursAgo: 500, Acks: 6, AckSpreadHours: 220),
        new(Self, "Health & Healing",
            "Praying for my aunt, who was just diagnosed with early-stage breast cancer. Surgery is scheduled for next month.",
            HoursAgo: 530, Acks: 9, AckSpreadHours: 240),
        new(Self, "General",
            "Lost my wallet with everything in it. Probably nothing, but please pray someone honest finds it.",
            HoursAgo: 560, Status: PostStatus.Archived, Acks: 3, AckSpreadHours: 48),
        new(Self, "Mental & Emotional Wellbeing",
            "Had a panic attack in the middle of a meeting today. First one in years. Embarrassed and shaken.",
            HoursAgo: 590, Anonymous: true, Acks: 6, AckSpreadHours: 240),
        new(Self, "Work & Finances",
            "Tax season and I owe more than I expected. Praying for wisdom with a payment plan.",
            HoursAgo: 620, Status: PostStatus.Archived, Acks: 4, AckSpreadHours: 100),
        new(Self, "Grief & Loss",
            "Two years since my mom passed. Some days are fine. Today isn't one of them.",
            HoursAgo: 650, Acks: 7, AckSpreadHours: 260),
        new(Self, "Family & Relationships",
            "Our roommate situation fell apart and we need a new place by the first. Prayers for an affordable option to open up.",
            HoursAgo: 680, Status: PostStatus.Archived, Acks: 4, AckSpreadHours: 120),
        new(Self, "Gratitude & Answered Prayers",
            "Eight months of physical therapy and I ran a full mile today without pain. Grateful for every small step.",
            HoursAgo: 700, Acks: 8, AckSpreadHours: 280),
        new(Self, "Health & Healing",
            "Getting my wisdom teeth out tomorrow. Small thing, but I'm a nervous patient. A quick prayer would help.",
            HoursAgo: 760, Status: PostStatus.Archived, Acks: 3, AckSpreadHours: 24),
        new(Self, "General",
            "Asking for safe travels for my family driving up for Thanksgiving. Long trip with three little kids.",
            HoursAgo: 820, Status: PostStatus.Archived, Acks: 4, AckSpreadHours: 48),
        new(Self, "Mental & Emotional Wellbeing",
            "Finally feeling like myself again after a hard season. Thank you for checking in on me these past months.",
            HoursAgo: 860, Status: PostStatus.Answered, Acks: 7, AckSpreadHours: 300),
        new(Self, "Work & Finances",
            "Interviewing for my dream job at the end of the week. Praying I'm ready, and at peace if it's a no.",
            HoursAgo: 900, Status: PostStatus.Archived, Acks: 5, AckSpreadHours: 72),
        new(Self, "Family & Relationships",
            "We found out we're expecting our first baby! Early days, so we're praying for a healthy pregnancy.",
            HoursAgo: 950, Status: PostStatus.Answered, Acks: 9, AckSpreadHours: 320),
    ];

    public static async Task SeedAsync(
        IServiceProvider services,
        string password,
        IReadOnlyList<string>? personalAccounts = null,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<AppDbContext>();
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var time = provider.GetRequiredService<TimeProvider>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoDataSeeder));

        var now = time.GetUtcNow();
        var communities = await db.Communities.ToDictionaryAsync(c => c.Name, ct);
        var users = new Dictionary<string, User>();

        for (var w = 0; w < Waves.Length; w++)
        {
            var wave = Waves[w];

            if (await userManager.FindByEmailAsync(wave.Users[0].Key + DemoDomain) is not null)
            {
                // Seeded on an earlier run. Load its users: later waves and personal posts
                // pick supporters from them.
                foreach (var (key, _) in wave.Users)
                {
                    users[key] = await userManager.FindByEmailAsync(key + DemoDomain)
                        ?? throw new InvalidOperationException(
                            $"Demo user {key}{DemoDomain} is missing. Reset the database to reseed.");
                }

                continue;
            }

            // Users go through UserManager so passwords are hashed exactly like real sign-ups.
            foreach (var (key, displayName) in wave.Users)
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

            foreach (var (key, names) in wave.Memberships)
            {
                db.CommunityMemberships.AddRange(names.Select(name => new CommunityMembership
                {
                    UserId = users[key].Id,
                    CommunityId = communities[name].Id,
                    JoinedAt = now.AddDays(-13),
                }));
            }

            // Acknowledgers: members of the post's community from this wave or earlier ones.
            var members = Waves.Take(w + 1).SelectMany(x => x.Memberships).ToList();

            // A fixed seed per wave makes the demo identical on every reset, and keeps a
            // wave's data the same whether it's seeded alone or with the waves before it.
            var random = new Random(2026 + w);
            foreach (var spec in wave.Posts)
            {
                var supporters = members
                    .Where(m => m.Key != spec.Author && m.Value.Contains(spec.Community))
                    .Select(m => users[m.Key])
                    .OrderBy(_ => random.Next())
                    .Take(spec.Acks)
                    .ToList();

                // Leave the demo user's most recent few unread so the badge shows.
                var unreadWindow = spec.Author == "demo" ? TimeSpan.FromHours(8) : TimeSpan.Zero;
                AddPost(db, users[spec.Author], communities[spec.Community], spec, supporters, now, random, unreadWindow);
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded demo wave {Wave}: {Users} users, {Posts} posts. Sign in as {Email}.",
                w + 1, wave.Users.Length, wave.Posts.Length, DemoEmail);
        }

        if (personalAccounts is { Count: > 0 })
        {
            await SeedPersonalAsync(db, userManager, personalAccounts, communities, users, now, logger, ct);
        }
    }

    private static async Task SeedPersonalAsync(
        AppDbContext db,
        UserManager<User> userManager,
        IReadOnlyList<string> emails,
        Dictionary<string, Community> communities,
        Dictionary<string, User> demoUsers,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken ct)
    {
        // Posts are dealt only among registered accounts, so none are lost to an email that
        // hasn't signed up. (Register every account before the first seed: one added later
        // gets a share computed for the bigger group, which overlaps an earlier account's.)
        var accounts = new List<User>();
        foreach (var email in emails.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await userManager.FindByEmailAsync(email) is { } account)
            {
                accounts.Add(account);
            }
            else
            {
                logger.LogWarning("Personal demo account {Email} isn't registered; skipping it. Register it, then restart the API.", email);
            }
        }

        var personalBodies = PersonalPosts.Select(p => p.Body).ToList();
        var personalCommunities = PersonalPosts.Select(p => p.Community).Distinct().Select(name => communities[name]).ToList();
        var communityIds = personalCommunities.Select(c => c.Id).ToList();
        var demoMembers = Waves.SelectMany(w => w.Memberships).ToList();

        for (var i = 0; i < accounts.Count; i++)
        {
            var me = accounts[i];
            if (await db.Posts.AnyAsync(p => p.AuthorId == me.Id && personalBodies.Contains(p.Body), ct))
            {
                continue;
            }

            // Join every community, keeping memberships the account already has.
            var joined = await db.CommunityMemberships
                .Where(m => m.UserId == me.Id).Select(m => m.CommunityId).ToListAsync(ct);
            db.CommunityMemberships.AddRange(personalCommunities
                .Where(c => !joined.Contains(c.Id))
                .Select(c => new CommunityMembership { UserId = me.Id, CommunityId = c.Id, JoinedAt = now.AddDays(-45) }));

            var random = new Random(2126 + i);
            var otherAccounts = accounts.Where(a => a.Id != me.Id).ToList();

            // Round-robin: account i of n gets posts i, i + n, i + 2n, ...
            for (var p = i; p < PersonalPosts.Length; p += accounts.Count)
            {
                var spec = PersonalPosts[p];

                // Demo members of that community, plus the other personal accounts, so
                // they've responded to each other too.
                var supporters = demoMembers
                    .Where(m => m.Value.Contains(spec.Community))
                    .Select(m => demoUsers[m.Key])
                    .Concat(otherAccounts)
                    .OrderBy(_ => random.Next())
                    .Take(spec.Acks)
                    .ToList();

                AddPost(db, me, communities[spec.Community], spec, supporters, now, random, unreadWindow: TimeSpan.FromHours(24));
            }

            // Responses this account gave: other people's recent posts it hasn't responded to.
            var toSupport = await db.Posts
                .Include(p => p.Community)
                .Where(p => p.AuthorId != me.Id
                    && p.Status != PostStatus.Archived
                    && communityIds.Contains(p.CommunityId)
                    && p.CreatedAt > now.AddDays(-30)
                    && !p.Acknowledgments.Any(a => a.UserId == me.Id))
                .ToListAsync(ct);

            foreach (var post in toSupport.OrderBy(_ => random.Next()).Take(ResponsesPerPersonalAccount))
            {
                var allowed = AcknowledgmentPolicy.AllowedFor(post.Community.Category, post.Status);
                var ackedAt = post.CreatedAt + (now - post.CreatedAt) * (0.1 + 0.8 * random.NextDouble());

                // Read-modify-write is safe here only because seeding finishes before the API
                // accepts requests. The real endpoint increments in SQL.
                post.AcknowledgmentCount++;
                AddAcknowledgment(db, post, post.AuthorId, me.Id, allowed[random.Next(allowed.Count)], ackedAt,
                    isRead: ackedAt < now.AddHours(-24));
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded personal demo data for {Email}.", me.Email);
        }
    }

    private static void AddPost(
        AppDbContext db,
        User author,
        Community community,
        DemoPost spec,
        IReadOnlyList<User> supporters,
        DateTimeOffset now,
        Random random,
        TimeSpan unreadWindow)
    {
        var createdAt = now.AddHours(-spec.HoursAgo);

        var post = new Post
        {
            AuthorId = author.Id,
            CommunityId = community.Id,
            Body = spec.Body,
            IsUrgent = spec.Urgent,
            IsAnonymous = spec.Anonymous,
            Status = spec.Status,
            CreatedAt = createdAt,
            // Kept equal to the number of Acknowledgment rows, as the API guarantees.
            AcknowledgmentCount = supporters.Count,
        };
        db.Posts.Add(post);

        var allowed = AcknowledgmentPolicy.AllowedFor(community.Category, spec.Status);
        for (var i = 0; i < supporters.Count; i++)
        {
            // Spread responses across the window, never into the future.
            var offsetHours = spec.AckSpreadHours * (i + 1) / (supporters.Count + 1);
            var ackedAt = createdAt.AddHours(Math.Min(offsetHours, spec.HoursAgo * 0.95));

            AddAcknowledgment(db, post, author.Id, supporters[i].Id, allowed[random.Next(allowed.Count)], ackedAt,
                isRead: ackedAt < now - unreadWindow);
        }
    }

    /// <summary>An acknowledgment plus the notification the pipeline would have written for it.</summary>
    private static void AddAcknowledgment(
        AppDbContext db, Post post, Guid authorId, Guid userId, AcknowledgmentType type, DateTimeOffset at, bool isRead)
    {
        post.Acknowledgments.Add(new Acknowledgment
        {
            UserId = userId,
            Type = type,
            CreatedAt = at,
        });

        db.Notifications.Add(new Notification
        {
            RecipientUserId = authorId,
            Post = post,
            ActorUserId = userId,
            Type = NotificationType.PostAcknowledged,
            CreatedAt = at,
            IsRead = isRead,
        });
    }
}

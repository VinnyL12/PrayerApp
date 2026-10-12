using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Shared;

public class AcknowledgmentPolicyTests
{
    [Theory]
    [InlineData(PostStatus.Active)]
    [InlineData(PostStatus.Answered)]
    public void Grief_posts_never_offer_celebration(PostStatus status)
    {
        var allowed = AcknowledgmentPolicy.AllowedFor(CommunityCategory.Grief, status);

        Assert.DoesNotContain(AcknowledgmentType.CelebratingWithYou, allowed);
        Assert.DoesNotContain(AcknowledgmentType.RejoicingWithYou, allowed);
        Assert.DoesNotContain(AcknowledgmentType.ThankfulWithYou, allowed);
    }

    [Fact]
    public void Gratitude_posts_offer_celebration_not_support()
    {
        var allowed = AcknowledgmentPolicy.AllowedFor(CommunityCategory.Gratitude, PostStatus.Active);

        Assert.Contains(AcknowledgmentType.CelebratingWithYou, allowed);
        Assert.DoesNotContain(AcknowledgmentType.PrayingForYou, allowed);
    }

    [Fact]
    public void Answered_posts_switch_to_celebration()
    {
        Assert.Equal(AcknowledgmentPolicy.Support, AcknowledgmentPolicy.AllowedFor(CommunityCategory.Health, PostStatus.Active));
        Assert.Equal(AcknowledgmentPolicy.Celebration, AcknowledgmentPolicy.AllowedFor(CommunityCategory.Health, PostStatus.Answered));
    }

    [Fact]
    public void Every_type_belongs_to_at_least_one_set_and_every_combination_offers_something()
    {
        var reachable = AcknowledgmentPolicy.Support.Concat(AcknowledgmentPolicy.Celebration).ToHashSet();
        Assert.All(Enum.GetValues<AcknowledgmentType>(), type => Assert.Contains(type, reachable));

        foreach (var category in Enum.GetValues<CommunityCategory>())
        {
            foreach (var status in Enum.GetValues<PostStatus>())
            {
                Assert.NotEmpty(AcknowledgmentPolicy.AllowedFor(category, status));
            }
        }
    }
}

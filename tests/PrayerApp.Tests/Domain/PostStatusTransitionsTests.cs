using PrayerApp.Domain.Rules;
using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Domain;

public class PostStatusTransitionsTests
{
    [Theory]
    [InlineData(PostStatus.Active, PostStatus.Answered, true)]
    [InlineData(PostStatus.Active, PostStatus.Archived, true)]
    [InlineData(PostStatus.Answered, PostStatus.Archived, true)]
    [InlineData(PostStatus.Answered, PostStatus.Active, false)]
    [InlineData(PostStatus.Archived, PostStatus.Active, false)]
    [InlineData(PostStatus.Archived, PostStatus.Answered, false)]
    [InlineData(PostStatus.Active, (PostStatus)99, false)]
    public void Transitions_follow_the_lifecycle(PostStatus from, PostStatus to, bool allowed)
    {
        Assert.Equal(allowed, PostStatusTransitions.CanTransition(from, to));
    }
}

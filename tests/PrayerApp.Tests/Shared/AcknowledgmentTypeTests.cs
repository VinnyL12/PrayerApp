using PrayerApp.Shared.Enums;

namespace PrayerApp.Tests.Shared;

public class AcknowledgmentTypeTests
{
    [Fact]
    public void Every_defined_type_has_a_label()
    {
        foreach (var type in Enum.GetValues<AcknowledgmentType>())
        {
            Assert.False(string.IsNullOrWhiteSpace(type.ToLabel()));
        }
    }

    [Fact]
    public void Labels_are_unique()
    {
        var labels = Enum.GetValues<AcknowledgmentType>().Select(t => t.ToLabel()).ToList();

        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public void Undefined_value_throws()
    {
        var bogus = (AcknowledgmentType)42;

        Assert.Throws<ArgumentOutOfRangeException>(() => bogus.ToLabel());
    }

    [Fact]
    public void Default_value_is_not_a_valid_type()
    {
        // default(T) for any enum is 0. Starting values at 1 means an
        // uninitialized field can never silently look like "PrayingForYou".
        Assert.False(Enum.IsDefined(default(AcknowledgmentType)));
    }
}

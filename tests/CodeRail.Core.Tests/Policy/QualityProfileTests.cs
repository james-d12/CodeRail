namespace CodeRail.Policy.Tests;

public class QualityProfileTests
{
    [Fact]
    public void DefaultConstruction_ProducesZeroToleranceThresholds()
    {
        var profile = new QualityProfile();

        Assert.Equal(0, profile.Test.AllowFailures);
        Assert.Equal(0, profile.CodeGuard.ErrorCount);
        Assert.Equal(0, profile.CodeGuard.CriticalCount);
        Assert.Null(profile.Coverage.Minimum);
    }
}

using CodeRail.Evidence;

namespace CodeRail.Policy.Tests;

public class GateResultTests
{
    [Fact]
    public void Summary_CountsBlockingFindingsBySeverity()
    {
        var critical = new Finding("a", Severity.Critical, "boom", null, null, null);
        var error = new Finding("b", Severity.Error, "boom", null, null, null);
        var warning = new Finding("c", Severity.Warning, "boom", null, null, null);
        var gate = new GateResult(ValidationStatus.Failed, [], [critical, error, error, warning], DateTimeOffset.UtcNow);

        var summary = gate.Summary;

        Assert.Equal(4, summary.Total);
        Assert.Equal(1, summary.Critical);
        Assert.Equal(2, summary.Error);
        Assert.Equal(1, summary.Warning);
        Assert.Equal(0, summary.Info);
    }

    [Fact]
    public void Summary_IsAllZero_WhenNoBlockingFindings()
    {
        var gate = new GateResult(ValidationStatus.Passed, [], [], DateTimeOffset.UtcNow);

        var summary = gate.Summary;

        Assert.Equal(0, summary.Total);
        Assert.Equal(0, summary.Critical);
        Assert.Equal(0, summary.Error);
        Assert.Equal(0, summary.Warning);
        Assert.Equal(0, summary.Info);
    }
}

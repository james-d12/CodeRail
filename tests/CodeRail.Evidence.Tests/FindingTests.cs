namespace CodeRail.Evidence.Tests;

public class FindingTests
{
    [Fact]
    public void OptionalLocationFields_DefaultToNull()
    {
        var finding = new Finding("architecture", Severity.Critical, "Domain references Infrastructure", null, null, null);

        Assert.Null(finding.File);
        Assert.Null(finding.Line);
        Assert.Null(finding.RuleId);
    }

    [Theory]
    [InlineData(Severity.Info)]
    [InlineData(Severity.Warning)]
    [InlineData(Severity.Error)]
    [InlineData(Severity.Critical)]
    public void Severity_RoundTripsThroughConstruction(Severity severity)
    {
        var finding = new Finding("rule", severity, "message", "File.cs", 1, "RULE-001");

        Assert.Equal(severity, finding.Severity);
    }

    [Fact]
    public void Severity_OrdersFromInfoToCritical()
    {
        Assert.True(Severity.Info < Severity.Warning);
        Assert.True(Severity.Warning < Severity.Error);
        Assert.True(Severity.Error < Severity.Critical);
    }
}

namespace CodeRail.Evidence.Tests;

public class ToolResultTests
{
    [Fact]
    public void RecordsWithSameValues_AreEqual()
    {
        var finding = new Finding("build-error", Severity.Error, "boom", "Foo.cs", 12, null);

        var first = new ToolResult(
            "dotnet-build",
            ValidationStatus.Failed,
            [finding],
            new Dictionary<string, object>(),
            [],
            TimeSpan.FromSeconds(1));

        var second = first with { };

        Assert.Equal(first, second);
    }

    [Fact]
    public void With_ProducesIndependentCopyWithOverriddenStatus()
    {
        var passed = new ToolResult(
            "dotnet-test",
            ValidationStatus.Passed,
            [],
            new Dictionary<string, object> { ["total"] = 10 },
            [],
            TimeSpan.FromSeconds(2));

        var failed = passed with { Status = ValidationStatus.Failed };

        Assert.Equal(ValidationStatus.Passed, passed.Status);
        Assert.Equal(ValidationStatus.Failed, failed.Status);
        Assert.Equal(passed.Metrics, failed.Metrics);
    }

    [Fact]
    public void Metrics_CanCarryHeterogeneousValueTypes()
    {
        var result = new ToolResult(
            "coverage",
            ValidationStatus.Passed,
            [],
            new Dictionary<string, object> { ["lineCoverage"] = 91.2, ["branchCoverage"] = 84.3 },
            [],
            TimeSpan.Zero);

        Assert.Equal(91.2, result.Metrics["lineCoverage"]);
        Assert.Equal(84.3, result.Metrics["branchCoverage"]);
    }
}

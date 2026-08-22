using CodeRail.Evidence;

namespace CodeRail.Policy.Tests;

public class PolicyEvaluatorTests
{
    private static readonly QualityProfile DefaultProfile = new()
    {
        Test = new TestQualityThresholds { AllowFailures = 0 },
        CodeGuard = new CodeGuardQualityThresholds { ErrorCount = 0, CriticalCount = 0 },
        Coverage = new CoverageQualityThresholds { Minimum = null }
    };

    private static ToolResult Passing(string tool) =>
        new(tool, ValidationStatus.Passed, [], new Dictionary<string, object>(), [], TimeSpan.Zero);

    [Fact]
    public void Evaluate_ReturnsPassed_WhenEveryToolPassesAndNoThresholdsBreached()
    {
        var results = new[] { Passing(ToolIds.DotnetBuild), Passing(ToolIds.DotnetTest), Passing(ToolIds.CodeGuard) };

        var gate = new PolicyEvaluator().Evaluate(results, DefaultProfile);

        Assert.Equal(ValidationStatus.Passed, gate.Status);
        Assert.Empty(gate.BlockingFindings);
        Assert.Same(results, gate.Tools);
    }

    [Fact]
    public void Evaluate_TreatsAnyBuildFailure_AsUnconditionallyBlocking()
    {
        var buildFinding = new Finding("build-failure", Severity.Error, "CS0103", "Foo.cs", 12, null);
        var build = new ToolResult(ToolIds.DotnetBuild, ValidationStatus.Failed, [buildFinding], new Dictionary<string, object>(), [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([build], DefaultProfile);

        Assert.Equal(ValidationStatus.Failed, gate.Status);
        Assert.Equal([buildFinding], gate.BlockingFindings);
    }

    [Fact]
    public void Evaluate_ToleratesFailingTests_UpToAllowFailuresThreshold()
    {
        var profile = new QualityProfile { Test = new TestQualityThresholds { AllowFailures = 2 } };
        var test = new ToolResult(
            ToolIds.DotnetTest, ValidationStatus.Failed,
            [new Finding("test-failure", Severity.Error, "flaky", null, null, null)],
            new Dictionary<string, object> { ["failed"] = 2 }, [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([test], profile);

        Assert.Equal(ValidationStatus.Passed, gate.Status);
        Assert.Empty(gate.BlockingFindings);
    }

    [Fact]
    public void Evaluate_BlocksFailingTests_BeyondAllowFailuresThreshold()
    {
        var finding = new Finding("test-failure", Severity.Error, "boom", null, null, null);
        var test = new ToolResult(
            ToolIds.DotnetTest, ValidationStatus.Failed, [finding],
            new Dictionary<string, object> { ["failed"] = 1 }, [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([test], DefaultProfile);

        Assert.Equal(ValidationStatus.Failed, gate.Status);
        Assert.Equal([finding], gate.BlockingFindings);
    }

    [Fact]
    public void Evaluate_BlocksOnlyErrorAndCriticalCodeGuardFindings_NotInfoOrWarning()
    {
        var info = new Finding("style", Severity.Info, "nit", null, null, "STYLE-001");
        var error = new Finding("architecture", Severity.Error, "layering violation", null, null, "ARCH-001");
        var codeGuard = new ToolResult(
            ToolIds.CodeGuard, ValidationStatus.Failed, [info, error], new Dictionary<string, object>(), [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([codeGuard], DefaultProfile);

        Assert.Equal(ValidationStatus.Failed, gate.Status);
        Assert.Equal([error], gate.BlockingFindings);
    }

    [Fact]
    public void Evaluate_ToleratesCodeGuardErrors_UpToConfiguredThreshold()
    {
        var profile = new QualityProfile { CodeGuard = new CodeGuardQualityThresholds { ErrorCount = 1 } };
        var error = new Finding("architecture", Severity.Error, "layering violation", null, null, "ARCH-001");
        var codeGuard = new ToolResult(
            ToolIds.CodeGuard, ValidationStatus.Failed, [error], new Dictionary<string, object>(), [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([codeGuard], profile);

        Assert.Equal(ValidationStatus.Passed, gate.Status);
    }

    [Fact]
    public void Evaluate_SynthesizesBlockingFinding_WhenCoverageBelowMinimum()
    {
        var profile = new QualityProfile { Coverage = new CoverageQualityThresholds { Minimum = 80 } };
        var coverage = new ToolResult(
            ToolIds.Coverage, ValidationStatus.Passed, [], new Dictionary<string, object> { ["lineCoverage"] = 61.5 }, [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([coverage], profile);

        Assert.Equal(ValidationStatus.Failed, gate.Status);
        var finding = Assert.Single(gate.BlockingFindings);
        Assert.Equal("coverage-below-threshold", finding.Type);
        Assert.Contains("61.5", finding.Message);
        Assert.Contains("80", finding.Message);
    }

    [Fact]
    public void Evaluate_IgnoresCoverageNumber_WhenNoMinimumConfigured()
    {
        var coverage = new ToolResult(
            ToolIds.Coverage, ValidationStatus.Passed, [], new Dictionary<string, object> { ["lineCoverage"] = 1.0 }, [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([coverage], DefaultProfile);

        Assert.Equal(ValidationStatus.Passed, gate.Status);
    }

    [Fact]
    public void Evaluate_FallsBackToAnyFailureBlocks_ForUnrecognizedToolIds()
    {
        var finding = new Finding("mutation-survived", Severity.Warning, "survived mutant", "Foo.cs", 5, null);
        var stryker = new ToolResult("stryker", ValidationStatus.Failed, [finding], new Dictionary<string, object>(), [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([stryker], DefaultProfile);

        Assert.Equal(ValidationStatus.Failed, gate.Status);
        Assert.Equal([finding], gate.BlockingFindings);
    }

    [Fact]
    public void Evaluate_ReturnsPartiallyEvaluated_WhenNoBlockingFindingsButATooReportedPartial()
    {
        var partial = new ToolResult(ToolIds.CodeGuard, ValidationStatus.PartiallyEvaluated, [], new Dictionary<string, object>(), [], TimeSpan.Zero);

        var gate = new PolicyEvaluator().Evaluate([partial], DefaultProfile);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, gate.Status);
    }

    [Fact]
    public void Evaluate_UsesInjectedTimeProvider_ForEvaluatedAtUtc()
    {
        var fixedTime = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var evaluator = new PolicyEvaluator(new FixedTimeProvider(fixedTime));

        var gate = evaluator.Evaluate([], DefaultProfile);

        Assert.Equal(fixedTime, gate.EvaluatedAtUtc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

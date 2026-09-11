using System.Text.RegularExpressions;
using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Tooling.Tests.Executors;

public class CoverageExecutorTests
{
    private static readonly ToolContext Context = new("/repo", []);

    [Fact]
    public async Task ExecuteAsync_ReportsLineAndBranchCoverage_WhenACoberturaReportIsProduced()
    {
        var runner = new StubProcessRunner((_, arguments, _) =>
        {
            WriteFakeCoberturaReport(arguments, linesCovered: 80, linesValid: 100, branchesCovered: 10, branchesValid: 20);
            return new ProcessResult(0, "", "", TimeSpan.FromMilliseconds(5), false);
        });
        var executor = new CoverageExecutor(runner, NullLogger<CoverageExecutor>.Instance);

        var result = await executor.ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal("coverage", result.Tool);
        Assert.Equal(ValidationStatus.Passed, result.Status);
        Assert.Equal(80.0, result.Metrics["lineCoverage"]);
        Assert.Equal(50.0, result.Metrics["branchCoverage"]);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsNewCodeLineCoverage_RestrictedToChangedFiles_WhenAChangeSetIsProvided()
    {
        var runner = new StubProcessRunner((_, arguments, _) =>
        {
            WriteFakeCoberturaReportWithPerFileBreakdown(arguments);
            return new ProcessResult(0, "", "", TimeSpan.FromMilliseconds(5), false);
        });
        var executor = new CoverageExecutor(runner, NullLogger<CoverageExecutor>.Instance);
        var changes = new ChangeSet(["Lib/Changed.cs"], []);
        var context = new ToolContext("/repo", [], changes);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        // Whole-repo coverage still comes from the root aggregate attributes...
        Assert.Equal(73.33, result.Metrics["lineCoverage"]);
        // ...but the new-code metric is restricted to Lib/Changed.cs alone (2 of 5 lines covered),
        // ignoring the untouched Lib/Other.cs (9 of 10 lines covered).
        Assert.Equal(40.0, result.Metrics["newCodeLineCoverage"]);
    }

    [Fact]
    public async Task ExecuteAsync_OmitsNewCodeLineCoverage_WhenChangedFilesHaveNoMatchingCoverageData()
    {
        var runner = new StubProcessRunner((_, arguments, _) =>
        {
            WriteFakeCoberturaReportWithPerFileBreakdown(arguments);
            return new ProcessResult(0, "", "", TimeSpan.FromMilliseconds(5), false);
        });
        var executor = new CoverageExecutor(runner, NullLogger<CoverageExecutor>.Instance);
        // Changed a file that isn't part of the coverage report at all (e.g. non-code, or a
        // project without coverlet.collector) - there's nothing to divide, so no metric.
        var changes = new ChangeSet(["README.md"], []);
        var context = new ToolContext("/repo", [], changes);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Metrics.ContainsKey("newCodeLineCoverage"));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenNoCoberturaReportIsProduced()
    {
        // Simulates the common case of the target's test projects not referencing
        // coverlet.collector - `dotnet test --collect` runs but silently emits nothing.
        var runner = StubProcessRunner.Returning(new ProcessResult(0, "", "", TimeSpan.FromMilliseconds(5), false));
        var executor = new CoverageExecutor(runner, NullLogger<CoverageExecutor>.Instance);

        var result = await executor.ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        Assert.Empty(result.Metrics);
        var finding = Assert.Single(result.Findings);
        Assert.Contains("coverlet.collector", finding.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ReusesAnEarlierBuild_OnlyWhenTheContextSaysOneAlreadyRan(bool skipBuild)
    {
        var runner = StubProcessRunner.Returning(new ProcessResult(0, "", "", TimeSpan.FromMilliseconds(5), false));
        var executor = new CoverageExecutor(runner, NullLogger<CoverageExecutor>.Instance);

        await executor.ExecuteAsync(Context with { SkipBuild = skipBuild }, CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("dotnet", call.Executable);
        if (skipBuild)
        {
            // --no-build implies --no-restore, so one flag covers both.
            Assert.EndsWith(" --no-build", call.Arguments);
            Assert.DoesNotContain("--no-restore", call.Arguments);
        }
        else
        {
            Assert.DoesNotContain("--no-build", call.Arguments);
        }
    }

    private static void WriteFakeCoberturaReport(string arguments, int linesCovered, int linesValid, int branchesCovered, int branchesValid)
    {
        var reportDirectory = FakeReportDirectory(arguments);
        File.WriteAllText(
            Path.Combine(reportDirectory, "coverage.cobertura.xml"),
            $"""<coverage lines-covered="{linesCovered}" lines-valid="{linesValid}" branches-covered="{branchesCovered}" branches-valid="{branchesValid}"></coverage>""");
    }

    /// <summary>Writes a report with both root-level aggregate attributes (11 of 15 lines, ~73.33%)
    /// and a per-file breakdown: <c>Lib/Changed.cs</c> (2 of 5 lines) and <c>Lib/Other.cs</c>
    /// (9 of 10 lines).</summary>
    private static void WriteFakeCoberturaReportWithPerFileBreakdown(string arguments)
    {
        var reportDirectory = FakeReportDirectory(arguments);
        File.WriteAllText(
            Path.Combine(reportDirectory, "coverage.cobertura.xml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <coverage lines-covered="11" lines-valid="15" branches-covered="0" branches-valid="0">
              <packages>
                <package name="Lib">
                  <classes>
                    <class name="Lib.Changed" filename="Lib/Changed.cs">
                      <lines>
                        <line number="1" hits="1" />
                        <line number="2" hits="1" />
                        <line number="3" hits="0" />
                        <line number="4" hits="0" />
                        <line number="5" hits="0" />
                      </lines>
                    </class>
                    <class name="Lib.Other" filename="Lib/Other.cs">
                      <lines>
                        <line number="1" hits="1" />
                        <line number="2" hits="1" />
                        <line number="3" hits="1" />
                        <line number="4" hits="1" />
                        <line number="5" hits="1" />
                        <line number="6" hits="1" />
                        <line number="7" hits="1" />
                        <line number="8" hits="1" />
                        <line number="9" hits="1" />
                        <line number="10" hits="0" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """);
    }

    private static string FakeReportDirectory(string arguments)
    {
        var match = Regex.Match(arguments, "--results-directory \"([^\"]+)\"");
        Assert.True(match.Success, $"Expected --results-directory in arguments: {arguments}");
        return Directory.CreateDirectory(Path.Combine(match.Groups[1].Value, "guid-subdir")).FullName;
    }
}

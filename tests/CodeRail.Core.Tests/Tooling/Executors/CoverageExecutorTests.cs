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

    private static void WriteFakeCoberturaReport(string arguments, int linesCovered, int linesValid, int branchesCovered, int branchesValid)
    {
        var match = Regex.Match(arguments, "--results-directory \"([^\"]+)\"");
        Assert.True(match.Success, $"Expected --results-directory in arguments: {arguments}");

        var reportDirectory = Directory.CreateDirectory(Path.Combine(match.Groups[1].Value, "guid-subdir"));
        File.WriteAllText(
            Path.Combine(reportDirectory.FullName, "coverage.cobertura.xml"),
            $"""<coverage lines-covered="{linesCovered}" lines-valid="{linesValid}" branches-covered="{branchesCovered}" branches-valid="{branchesValid}"></coverage>""");
    }
}

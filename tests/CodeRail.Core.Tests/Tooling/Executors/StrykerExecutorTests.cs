using System.ComponentModel;
using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Tooling.Tests.Executors;

public class StrykerExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsMutationScoreAndSurvivedFindings_WhenAReportIsProduced()
    {
        var repoRoot = Directory.CreateTempSubdirectory("coderail-stryker-").FullName;
        try
        {
            var runner = new StubProcessRunner((_, _, workingDirectory) =>
            {
                WriteFakeMutationReport(workingDirectory);
                return new ProcessResult(0, "", "", TimeSpan.FromMilliseconds(5), false);
            });
            var executor = new StrykerExecutor(runner, NullLogger<StrykerExecutor>.Instance);
            var context = new ToolContext(repoRoot, []);

            var result = await executor.ExecuteAsync(context, CancellationToken.None);

            Assert.Equal("stryker", result.Tool);
            Assert.Equal(ValidationStatus.Passed, result.Status);
            Assert.Equal(50.0, result.Metrics["mutationScore"]);
            Assert.Equal(1, result.Metrics["killed"]);
            Assert.Equal(1, result.Metrics["survived"]);
            var finding = Assert.Single(result.Findings);
            Assert.Equal("mutation-survived", finding.Type);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenNoMutationReportIsProduced()
    {
        // Simulates `dotnet-stryker` not being installed - the process still runs (it's `dotnet`
        // itself that's found), it just errors out before writing anything to StrykerOutput.
        var repoRoot = Directory.CreateTempSubdirectory("coderail-stryker-").FullName;
        try
        {
            var runner = StubProcessRunner.Returning(new ProcessResult(1, "", "No executable found matching command \"dotnet-stryker\"", TimeSpan.FromMilliseconds(5), false));
            var executor = new StrykerExecutor(runner, NullLogger<StrykerExecutor>.Instance);
            var context = new ToolContext(repoRoot, []);

            var result = await executor.ExecuteAsync(context, CancellationToken.None);

            Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
            Assert.Empty(result.Metrics);
            var finding = Assert.Single(result.Findings);
            Assert.Contains("dotnet-stryker", finding.Message);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenDotnetCannotBeStarted()
    {
        var runner = StubProcessRunner.Throwing(new Win32Exception("not found"));
        var executor = new StrykerExecutor(runner, NullLogger<StrykerExecutor>.Instance);
        var context = new ToolContext("/repo", []);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("stryker-unavailable", finding.Type);
    }

    [Fact]
    public async Task ExecuteAsync_RunsFromTheRepoRoot_WithoutASolutionFlag_WhenNoExplicitSolutionsAreGiven()
    {
        var runner = StubProcessRunner.Returning(new ProcessResult(1, "", "", TimeSpan.Zero, false));
        var executor = new StrykerExecutor(runner, NullLogger<StrykerExecutor>.Instance);
        var context = new ToolContext("/repo", []);

        await executor.ExecuteAsync(context, CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("dotnet", call.Executable);
        Assert.DoesNotContain("--solution", call.Arguments);
        Assert.Equal("/repo", call.WorkingDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_UsesTheSolutionFlagAndItsOwnDirectory_ForEachExplicitSolutionPath()
    {
        var runner = StubProcessRunner.Returning(new ProcessResult(1, "", "", TimeSpan.Zero, false));
        var executor = new StrykerExecutor(runner, NullLogger<StrykerExecutor>.Instance);
        var solutionPath = Path.Combine("/repo", "src", "MySolution.sln");
        var context = new ToolContext("/repo", [solutionPath]);

        await executor.ExecuteAsync(context, CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Contains($"--solution \"{solutionPath}\"", call.Arguments);
        Assert.Equal(Path.Combine("/repo", "src"), call.WorkingDirectory);
    }

    private static void WriteFakeMutationReport(string workingDirectory)
    {
        var reportDirectory = Directory.CreateDirectory(Path.Combine(workingDirectory, "StrykerOutput", "2026-01-01", "reports"));
        File.WriteAllText(
            Path.Combine(reportDirectory.FullName, "mutation-report.json"),
            """
            {
              "files": {
                "Calculator.cs": {
                  "mutants": [
                    { "id": "1", "mutatorName": "Arithmetic", "status": "Killed", "location": { "start": { "line": 1 }, "end": { "line": 1 } } },
                    { "id": "2", "mutatorName": "Boolean", "status": "Survived", "location": { "start": { "line": 4 }, "end": { "line": 4 } } }
                  ]
                }
              }
            }
            """);
    }
}

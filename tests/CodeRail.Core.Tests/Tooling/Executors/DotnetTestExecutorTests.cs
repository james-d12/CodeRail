using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Tooling.Tests.Executors;

public class DotnetTestExecutorTests
{
    private readonly DotnetTestExecutor _executor = new(
        new ProcessRunner(NullLogger<ProcessRunner>.Instance), NullLogger<DotnetTestExecutor>.Instance);

    [Fact]
    public async Task ExecuteAsync_ReportsPassed_WhenAllTestsPass()
    {
        var projectPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PassingProject", "PassingProject.csproj");
        var context = new ToolContext(AppContext.BaseDirectory, [projectPath]);

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("dotnet-test", result.Tool);
        Assert.Equal(ValidationStatus.Passed, result.Status);
        Assert.Equal(1, result.Metrics["total"]);
        Assert.Equal(1, result.Metrics["passed"]);
        Assert.Equal(0, result.Metrics["failed"]);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFailed_WithOneFindingPerFailingTest()
    {
        var projectPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "FailingTestsProject", "FailingTestsProject.csproj");
        var context = new ToolContext(AppContext.BaseDirectory, [projectPath]);

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Failed, result.Status);
        Assert.Equal(2, result.Metrics["total"]);
        Assert.Equal(1, result.Metrics["passed"]);
        Assert.Equal(1, result.Metrics["failed"]);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("test-failure", finding.Type);
        Assert.Contains("AlwaysFails", finding.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsPassed_WithInfoFinding_WhenTargetHasNoTestProjects()
    {
        var projectPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NoTestsProject", "NoTestsProject.csproj");
        var context = new ToolContext(AppContext.BaseDirectory, [projectPath]);

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Passed, result.Status);
        Assert.Equal(0, result.Metrics["total"]);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("no-tests-found", finding.Type);
        Assert.Equal(Severity.Info, finding.Severity);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFailed_WhenTargetFailsToBuild()
    {
        var projectPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrokenTestProject", "BrokenTestProject.csproj");
        var context = new ToolContext(AppContext.BaseDirectory, [projectPath]);

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Failed, result.Status);
        Assert.Equal(0, result.Metrics["total"]);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("test-run-failure", finding.Type);
        Assert.Equal(Severity.Error, finding.Severity);
    }
}

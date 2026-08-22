using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Tooling.Tests.Executors;

public class DotnetBuildExecutorTests
{
    private readonly DotnetBuildExecutor _executor = new(
        new ProcessRunner(NullLogger<ProcessRunner>.Instance), NullLogger<DotnetBuildExecutor>.Instance);

    [Fact]
    public async Task ExecuteAsync_ReportsPassed_ForBuildableProject()
    {
        var projectPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PassingProject", "PassingProject.csproj");
        var context = new ToolContext(AppContext.BaseDirectory, [projectPath]);

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("dotnet-build", result.Tool);
        Assert.Equal(ValidationStatus.Passed, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsFailed_ForBrokenProject()
    {
        var projectPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BrokenBuildProject", "BrokenBuildProject.csproj");
        var context = new ToolContext(AppContext.BaseDirectory, [projectPath]);

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Failed, result.Status);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Equal("build-failure", finding.Type);
    }
}

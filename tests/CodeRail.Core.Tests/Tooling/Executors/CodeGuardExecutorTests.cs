using System.ComponentModel;
using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Tooling.Tests.Executors;

public class CodeGuardExecutorTests
{
    private static readonly ToolContext Context = new("/repo", []);

    [Fact]
    public async Task ExecuteAsync_ParsesPassingCodeGuardJson()
    {
        const string json = """{ "status": "passed", "rulesEvaluated": 10, "rulesPassed": 10, "rulesFailed": 0, "rulesErrored": 0 }""";
        var runner = StubProcessRunner.Returning(new ProcessResult(0, json, "", TimeSpan.FromMilliseconds(5), false));
        var executor = new CodeGuardExecutor(runner, NullLogger<CodeGuardExecutor>.Instance);

        var result = await executor.ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal("codeguard", result.Tool);
        Assert.Equal(ValidationStatus.Passed, result.Status);
        Assert.Equal(10, result.Metrics["rulesEvaluated"]);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_ParsesFailingCodeGuardJson_IntoFindings()
    {
        const string json = """
            {
              "status": "failed",
              "rulesEvaluated": 5,
              "rulesPassed": 4,
              "rulesFailed": 1,
              "rulesErrored": 0,
              "violations": [
                { "ruleId": "ARCH-001", "severity": "critical", "message": "layering violation", "file": "Foo.cs", "line": 1 }
              ]
            }
            """;
        var runner = StubProcessRunner.Returning(new ProcessResult(1, json, "", TimeSpan.FromMilliseconds(5), false));
        var executor = new CodeGuardExecutor(runner, NullLogger<CodeGuardExecutor>.Instance);

        var result = await executor.ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal(ValidationStatus.Failed, result.Status);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("ARCH-001", finding.RuleId);
    }

    [Fact]
    public async Task ExecuteAsync_PassesEachResolvedSolutionAsARepeatedSolutionFlag()
    {
        const string json = """{ "status": "passed", "rulesEvaluated": 1, "rulesPassed": 1, "rulesFailed": 0, "rulesErrored": 0 }""";
        var runner = StubProcessRunner.Returning(new ProcessResult(0, json, "", TimeSpan.FromMilliseconds(5), false));
        var executor = new CodeGuardExecutor(runner, NullLogger<CodeGuardExecutor>.Instance);
        var context = new ToolContext("/repo", ["/repo/A.sln", "/repo/B.sln"]);

        await executor.ExecuteAsync(context, CancellationToken.None);

        var arguments = Assert.Single(runner.Calls).Arguments;
        Assert.Contains("--solution \"/repo/A.sln\"", arguments);
        Assert.Contains("--solution \"/repo/B.sln\"", arguments);
    }

    [Fact]
    public async Task ExecuteAsync_OmitsSolutionFlag_WhenNoSolutionsWereResolved()
    {
        const string json = """{ "status": "passed", "rulesEvaluated": 1, "rulesPassed": 1, "rulesFailed": 0, "rulesErrored": 0 }""";
        var runner = StubProcessRunner.Returning(new ProcessResult(0, json, "", TimeSpan.FromMilliseconds(5), false));
        var executor = new CodeGuardExecutor(runner, NullLogger<CodeGuardExecutor>.Instance);

        await executor.ExecuteAsync(Context, CancellationToken.None);

        Assert.DoesNotContain("--solution", Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenCodeGuardIsNotInstalled()
    {
        var runner = StubProcessRunner.Throwing(new Win32Exception("No such file or directory"));
        var executor = new CodeGuardExecutor(runner, NullLogger<CodeGuardExecutor>.Instance);

        var result = await executor.ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        var finding = Assert.Single(result.Findings);
        Assert.Contains("PATH", finding.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenOutputIsEmpty()
    {
        var runner = StubProcessRunner.Returning(new ProcessResult(1, "", "codeguard: some internal error", TimeSpan.FromMilliseconds(5), false));
        var executor = new CodeGuardExecutor(runner, NullLogger<CodeGuardExecutor>.Instance);

        var result = await executor.ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        Assert.Contains("some internal error", result.Findings[0].Message);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenOutputIsNotValidJson()
    {
        var runner = StubProcessRunner.Returning(new ProcessResult(0, "not json at all", "", TimeSpan.FromMilliseconds(5), false));
        var executor = new CodeGuardExecutor(runner, NullLogger<CodeGuardExecutor>.Instance);

        var result = await executor.ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        Assert.Contains("not json at all", result.Findings[0].Message);
    }
}

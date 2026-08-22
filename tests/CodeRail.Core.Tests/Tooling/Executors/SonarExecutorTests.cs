using System.ComponentModel;
using System.Net;
using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Tooling.Tests.Executors;

public class SonarExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenNoSonarConfigIsSet()
    {
        var executor = new SonarExecutor(
            StubProcessRunner.Returning(new ProcessResult(0, "", "", TimeSpan.Zero, false)),
            new HttpClient(FakeHttpMessageHandler.Json(_ => "{}")),
            NullLogger<SonarExecutor>.Instance,
            () => "token");
        var context = new ToolContext("/repo", []);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("sonar-unavailable", finding.Type);
        Assert.Contains("projectKey", finding.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenNoTokenIsConfigured()
    {
        var executor = new SonarExecutor(
            StubProcessRunner.Returning(new ProcessResult(0, "", "", TimeSpan.Zero, false)),
            new HttpClient(FakeHttpMessageHandler.Json(_ => "{}")),
            NullLogger<SonarExecutor>.Instance,
            () => null);
        var context = new ToolContext("/repo", [], Sonar: new SonarConfig("my-project", null, null));

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        Assert.Contains("CODERAIL_SONAR_TOKEN", Assert.Single(result.Findings).Message);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenDotnetCannotBeStarted()
    {
        var executor = new SonarExecutor(
            StubProcessRunner.Throwing(new Win32Exception("not found")),
            new HttpClient(FakeHttpMessageHandler.Json(_ => "{}")),
            NullLogger<SonarExecutor>.Instance,
            () => "token");
        var context = new ToolContext("/repo", [], Sonar: new SonarConfig("my-project", null, null));

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        Assert.Equal("sonar-unavailable", Assert.Single(result.Findings).Type);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenBeginFails()
    {
        var runner = new StubProcessRunner((_, arguments, _) => arguments.Contains("sonarscanner begin")
            ? new ProcessResult(1, "", "invalid token", TimeSpan.Zero, false)
            : new ProcessResult(0, "", "", TimeSpan.Zero, false));
        var executor = new SonarExecutor(
            runner, new HttpClient(FakeHttpMessageHandler.Json(_ => "{}")), NullLogger<SonarExecutor>.Instance, () => "token");
        var context = new ToolContext("/repo", [], Sonar: new SonarConfig("my-project", null, null));

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
        Assert.Contains("begin", Assert.Single(result.Findings).Message);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenNoReportTaskFileIsProduced()
    {
        var repoRoot = Directory.CreateTempSubdirectory("coderail-sonar-").FullName;
        try
        {
            var runner = StubProcessRunner.Returning(new ProcessResult(0, "", "", TimeSpan.Zero, false));
            var executor = new SonarExecutor(
                runner, new HttpClient(FakeHttpMessageHandler.Json(_ => "{}")), NullLogger<SonarExecutor>.Instance, () => "token");
            var context = new ToolContext(repoRoot, [], Sonar: new SonarConfig("my-project", null, null));

            var result = await executor.ExecuteAsync(context, CancellationToken.None);

            Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
            Assert.Contains("report-task.txt", Assert.Single(result.Findings).Message);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsPartiallyEvaluated_WhenTheBackgroundTaskDoesNotSucceed()
    {
        var repoRoot = Directory.CreateTempSubdirectory("coderail-sonar-").FullName;
        try
        {
            var runner = new StubProcessRunner((_, arguments, workingDirectory) =>
            {
                if (arguments.Contains("sonarscanner end"))
                {
                    WriteReportTask(workingDirectory, "abc123", "https://sonarcloud.io");
                }

                return new ProcessResult(0, "", "", TimeSpan.Zero, false);
            });
            var handler = FakeHttpMessageHandler.Json(_ => """{"task":{"status":"FAILED"}}""");
            var executor = new SonarExecutor(runner, new HttpClient(handler), NullLogger<SonarExecutor>.Instance, () => "token");
            var context = new ToolContext(repoRoot, [], Sonar: new SonarConfig("my-project", null, null));

            var result = await executor.ExecuteAsync(context, CancellationToken.None);

            Assert.Equal(ValidationStatus.PartiallyEvaluated, result.Status);
            Assert.Contains("FAILED", Assert.Single(result.Findings).Message);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReportsNewIssues_OnASuccessfulRun()
    {
        var repoRoot = Directory.CreateTempSubdirectory("coderail-sonar-").FullName;
        try
        {
            var runner = new StubProcessRunner((_, arguments, workingDirectory) =>
            {
                if (arguments.Contains("sonarscanner end"))
                {
                    WriteReportTask(workingDirectory, "abc123", "https://sonarcloud.io");
                }

                return new ProcessResult(0, "", "", TimeSpan.Zero, false);
            });
            var handler = FakeHttpMessageHandler.Json(request => request.RequestUri!.ToString().Contains("/api/ce/task")
                ? """{"task":{"status":"SUCCESS"}}"""
                : """
                  {
                    "issues": [
                      { "rule": "S1", "severity": "BLOCKER", "component": "my-project:Foo.cs", "message": "boom", "line": 3 },
                      { "rule": "S2", "severity": "MAJOR", "component": "my-project:Bar.cs", "message": "nit", "line": 8 }
                    ]
                  }
                  """);
            var executor = new SonarExecutor(runner, new HttpClient(handler), NullLogger<SonarExecutor>.Instance, () => "s3cr3t");
            var context = new ToolContext(repoRoot, [], Sonar: new SonarConfig("my-project", "my-org", null));

            var result = await executor.ExecuteAsync(context, CancellationToken.None);

            Assert.Equal(ToolIds.Sonar, result.Tool);
            Assert.Equal(ValidationStatus.Passed, result.Status);
            Assert.Equal(2, result.Metrics["newIssueCount"]);
            Assert.Equal(1, result.Metrics["newBlockerCount"]);
            Assert.Equal(0, result.Metrics["newCriticalCount"]);
            Assert.Contains(result.Findings, f => f.File == "Foo.cs" && f.Severity == Severity.Critical);

            // begin/build/test/end - four process invocations, none of them carrying the raw
            // token in the logged argument string (docs §21 - "never expose environment
            // variables indiscriminately" / "protect secrets").
            Assert.Equal(4, runner.Calls.Count);
            Assert.All(runner.Calls, call => Assert.DoesNotContain("s3cr3t", call.Arguments));
            Assert.Contains(runner.Calls, call => call.Arguments.Contains("/k:\"my-project\"") && call.Arguments.Contains("/o:\"my-org\""));

            // Nor in any HTTP request URI.
            Assert.All(handler.Requests, r => Assert.DoesNotContain("s3cr3t", r.RequestUri!.ToString()));
            Assert.All(handler.Requests, r => Assert.Equal("Bearer", r.Headers.Authorization?.Scheme));
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    private static void WriteReportTask(string workingDirectory, string ceTaskId, string serverUrl)
    {
        var directory = Directory.CreateDirectory(Path.Combine(workingDirectory, ".sonarqube", "out", ".sonar"));
        File.WriteAllText(
            Path.Combine(directory.FullName, "report-task.txt"),
            $"""
            projectKey=my-project
            serverUrl={serverUrl}
            ceTaskId={ceTaskId}
            ceTaskUrl={serverUrl}/api/ce/task?id={ceTaskId}
            """);
    }
}

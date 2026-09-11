using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Parsing;
using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling.Executors;

/// <summary>
/// Runs the SonarCloud/SonarQube analysis lifecycle (<c>docs/HIGH_LEVEL_PLAN.md</c> §9.6): begin →
/// build → test → end → wait for the server's background processing → fetch new-code-period issues
/// from the Sonar Web API. Unlike every other executor, the final step isn't a CLI invocation
/// through <see cref="IProcessRunner"/> - it's an HTTP call, so this is the one executor that also
/// takes an <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// <b>Best-effort, unverified against a live server.</b> The <c>begin</c>/<c>end</c> CLI shape is
/// taken directly from this repo's own working CI invocation (<c>.github/workflows/ci.yml</c>).
/// The <c>report-task.txt</c> location/format and the Sonar Web API shapes
/// (<c>api/ce/task</c>/<c>api/issues/search</c>) are long-stable, well-documented Sonar
/// conventions, but - unlike <c>StrykerExecutor</c>, which was checked against a real installed
/// <c>dotnet-stryker</c> - there was no live Sonar server available to verify this against while
/// writing it. Treat the exact polling/parsing details as the first thing to check if this
/// misbehaves against a real server.
/// <para/>
/// Degrades to <see cref="ValidationStatus.PartiallyEvaluated"/>, matching
/// <c>CodeGuardExecutor</c>, when <see cref="ToolContext.Sonar"/> is unset (no
/// <c>quality.sonar.projectKey</c> configured) or <c>CODERAIL_SONAR_TOKEN</c> isn't set - an
/// environment/configuration gap shouldn't block the repair loop the same way an actual code
/// defect should. The token is deliberately never a CLI flag or profile YAML value (docs §21,
/// "protect secrets") - it's read from the environment and passed to the scanner via
/// <c>SONAR_TOKEN</c> in <see cref="IProcessRunner"/>'s <c>environmentVariables</c> parameter,
/// never interpolated into a logged command-line argument string.
/// <para/>
/// Its own <c>build</c> must stay a real build - the scanner only collects analysis data from a
/// compile that happens between <c>begin</c> and <c>end</c>, so it never takes
/// <c>--no-build</c> and ignores <see cref="ToolContext.SkipBuild"/>. The <c>test</c> call right
/// after it always takes <c>--no-build</c> though: it runs immediately after that build, inside
/// this same <c>ExecuteAsync</c>, so there is never anything left to compile.
/// </remarks>
public sealed class SonarExecutor(
    IProcessRunner processRunner,
    HttpClient httpClient,
    ILogger<SonarExecutor> logger,
    Func<string?>? tokenProvider = null) : IToolExecutor
{
    private const string DefaultHostUrl = "https://sonarcloud.io";
    private static readonly TimeSpan ScannerTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CeTaskPollInterval = TimeSpan.FromSeconds(2);
    private const int MaxCeTaskPollAttempts = 60; // ~2 minutes of polling before giving up.

    private readonly Func<string?> _tokenProvider = tokenProvider ?? (() => Environment.GetEnvironmentVariable("CODERAIL_SONAR_TOKEN"));

    public string Name => ValidationSteps.Sonar;

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Sonar is not { } sonar)
        {
            return Unavailable(stopwatch.Elapsed,
                "Sonar analysis skipped: no 'quality.sonar.projectKey' configured in the validation profile.");
        }

        var token = _tokenProvider();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Unavailable(stopwatch.Elapsed, "Sonar analysis skipped: set the CODERAIL_SONAR_TOKEN environment variable.");
        }

        var hostUrl = (sonar.HostUrl ?? DefaultHostUrl).TrimEnd('/');
        var tokenEnv = new Dictionary<string, string> { ["SONAR_TOKEN"] = token };

        var beginArgs = $"sonarscanner begin /k:\"{sonar.ProjectKey}\"" +
            (sonar.Organization is { } org ? $" /o:\"{org}\"" : "") +
            (sonar.HostUrl is { } explicitHost ? $" /d:sonar.host.url=\"{explicitHost}\"" : "") +
            " /d:sonar.qualitygate.wait=false";

        try
        {
            var begin = await processRunner.RunAsync(
                "dotnet", beginArgs, context.RepoRoot, environmentVariables: tokenEnv, timeout: ScannerTimeout, cancellationToken: cancellationToken);
            if (begin.ExitCode != 0)
            {
                return Unavailable(stopwatch.Elapsed, $"'dotnet sonarscanner begin' failed:\n{OutputTail.Last(begin.StandardOutput + begin.StandardError)}");
            }

            var build = await processRunner.RunAsync("dotnet", "build", context.RepoRoot, timeout: ScannerTimeout, cancellationToken: cancellationToken);
            var test = await processRunner.RunAsync(
                "dotnet", "test --collect:\"XPlat Code Coverage\" --no-build", context.RepoRoot, timeout: ScannerTimeout, cancellationToken: cancellationToken);
            _ = build;
            _ = test; // Build/test failures don't abort the scan - `end` still uploads whatever analysis data was gathered, and CodeRail's own `build`/`test` steps already reported these results earlier in the pipeline.

            var end = await processRunner.RunAsync(
                "dotnet", "sonarscanner end", context.RepoRoot, environmentVariables: tokenEnv, timeout: ScannerTimeout, cancellationToken: cancellationToken);
            if (end.ExitCode != 0)
            {
                return Unavailable(stopwatch.Elapsed, $"'dotnet sonarscanner end' failed:\n{OutputTail.Last(end.StandardOutput + end.StandardError)}");
            }
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "'dotnet sonarscanner' could not be started");
            return Unavailable(stopwatch.Elapsed,
                "Could not run 'dotnet sonarscanner'. Install it as a local tool (`dotnet tool install dotnet-sonarscanner`) " +
                "or a global tool (`dotnet tool install -g dotnet-sonarscanner`).");
        }

        var reportTaskPath = Path.Combine(context.RepoRoot, ".sonarqube", "out", ".sonar", "report-task.txt");
        if (!File.Exists(reportTaskPath))
        {
            return Unavailable(stopwatch.Elapsed, $"No Sonar report-task.txt was produced at '{reportTaskPath}' after 'sonarscanner end'.");
        }

        var reportTask = ParseReportTask(await File.ReadAllTextAsync(reportTaskPath, cancellationToken));
        if (!reportTask.TryGetValue("ceTaskId", out var ceTaskId))
        {
            return Unavailable(stopwatch.Elapsed, "Sonar's report-task.txt didn't contain a 'ceTaskId'.");
        }

        var serverUrl = (reportTask.GetValueOrDefault("serverUrl") ?? hostUrl).TrimEnd('/');

        var taskStatus = await WaitForBackgroundTaskAsync(serverUrl, ceTaskId, token, cancellationToken);
        if (taskStatus != "SUCCESS")
        {
            return Unavailable(stopwatch.Elapsed, $"Sonar's background analysis task finished with status '{taskStatus}', not SUCCESS.");
        }

        var issuesJson = await GetJsonAsync(
            $"{serverUrl}/api/issues/search?componentKeys={Uri.EscapeDataString(sonar.ProjectKey)}" +
            "&statuses=OPEN,CONFIRMED,REOPENED&sinceLeakPeriod=true&ps=500",
            token, cancellationToken);
        var findings = SonarIssuesParser.Parse(issuesJson, sonar.ProjectKey);

        stopwatch.Stop();
        var metrics = new Dictionary<string, object>
        {
            ["newIssueCount"] = findings.Count,
            ["newBlockerCount"] = findings.Count(f => f.Severity == Severity.Critical),
            ["newCriticalCount"] = findings.Count(f => f.Severity == Severity.Error)
        };
        return new ToolResult(ToolIds.Sonar, ValidationStatus.Passed, findings, metrics, [], stopwatch.Elapsed);
    }

    private async Task<string> WaitForBackgroundTaskAsync(string serverUrl, string ceTaskId, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxCeTaskPollAttempts; attempt++)
        {
            var json = await GetJsonAsync($"{serverUrl}/api/ce/task?id={Uri.EscapeDataString(ceTaskId)}", token, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var status = document.RootElement.GetProperty("task").GetProperty("status").GetString() ?? "UNKNOWN";

            if (status is "SUCCESS" or "FAILED" or "CANCELED")
            {
                return status;
            }

            logger.LogDebug("Sonar background task {CeTaskId} still {Status} - polling again", ceTaskId, status);
            await Task.Delay(CeTaskPollInterval, cancellationToken);
        }

        return "TIMEOUT";
    }

    private async Task<string> GetJsonAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static Dictionary<string, string> ParseReportTask(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                result[line[..separator]] = line[(separator + 1)..];
            }
        }

        return result;
    }

    private static ToolResult Unavailable(TimeSpan duration, string message) =>
        new(ToolIds.Sonar, ValidationStatus.PartiallyEvaluated,
            [new Finding("sonar-unavailable", Severity.Warning, message, null, null, null)],
            new Dictionary<string, object>(), [], duration);
}

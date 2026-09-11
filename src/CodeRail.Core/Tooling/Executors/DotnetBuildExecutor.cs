using CodeRail.Evidence;
using CodeRail.Execution;
using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling.Executors;

/// <summary>
/// Runs <c>dotnet build</c> against each of the context's solutions (or once against the repo
/// root if none were resolved, letting the SDK auto-discover). See
/// <c>docs/HIGH_LEVEL_PLAN.md</c> §9.1.
/// </summary>
/// <remarks>
/// Deliberately runs a plain <c>dotnet build</c> (implicit restore) rather than the doc's
/// illustrative <c>dotnet build --no-restore</c>: CodeRail validates arbitrary target
/// repositories that it has no guarantee were pre-restored by the caller, so the build step
/// self-contains restore + build rather than assuming anything ran before it. The steps *after*
/// it don't have to: once this one passes, <c>ValidationEngine</c> sets
/// <see cref="ToolContext.SkipBuild"/> and <see cref="DotnetTestExecutor"/>/
/// <see cref="CoverageExecutor"/> reuse this compile via <c>dotnet test --no-build</c> instead of
/// redoing it. What's left to share is the test *execution* itself - `test` and `coverage` still
/// run the suite once each.
/// </remarks>
public sealed class DotnetBuildExecutor(IProcessRunner processRunner, ILogger<DotnetBuildExecutor> logger) : IToolExecutor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public string Name => ValidationSteps.Build;

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var targets = context.SolutionPaths.Count > 0 ? context.SolutionPaths : [context.RepoRoot];
        var findings = new List<Finding>();
        var totalDuration = TimeSpan.Zero;
        var failedCount = 0;

        foreach (var target in targets)
        {
            var isRepoRoot = target == context.RepoRoot;
            var arguments = isRepoRoot ? "build" : $"build \"{target}\"";

            logger.LogInformation("Building {Target}", target);
            var result = await processRunner.RunAsync("dotnet", arguments, context.RepoRoot, timeout: Timeout, cancellationToken: cancellationToken);
            totalDuration += result.Duration;

            if (result.ExitCode != 0 || result.TimedOut)
            {
                failedCount++;
                var message = result.TimedOut
                    ? $"dotnet build timed out after {Timeout}"
                    : OutputTail.Last(result.StandardOutput + Environment.NewLine + result.StandardError);

                findings.Add(new Finding("build-failure", Severity.Error, message, isRepoRoot ? null : target, null, null));
            }
        }

        var status = failedCount == 0 ? ValidationStatus.Passed : ValidationStatus.Failed;
        var metrics = new Dictionary<string, object> { ["solutionsBuilt"] = targets.Count, ["solutionsFailed"] = failedCount };

        return new ToolResult(ToolIds.DotnetBuild, status, findings, metrics, [], totalDuration);
    }
}

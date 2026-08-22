using System.ComponentModel;
using System.Diagnostics;
using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Parsing;
using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling.Executors;

/// <summary>
/// Runs <c>dotnet stryker</c> (mutation testing) against each of the context's solutions and
/// converts the resulting mutation-testing-elements JSON report into the common evidence model.
/// See <c>docs/HIGH_LEVEL_PLAN.md</c> §9.5.
/// </summary>
/// <remarks>
/// Deliberately never part of the shipped <c>dotnet-default</c> profile - mutation testing is
/// docs §14's "final validation" tier, far more expensive than build/test/coverage/CodeGuard, and
/// shouldn't run on every AI repair iteration. A repository opts in by adding <c>stryker</c> to its
/// own <c>--profile</c> file; <c>ValidationEngine</c> needs no special-casing for this - it's
/// already just "a step that happens not to be in the default list". Like
/// <c>CodeGuardExecutor</c>/<c>CoverageExecutor</c>, a missing tool or missing report degrades to
/// <see cref="ValidationStatus.PartiallyEvaluated"/> rather than a hard failure - an environment gap
/// (Stryker not installed) shouldn't block the repair loop the same way an actual code defect
/// should.
/// </remarks>
public sealed class StrykerExecutor(IProcessRunner processRunner, ILogger<StrykerExecutor> logger) : IToolExecutor
{
    // Mutation testing re-runs the test suite once per surviving-mutant candidate - far slower
    // than build/test/coverage, hence the generous timeout (docs §14, "final validation").
    private static readonly TimeSpan Timeout = TimeSpan.FromHours(1);

    public string Name => ValidationSteps.Stryker;

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var targets = context.SolutionPaths.Count > 0 ? context.SolutionPaths : [context.RepoRoot];
        var stopwatch = Stopwatch.StartNew();
        var findings = new List<Finding>();
        int killed = 0, survived = 0, noCoverage = 0, timeout = 0;
        var anyReportFound = false;

        foreach (var target in targets)
        {
            var isRepoRoot = target == context.RepoRoot;
            // Stryker.NET analyzes every project referenced by a solution when run from that
            // solution file's own directory - it doesn't accept a solution path as a plain
            // trailing argument the way `dotnet test`/`dotnet build` do.
            var workingDirectory = isRepoRoot ? context.RepoRoot : Path.GetDirectoryName(target) ?? context.RepoRoot;
            var arguments = isRepoRoot
                ? "stryker --reporter Json"
                : $"stryker --solution \"{target}\" --reporter Json";

            ProcessResult result;
            try
            {
                logger.LogInformation("Running mutation testing for {Target}", target);
                result = await processRunner.RunAsync("dotnet", arguments, workingDirectory, timeout: Timeout, cancellationToken: cancellationToken);
            }
            catch (Win32Exception ex)
            {
                logger.LogWarning(ex, "'dotnet stryker' could not be started");
                return Unavailable(stopwatch.Elapsed,
                    "Could not run 'dotnet stryker'. Install it as a local tool (`dotnet tool install dotnet-stryker`) " +
                    "or a global tool (`dotnet tool install -g dotnet-stryker`).");
            }

            var outputRoot = Path.Combine(workingDirectory, "StrykerOutput");
            var reportFile = Directory.Exists(outputRoot)
                ? Directory.EnumerateFiles(outputRoot, "mutation-report.json", SearchOption.AllDirectories).FirstOrDefault()
                : null;

            if (reportFile is null)
            {
                logger.LogWarning(
                    "No Stryker mutation report was produced for {Target} (exit code {ExitCode}):\n{Output}",
                    target, result.ExitCode, OutputTail.Last(result.StandardOutput + result.StandardError));
                continue;
            }

            anyReportFound = true;
            var summary = StrykerJsonParser.Parse(await File.ReadAllTextAsync(reportFile, cancellationToken));
            killed += summary.Killed;
            survived += summary.Survived;
            noCoverage += summary.NoCoverage;
            timeout += summary.Timeout;
            findings.AddRange(summary.Findings);
        }

        stopwatch.Stop();

        if (!anyReportFound)
        {
            return Unavailable(stopwatch.Elapsed,
                "No Stryker mutation report was produced. Ensure the 'dotnet-stryker' tool is installed and the " +
                "target has a supported test project.");
        }

        // Whether a mutation score is "good enough" is a policy decision, not this executor's -
        // see PolicyEvaluator - so this reports Passed whenever a report was successfully
        // collected, regardless of the score.
        var detected = killed + timeout;
        var valid = detected + survived + noCoverage;
        var metrics = new Dictionary<string, object>
        {
            ["killed"] = killed,
            ["survived"] = survived,
            ["noCoverage"] = noCoverage,
            ["timeout"] = timeout
        };
        if (valid > 0)
        {
            metrics["mutationScore"] = Math.Round((double)detected / valid * 100, 2);
        }

        return new ToolResult(ToolIds.Stryker, ValidationStatus.Passed, findings, metrics, [], stopwatch.Elapsed);
    }

    private static ToolResult Unavailable(TimeSpan duration, string message) =>
        new(ToolIds.Stryker, ValidationStatus.PartiallyEvaluated,
            [new Finding("stryker-unavailable", Severity.Warning, message, null, null, null)],
            new Dictionary<string, object>(), [], duration);
}

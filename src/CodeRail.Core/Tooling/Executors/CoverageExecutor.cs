using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Parsing;
using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling.Executors;

/// <summary>
/// Runs <c>dotnet test --collect:"XPlat Code Coverage"</c> against each of the context's
/// solutions and aggregates the resulting Cobertura report(s) into line/branch coverage metrics.
/// See <c>docs/HIGH_LEVEL_PLAN.md</c> §9.3.
/// </summary>
/// <remarks>
/// Whether a number is "good enough" is a policy decision, not this executor's - see
/// <c>PolicyEvaluator</c> - so this always reports <see cref="ValidationStatus.Passed"/> when
/// collection itself succeeded, regardless of the coverage percentage. Also duplicates the test
/// run <see cref="DotnetTestExecutor"/> already did (docs' "fast vs. expensive" tiering, §14,
/// treats coverage as a "medium loop" concern anyway) - combining them into a single invocation
/// is a documented follow-up once the engine can share intermediate output between steps.
/// </remarks>
public sealed class CoverageExecutor(IProcessRunner processRunner, ILogger<CoverageExecutor> logger) : IToolExecutor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    public string Name => ValidationSteps.Coverage;

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var targets = context.SolutionPaths.Count > 0 ? context.SolutionPaths : [context.RepoRoot];
        var totalDuration = TimeSpan.Zero;
        double linesCovered = 0, linesValid = 0, branchesCovered = 0, branchesValid = 0;
        var anyReportFound = false;

        foreach (var target in targets)
        {
            var isRepoRoot = target == context.RepoRoot;
            var resultsDirectory = Directory.CreateTempSubdirectory("coderail-coverage-").FullName;
            try
            {
                var arguments = (isRepoRoot ? "test" : $"test \"{target}\"") +
                    $" --collect:\"XPlat Code Coverage\" --results-directory \"{resultsDirectory}\"";

                logger.LogInformation("Collecting coverage for {Target}", target);
                var result = await processRunner.RunAsync("dotnet", arguments, context.RepoRoot, timeout: Timeout, cancellationToken: cancellationToken);
                totalDuration += result.Duration;

                var reportFiles = Directory.Exists(resultsDirectory)
                    ? Directory.EnumerateFiles(resultsDirectory, "coverage.cobertura.xml", SearchOption.AllDirectories).ToList()
                    : [];

                foreach (var reportFile in reportFiles)
                {
                    anyReportFound = true;
                    var summary = CoberturaParser.Parse(reportFile);
                    linesCovered += summary.LinesCovered;
                    linesValid += summary.LinesValid;
                    branchesCovered += summary.BranchesCovered;
                    branchesValid += summary.BranchesValid;
                }
            }
            finally
            {
                TryDelete(resultsDirectory);
            }
        }

        if (!anyReportFound)
        {
            // Most commonly: the target's test projects don't reference coverlet.collector, so
            // "XPlat Code Coverage" silently produces nothing - not an infrastructure crash, but
            // still can't be evaluated.
            logger.LogWarning("No Cobertura coverage report was produced by any target");
            var finding = new Finding(
                "coverage-unavailable", Severity.Warning,
                "No coverage report was produced. Ensure test projects reference the 'coverlet.collector' NuGet package.",
                null, null, null);
            return new ToolResult(ToolIds.Coverage, ValidationStatus.PartiallyEvaluated, [finding], new Dictionary<string, object>(), [], totalDuration);
        }

        var metrics = new Dictionary<string, object>();
        if (linesValid > 0)
        {
            metrics["lineCoverage"] = Math.Round(linesCovered / linesValid * 100, 2);
        }

        if (branchesValid > 0)
        {
            metrics["branchCoverage"] = Math.Round(branchesCovered / branchesValid * 100, 2);
        }

        return new ToolResult(ToolIds.Coverage, ValidationStatus.Passed, [], metrics, [], totalDuration);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

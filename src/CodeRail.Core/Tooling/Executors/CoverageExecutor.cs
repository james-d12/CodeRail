using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Parsing;
using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling.Executors;

/// <summary>
/// Runs <c>dotnet test --collect:"XPlat Code Coverage"</c> against each of the context's
/// solutions and aggregates the resulting Cobertura report(s) into line/branch coverage metrics.
/// See <c>docs/HIGH_LEVEL_PLAN.md</c> §9.3. When <see cref="ToolContext.Changes"/> is set, also
/// reports a <c>newCodeLineCoverage</c> metric restricted to the changed files (§12), by matching
/// each Cobertura report's per-file line coverage against <see cref="ChangeSet.ChangedFiles"/>.
/// </summary>
/// <remarks>
/// Whether a number is "good enough" is a policy decision, not this executor's - see
/// <c>PolicyEvaluator</c> - so this always reports <see cref="ValidationStatus.Passed"/> when
/// collection itself succeeded, regardless of the coverage percentage. Adds <c>--no-build</c>
/// (which implies <c>--no-restore</c>) when <see cref="ToolContext.SkipBuild"/> says a <c>build</c>
/// step already compiled these same targets, so this no longer redoes that compile - but it still
/// re-executes the test suite <see cref="DotnetTestExecutor"/> already ran (docs' "fast vs.
/// expensive" tiering, §14, treats coverage as a "medium loop" concern anyway). Combining the two
/// into a single <c>dotnet test</c> invocation that emits both TRX and Cobertura is the remaining
/// follow-up; they can't simply run concurrently, because coverlet rewrites the assemblies in
/// <c>bin</c> in place (see <c>ValidationEngine</c>'s notes).
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

        // Only accumulated when context.Changes is set (docs §12) - per-file breakdown isn't
        // needed for the whole-repository metrics above, so skip the extra parsing otherwise.
        var perFileTotals = new Dictionary<string, (double Covered, double Valid)>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            var isRepoRoot = target == context.RepoRoot;
            var resultsDirectory = Directory.CreateTempSubdirectory("coderail-coverage-").FullName;
            try
            {
                var arguments = (isRepoRoot ? "test" : $"test \"{target}\"") +
                    $" --collect:\"XPlat Code Coverage\" --results-directory \"{resultsDirectory}\"" +
                    (context.SkipBuild ? " --no-build" : "");

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

                    if (context.Changes is not null)
                    {
                        foreach (var fileCoverage in CoberturaParser.ParsePerFile(reportFile))
                        {
                            var key = NormalizePath(fileCoverage.FileName, context.RepoRoot);
                            var existing = perFileTotals.TryGetValue(key, out var accumulated) ? accumulated : (0, 0);
                            perFileTotals[key] = (existing.Covered + fileCoverage.LinesCovered, existing.Valid + fileCoverage.LinesValid);
                        }
                    }
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

        if (context.Changes is { ChangedFiles.Count: > 0 })
        {
            var (newCodeCovered, newCodeValid) = SumChangedFileCoverage(perFileTotals, context.Changes.ChangedFiles, context.RepoRoot);
            if (newCodeValid > 0)
            {
                metrics["newCodeLineCoverage"] = Math.Round(newCodeCovered / newCodeValid * 100, 2);
            }
        }

        return new ToolResult(ToolIds.Coverage, ValidationStatus.Passed, [], metrics, [], totalDuration);
    }

    private static (double Covered, double Valid) SumChangedFileCoverage(
        IReadOnlyDictionary<string, (double Covered, double Valid)> perFileTotals, IReadOnlyList<string> changedFiles, string repoRoot)
    {
        double covered = 0, valid = 0;
        foreach (var changedFile in changedFiles)
        {
            if (perFileTotals.TryGetValue(NormalizePath(changedFile, repoRoot), out var fileCoverage))
            {
                covered += fileCoverage.Covered;
                valid += fileCoverage.Valid;
            }
        }

        return (covered, valid);
    }

    /// <summary>Cobertura <c>filename</c> attributes and git's repo-root-relative diff paths need
    /// a common form to compare - resolve both to a full, OS-normalized path.</summary>
    private static string NormalizePath(string path, string repoRoot) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path));

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

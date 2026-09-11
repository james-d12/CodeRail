using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Parsing;
using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling.Executors;

/// <summary>
/// Runs <c>dotnet test</c> against each of the context's solutions, parses the resulting TRX
/// file(s), and converts failures into <see cref="Finding"/>s. See
/// <c>docs/HIGH_LEVEL_PLAN.md</c> §9.2. Adds <c>--no-build</c> (which implies <c>--no-restore</c>)
/// when <see cref="ToolContext.SkipBuild"/> says a <c>build</c> step already compiled these same
/// targets in this run; otherwise it self-restores and self-builds, so a profile without a
/// <c>build</c> step still works - see <see cref="DotnetBuildExecutor"/>'s remarks.
/// </summary>
public sealed class DotnetTestExecutor(IProcessRunner processRunner, ILogger<DotnetTestExecutor> logger) : IToolExecutor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    public string Name => ValidationSteps.Test;

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var targets = context.SolutionPaths.Count > 0 ? context.SolutionPaths : [context.RepoRoot];
        var findings = new List<Finding>();
        var totalDuration = TimeSpan.Zero;
        int total = 0, passed = 0, failed = 0, skipped = 0;
        var infrastructureFailure = false;

        foreach (var target in targets)
        {
            var isRepoRoot = target == context.RepoRoot;
            var resultsDirectory = Directory.CreateTempSubdirectory("coderail-test-").FullName;
            try
            {
                var arguments = (isRepoRoot ? "test" : $"test \"{target}\"") +
                    $" --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDirectory}\"" +
                    (context.SkipBuild ? " --no-build" : "");

                logger.LogInformation("Testing {Target}", target);
                var result = await processRunner.RunAsync("dotnet", arguments, context.RepoRoot, timeout: Timeout, cancellationToken: cancellationToken);
                totalDuration += result.Duration;

                var trxFiles = Directory.Exists(resultsDirectory)
                    ? Directory.EnumerateFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories).ToList()
                    : [];

                if (trxFiles.Count == 0)
                {
                    if (result.TimedOut || result.ExitCode != 0)
                    {
                        // No TRX plus a timeout or non-zero exit means the run never got as far
                        // as executing tests (e.g. a compile error) rather than tests genuinely
                        // failing - surface it as its own finding instead of silently reporting
                        // zero tests.
                        infrastructureFailure = true;
                        var message = result.TimedOut
                            ? $"dotnet test timed out after {Timeout}"
                            : OutputTail.Last(result.StandardOutput + Environment.NewLine + result.StandardError);
                        findings.Add(new Finding("test-run-failure", Severity.Error, message, isRepoRoot ? null : target, null, null));
                    }
                    else
                    {
                        // No TRX with a clean (zero) exit means dotnet test ran successfully but
                        // this target has no test projects to run (e.g. a solution/project made
                        // up entirely of libraries) - not a failure, just nothing to report.
                        findings.Add(new Finding("no-tests-found", Severity.Info, "No test projects found.", isRepoRoot ? null : target, null, null));
                    }

                    continue;
                }

                foreach (var trxFile in trxFiles)
                {
                    var summary = TrxParser.Parse(trxFile);
                    total += summary.Total;
                    passed += summary.Passed;
                    failed += summary.Failed;
                    skipped += summary.Skipped;

                    foreach (var failure in summary.Failures)
                    {
                        var message = failure.ErrorMessage is null ? failure.TestName : $"{failure.TestName}: {failure.ErrorMessage}";
                        findings.Add(new Finding("test-failure", Severity.Error, message, isRepoRoot ? null : target, null, null));
                    }
                }
            }
            finally
            {
                TryDelete(resultsDirectory);
            }
        }

        var status = infrastructureFailure || failed > 0 ? ValidationStatus.Failed : ValidationStatus.Passed;
        var metrics = new Dictionary<string, object> { ["total"] = total, ["passed"] = passed, ["failed"] = failed, ["skipped"] = skipped };

        return new ToolResult(ToolIds.DotnetTest, status, findings, metrics, [], totalDuration);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup - leaving a stray temp directory behind isn't worth failing the
            // whole tool run over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

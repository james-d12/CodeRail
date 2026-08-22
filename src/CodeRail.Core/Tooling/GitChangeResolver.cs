using CodeRail.Execution;
using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling;

/// <summary>
/// Resolves a <see cref="ChangeSet"/> against a base revision by shelling out to <c>git</c>
/// through <see cref="IProcessRunner"/> - never <see cref="System.Diagnostics.Process"/> directly,
/// matching every other executor in this codebase. See <c>docs/HIGH_LEVEL_PLAN.md</c> §12
/// (Changed-Code Awareness).
/// </summary>
/// <remarks>
/// Deliberately git-CLI-based rather than a native library (e.g. LibGit2Sharp): CodeRail already
/// requires <c>git</c> to be present (it's validating a git repository), <see cref="IProcessRunner"/>
/// already provides timeout/cancellation/output-capture/env-var handling for free, and adding a
/// native dependency would be the first of its kind in the solution for no offsetting benefit.
/// </remarks>
public static class GitChangeResolver
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// "Changed" means the union of: everything committed since <paramref name="baseRef"/> and
    /// <c>HEAD</c>'s merge-base, everything staged, and everything still only in the working
    /// tree - because an AI agent's just-made edits are typically still uncommitted at the moment
    /// <c>coderail validate</c> runs. Known limitation: brand-new *untracked* files (not yet
    /// <c>git add</c>-ed) are not reported by any of the three <c>git diff</c> invocations this
    /// uses, so they won't appear in <see cref="ChangeSet.ChangedFiles"/> - a documented follow-up,
    /// not a silent correctness gap: <c>git diff</c> simply doesn't see untracked paths.
    /// </summary>
    public static async Task<ChangeSet> ResolveAsync(
        IProcessRunner processRunner,
        string repoRoot,
        string baseRef,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var mergeBase = await RunGit(processRunner, repoRoot, $"merge-base \"{baseRef}\" HEAD", cancellationToken);
        var mergeBaseSha = mergeBase.Trim();
        if (mergeBaseSha.Length == 0)
        {
            throw new InvalidOperationException($"'git merge-base \"{baseRef}\" HEAD' produced no output - is '{baseRef}' a valid ref?");
        }

        var changedFiles = new SortedSet<string>(StringComparer.Ordinal);
        await CollectNameOnlyDiff(processRunner, repoRoot, $"diff --name-only \"{mergeBaseSha}\" HEAD", changedFiles, cancellationToken);
        await CollectNameOnlyDiff(processRunner, repoRoot, "diff --name-only", changedFiles, cancellationToken);
        await CollectNameOnlyDiff(processRunner, repoRoot, "diff --name-only --cached", changedFiles, cancellationToken);

        logger?.LogDebug("Resolved {Count} changed file(s) against base '{BaseRef}' (merge-base {MergeBaseSha})", changedFiles.Count, baseRef, mergeBaseSha);

        var changedProjects = ResolveChangedProjects(repoRoot, changedFiles);
        return new ChangeSet(changedFiles.ToList(), changedProjects);
    }

    private static async Task CollectNameOnlyDiff(
        IProcessRunner processRunner, string repoRoot, string arguments, SortedSet<string> into, CancellationToken cancellationToken)
    {
        var output = await RunGit(processRunner, repoRoot, arguments, cancellationToken);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            into.Add(line);
        }
    }

    private static async Task<string> RunGit(IProcessRunner processRunner, string repoRoot, string arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("git", arguments, repoRoot, timeout: Timeout, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"'git {arguments}' failed (exit code {result.ExitCode}):\n{OutputTail.Last(result.StandardError)}");
        }

        return result.StandardOutput;
    }

    private static IReadOnlyList<string> ResolveChangedProjects(string repoRoot, IReadOnlyCollection<string> changedFiles)
    {
        var projects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativeFile in changedFiles)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(relativeFile, repoRoot));
            while (directory is not null && directory.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                var csproj = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.csproj").FirstOrDefault() : null;
                if (csproj is not null)
                {
                    projects.Add(csproj);
                    break;
                }

                directory = Path.GetDirectoryName(directory);
            }
        }

        return projects.ToList();
    }
}

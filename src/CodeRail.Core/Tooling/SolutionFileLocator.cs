using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling;

/// <summary>
/// Discovers .sln/.slnx files under a repository root, recursively, skipping build/tooling
/// directories. Ported from CodeGuard's <c>SolutionFileLocator</c> (same problem, same fix: a
/// nested worktree checkout under <c>.claude/worktrees/</c> would otherwise be discovered as a
/// second copy of the same repo).
/// </summary>
public static class SolutionFileLocator
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", ".claude"
    };

    public static IReadOnlyList<string> Resolve(string repoRoot, IReadOnlyList<string> explicitSolutionPaths, ILogger? logger = null)
    {
        if (explicitSolutionPaths.Count > 0)
        {
            var resolved = explicitSolutionPaths.Select(p => Path.GetFullPath(p, repoRoot)).ToList();
            var missing = resolved.Where(p => !File.Exists(p)).ToList();
            if (missing.Count > 0)
            {
                logger?.LogError("Solution file(s) not found: {MissingFiles}", string.Join(", ", missing));
                throw new FileNotFoundException("Solution file(s) not found: " + string.Join(", ", missing));
            }

            logger?.LogDebug("Using {Count} explicitly specified solution file(s)", resolved.Count);
            return resolved;
        }

        var candidates = FindSolutionFiles(repoRoot).ToList();
        logger?.LogDebug("Discovered {Count} solution file(s) under {RepoRoot}", candidates.Count, repoRoot);
        return candidates;
    }

    private static IEnumerable<string> FindSolutionFiles(string directoryPath)
    {
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.sln"))
        {
            yield return file;
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.slnx"))
        {
            yield return file;
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(directoryPath))
        {
            if (ExcludedDirectoryNames.Contains(Path.GetFileName(subdirectory)))
            {
                continue;
            }

            foreach (var file in FindSolutionFiles(subdirectory))
            {
                yield return file;
            }
        }
    }
}

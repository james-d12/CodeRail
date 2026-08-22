using CodeRail.Evidence;

namespace CodeRail.Tooling;

/// <summary>
/// Runs one external engineering tool (a build, a test run, CodeGuard, ...) and converts its
/// output into the common evidence model. See <c>docs/HIGH_LEVEL_PLAN.md</c> §7 (Tool Executors).
/// Every implementation should go through <see cref="CodeRail.Execution.IProcessRunner"/> rather
/// than starting processes itself.
/// </summary>
public interface IToolExecutor
{
    /// <summary>The validation-profile step name this executor answers to, e.g. "build", "test",
    /// "codeguard", "coverage" - matched against <c>ValidationProfile.Steps</c>.</summary>
    string Name { get; }

    Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken);
}

/// <summary>Everything an executor needs to know about the repository it's validating.</summary>
/// <param name="RepoRoot">Root directory of the repository being validated.</param>
/// <param name="SolutionPaths">Explicit .sln/.slnx paths to target. Empty means "let the
/// underlying tool auto-discover the solution/project in <paramref name="RepoRoot"/>".</param>
public sealed record ToolContext(string RepoRoot, IReadOnlyList<string> SolutionPaths);

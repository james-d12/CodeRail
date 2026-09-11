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
/// <param name="Changes">The resolved changed-code set (docs §12), when the caller supplied a
/// <c>--base-ref</c>. Null means changed-code awareness is off - every existing executor ignores
/// this and evaluates the whole repository, which stays the default behaviour.</param>
/// <param name="Sonar">Sonar server connection details (docs §9.6), resolved from the validation
/// profile's <c>quality.sonar.projectKey</c>. Null means <c>SonarExecutor</c> has nothing to
/// connect to and degrades to <see cref="ValidationStatus.PartiallyEvaluated"/> - every other
/// executor ignores this field.</param>
/// <param name="SkipBuild">True once a <c>build</c> step has passed earlier in <i>this</i> run, so
/// a compile of exactly these <paramref name="SolutionPaths"/> is already on disk and downstream
/// executors can reuse it instead of redoing it (<c>dotnet test --no-build</c>, which also implies
/// <c>--no-restore</c>). Set by <c>ValidationEngine</c>; false - every executor self-restores and
/// self-builds, as it must for a profile with no <c>build</c> step at all.</param>
public sealed record ToolContext(
    string RepoRoot, IReadOnlyList<string> SolutionPaths, ChangeSet? Changes = null, SonarConfig? Sonar = null,
    bool SkipBuild = false);

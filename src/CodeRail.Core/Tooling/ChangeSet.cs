namespace CodeRail.Tooling;

/// <summary>
/// The set of files/projects an AI agent has actually touched, relative to some base revision -
/// the raw input to changed-code-aware policy (<c>docs/HIGH_LEVEL_PLAN.md</c> §12). Produced by
/// <see cref="GitChangeResolver"/>, carried on <see cref="ToolContext.Changes"/>.
/// </summary>
/// <param name="ChangedFiles">Repo-root-relative paths (forward-slash, as git reports them) of
/// every file that differs from the base revision - committed since the merge-base, staged, or
/// still only in the working tree.</param>
/// <param name="ChangedProjects">Absolute paths of the <c>.csproj</c> files that own at least one
/// changed file, derived by walking each changed file's directory upward for the nearest project
/// file.</param>
public sealed record ChangeSet(IReadOnlyList<string> ChangedFiles, IReadOnlyList<string> ChangedProjects);

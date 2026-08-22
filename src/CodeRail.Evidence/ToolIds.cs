namespace CodeRail.Evidence;

/// <summary>
/// Well-known <see cref="ToolResult.Tool"/> identifiers, shared between the executors that
/// produce a given tool's <see cref="ToolResult"/> (<c>CodeRail.Core.Tooling</c>) and the policy
/// evaluation that interprets it (<c>CodeRail.Core.Policy</c>) - both depend on this
/// zero-dependency project, so the identifiers live here rather than in either namespace.
/// </summary>
public static class ToolIds
{
    public const string DotnetBuild = "dotnet-build";
    public const string DotnetTest = "dotnet-test";
    public const string CodeGuard = "codeguard";
    public const string Coverage = "coverage";
}

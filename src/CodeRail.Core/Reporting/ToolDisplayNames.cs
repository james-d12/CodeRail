using CodeRail.Evidence;

namespace CodeRail.Reporting;

/// <summary>Maps a <see cref="ToolResult.Tool"/> identifier to the short label used in console
/// output (§1's <c>Build: PASS</c> / <c>CodeGuard: FAIL</c> style).</summary>
internal static class ToolDisplayNames
{
    public static string For(string toolId) => toolId switch
    {
        ToolIds.DotnetBuild => "Build",
        ToolIds.DotnetTest => "Tests",
        ToolIds.CodeGuard => "CodeGuard",
        ToolIds.Coverage => "Coverage",
        ToolIds.Stryker => "Stryker",
        ToolIds.Sonar => "Sonar",
        _ => toolId
    };
}

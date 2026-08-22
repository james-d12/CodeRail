namespace CodeRail.Evidence;

/// <summary>
/// A single actionable issue reported by a tool, normalised into the common evidence model so an
/// AI agent can consume a CodeGuard finding, a Sonar finding, and a Stryker finding the same way.
/// </summary>
public sealed record Finding(
    string Type,
    Severity Severity,
    string Message,
    string? File,
    int? Line,
    string? RuleId);

public enum Severity
{
    Info,
    Warning,
    Error,
    Critical
}

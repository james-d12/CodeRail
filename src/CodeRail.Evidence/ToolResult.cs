namespace CodeRail.Evidence;

/// <summary>
/// The outcome of a single tool execution (e.g. a build, a test run, CodeGuard), before quality
/// policy is applied. See <c>docs/HIGH_LEVEL_PLAN.md</c> §10 (Common Evidence Model).
/// </summary>
public sealed record ToolResult(
    string Tool,
    ValidationStatus Status,
    IReadOnlyList<Finding> Findings,
    IReadOnlyDictionary<string, object> Metrics,
    IReadOnlyList<Artifact> Artifacts,
    TimeSpan Duration);

/// <summary>The pass/fail/partial outcome of a tool execution or overall quality gate.</summary>
public enum ValidationStatus
{
    Passed,
    Failed,
    PartiallyEvaluated
}

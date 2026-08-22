using CodeRail.Evidence;

namespace CodeRail.Policy;

/// <summary>
/// The final, top-level answer CodeRail hands back to an AI agent or CI pipeline: every tool's
/// raw evidence, plus the subset of findings that actually blocked the gate once
/// <see cref="QualityProfile"/> thresholds were applied. See <c>docs/HIGH_LEVEL_PLAN.md</c> §1
/// and §11 - this is the "quality gate" that <see cref="PolicyEvaluator"/> produces from a run's
/// <see cref="ToolResult"/>s.
/// </summary>
public sealed record GateResult(
    ValidationStatus Status,
    IReadOnlyList<ToolResult> Tools,
    IReadOnlyList<Finding> BlockingFindings,
    DateTimeOffset EvaluatedAtUtc);

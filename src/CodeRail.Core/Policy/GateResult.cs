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
    DateTimeOffset EvaluatedAtUtc)
{
    /// <summary>Severity breakdown of <see cref="BlockingFindings"/>, computed on read rather than
    /// stored - keeps every existing <c>new GateResult(...)</c> call site working and leaves record
    /// equality (which only covers primary-constructor-backed fields) unaffected. Serialized by
    /// <see cref="Reporting.Json.JsonGateResultWriter"/> for free since it's a public property.</summary>
    public FindingSummary Summary => new(
        BlockingFindings.Count,
        BlockingFindings.Count(f => f.Severity == Severity.Critical),
        BlockingFindings.Count(f => f.Severity == Severity.Error),
        BlockingFindings.Count(f => f.Severity == Severity.Warning),
        BlockingFindings.Count(f => f.Severity == Severity.Info));
}

/// <summary>Severity counts across a <see cref="GateResult"/>'s <see cref="GateResult.BlockingFindings"/>.</summary>
public sealed record FindingSummary(int Total, int Critical, int Error, int Warning, int Info);

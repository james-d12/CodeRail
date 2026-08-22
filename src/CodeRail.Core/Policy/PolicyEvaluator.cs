using CodeRail.Evidence;

namespace CodeRail.Policy;

/// <summary>
/// Turns a set of raw <see cref="ToolResult"/>s into a single <see cref="GateResult"/> by
/// applying a <see cref="QualityProfile"/>'s thresholds - a pure function with no I/O, per
/// <c>docs/HIGH_LEVEL_PLAN.md</c> §24.3 ("policy is separate from execution"). Deliberately
/// whole-repository, not changed-code-aware (§12) - see <see cref="CoverageQualityThresholds"/>.
/// </summary>
public sealed class PolicyEvaluator(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public GateResult Evaluate(IReadOnlyList<ToolResult> results, QualityProfile profile)
    {
        var blockingFindings = new List<Finding>();
        var anyPartiallyEvaluated = false;

        foreach (var result in results)
        {
            blockingFindings.AddRange(EvaluateTool(result, profile));

            if (result.Status == ValidationStatus.PartiallyEvaluated)
            {
                anyPartiallyEvaluated = true;
            }
        }

        var status = blockingFindings.Count > 0
            ? ValidationStatus.Failed
            : anyPartiallyEvaluated ? ValidationStatus.PartiallyEvaluated : ValidationStatus.Passed;

        return new GateResult(status, results, blockingFindings, _timeProvider.GetUtcNow());
    }

    private static IEnumerable<Finding> EvaluateTool(ToolResult result, QualityProfile profile) => result.Tool switch
    {
        // A failed build is unconditionally blocking - there's no meaningful threshold for "how
        // many compile errors are acceptable".
        ToolIds.DotnetBuild when result.Status != ValidationStatus.Passed => result.Findings,

        ToolIds.DotnetTest when ReadInt(result, "failed") > profile.Test.AllowFailures => result.Findings,

        ToolIds.CodeGuard when BreachesCodeGuardThresholds(result, profile.CodeGuard) =>
            result.Findings.Where(f => f.Severity is Severity.Error or Severity.Critical),

        ToolIds.Coverage when BelowCoverageThreshold(result, profile.Coverage, out var finding) => [finding],

        // Any other tool (e.g. a future Sonar/Stryker executor without a dedicated threshold
        // yet) falls back to "a failed run blocks".
        _ when result.Status == ValidationStatus.Failed && !IsKnownToolId(result.Tool) => result.Findings,

        _ => []
    };

    private static bool IsKnownToolId(string tool) =>
        tool is ToolIds.DotnetBuild or ToolIds.DotnetTest or ToolIds.CodeGuard or ToolIds.Coverage;

    private static bool BreachesCodeGuardThresholds(ToolResult result, CodeGuardQualityThresholds thresholds)
    {
        var errors = result.Findings.Count(f => f.Severity == Severity.Error);
        var criticals = result.Findings.Count(f => f.Severity == Severity.Critical);
        return errors > thresholds.ErrorCount || criticals > thresholds.CriticalCount;
    }

    private static bool BelowCoverageThreshold(ToolResult result, CoverageQualityThresholds thresholds, out Finding finding)
    {
        finding = null!;
        if (thresholds.Minimum is not { } minimum || !TryReadDouble(result, "lineCoverage", out var actual) || actual >= minimum)
        {
            return false;
        }

        finding = new Finding(
            "coverage-below-threshold",
            Severity.Error,
            $"Line coverage {actual:0.##}% is below the required minimum {minimum:0.##}%.",
            null, null, null);
        return true;
    }

    private static int ReadInt(ToolResult result, string key) =>
        result.Metrics.TryGetValue(key, out var value) && value is int i ? i : 0;

    private static bool TryReadDouble(ToolResult result, string key, out double value)
    {
        if (result.Metrics.TryGetValue(key, out var raw))
        {
            switch (raw)
            {
                case double d:
                    value = d;
                    return true;
                case int i:
                    value = i;
                    return true;
            }
        }

        value = 0;
        return false;
    }
}

using CodeRail.Evidence;

namespace CodeRail.Policy;

/// <summary>
/// Turns a set of raw <see cref="ToolResult"/>s into a single <see cref="GateResult"/> by
/// applying a <see cref="QualityProfile"/>'s thresholds - a pure function with no I/O, per
/// <c>docs/HIGH_LEVEL_PLAN.md</c> §24.3 ("policy is separate from execution"). Whole-repository
/// thresholds (<see cref="CoverageQualityThresholds.Minimum"/>) and changed-code thresholds
/// (<see cref="CoverageQualityThresholds.NewCodeMinimum"/>, §12) are evaluated independently -
/// either can produce a blocking finding on its own.
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

        ToolIds.Coverage => CoverageFindings(result, profile.Coverage),

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

    private static IEnumerable<Finding> CoverageFindings(ToolResult result, CoverageQualityThresholds thresholds)
    {
        if (TryBelowThreshold(result, "lineCoverage", thresholds.Minimum, out var actual))
        {
            yield return new Finding(
                "coverage-below-threshold",
                Severity.Error,
                $"Line coverage {actual:0.##}% is below the required minimum {thresholds.Minimum:0.##}%.",
                null, null, null);
        }

        if (TryBelowThreshold(result, "newCodeLineCoverage", thresholds.NewCodeMinimum, out var newCodeActual))
        {
            yield return new Finding(
                "new-code-coverage-below-threshold",
                Severity.Error,
                $"New-code line coverage {newCodeActual:0.##}% is below the required minimum {thresholds.NewCodeMinimum:0.##}%.",
                null, null, null);
        }
    }

    private static bool TryBelowThreshold(ToolResult result, string metricKey, double? minimum, out double actual)
    {
        actual = 0;
        return minimum is { } m && TryReadDouble(result, metricKey, out actual) && actual < m;
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

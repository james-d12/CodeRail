namespace CodeRail.Policy;

/// <summary>
/// Per-tool pass/fail thresholds, loaded from a validation profile's <c>quality:</c> block
/// (<c>docs/HIGH_LEVEL_PLAN.md</c> §11). Plain mutable properties (rather than a record) so
/// <c>CodeRail.Configuration</c> can deserialize it directly from YAML.
/// </summary>
public sealed class QualityProfile
{
    public TestQualityThresholds Test { get; set; } = new();
    public CodeGuardQualityThresholds CodeGuard { get; set; } = new();
    public CoverageQualityThresholds Coverage { get; set; } = new();
}

public sealed class TestQualityThresholds
{
    /// <summary>Number of failing tests tolerated before the gate blocks. Zero (the default)
    /// means any failing test blocks.</summary>
    public int AllowFailures { get; set; }
}

public sealed class CodeGuardQualityThresholds
{
    /// <summary>Number of CodeGuard error-severity findings tolerated before the gate blocks.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Number of CodeGuard critical-severity findings tolerated before the gate blocks.</summary>
    public int CriticalCount { get; set; }
}

public sealed class CoverageQualityThresholds
{
    /// <summary>Minimum required whole-repository line coverage percentage (0-100). Null means no
    /// whole-repository coverage threshold is enforced - the tool still runs and reports the
    /// number, it just doesn't block the gate.</summary>
    public double? Minimum { get; set; }

    /// <summary>Minimum required line coverage percentage (0-100) restricted to changed code
    /// (docs §12), only evaluated when the caller supplied a <c>--base-ref</c> so a
    /// <see cref="CodeRail.Tooling.ChangeSet"/> was resolved. Null means no new-code threshold is
    /// enforced. Independent of <see cref="Minimum"/> - either, both, or neither can be
    /// configured, and either can independently block the gate.</summary>
    public double? NewCodeMinimum { get; set; }
}

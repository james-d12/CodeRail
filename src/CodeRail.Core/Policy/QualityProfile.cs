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
    /// <summary>Minimum required line coverage percentage (0-100). Null means no coverage
    /// threshold is enforced - the tool still runs and reports the number, it just doesn't block
    /// the gate. Changed-code-only coverage (docs §12) is out of scope for this profile shape;
    /// this is always a whole-repository threshold.</summary>
    public double? Minimum { get; set; }
}

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
    public MutationQualityThresholds Mutation { get; set; } = new();
    public SonarQualityThresholds Sonar { get; set; } = new();
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

public sealed class MutationQualityThresholds
{
    /// <summary>Minimum required whole-repository mutation score percentage (0-100), as reported
    /// by <c>StrykerExecutor</c> (docs §9.5). Null means no threshold is enforced - the tool still
    /// runs and reports the score, it just doesn't block the gate. Unlike
    /// <see cref="CoverageQualityThresholds.NewCodeMinimum"/>, there is deliberately no
    /// "changed-code-only" counterpart here yet - Stryker.NET's own incremental/<c>--since</c> mode
    /// would need to be wired to <see cref="CodeRail.Tooling.ChangeSet"/> first (docs §12), and
    /// that's a follow-up rather than part of the initial executor.</summary>
    public double? Minimum { get; set; }
}

public sealed class SonarQualityThresholds
{
    /// <summary>The Sonar project key. Required to actually run <c>SonarExecutor</c> - a profile
    /// that includes the <c>sonar</c> step without configuring this degrades to
    /// <c>PartiallyEvaluated</c> rather than attempting analysis with nothing to key it to.</summary>
    public string? ProjectKey { get; set; }

    /// <summary>SonarCloud organization key. Null for a self-hosted SonarQube server.</summary>
    public string? Organization { get; set; }

    /// <summary>Self-hosted SonarQube server URL. Null defaults to SonarCloud
    /// (<c>https://sonarcloud.io</c>).</summary>
    public string? HostUrl { get; set; }

    /// <summary>Number of new Sonar BLOCKER-severity issues (in the analysis's "new code period")
    /// tolerated before the gate blocks. Zero (the default) means any new blocker blocks - mirrors
    /// <see cref="CodeGuardQualityThresholds.ErrorCount"/>.</summary>
    public int NewBlocker { get; set; }

    /// <summary>Number of new Sonar CRITICAL-severity issues tolerated before the gate blocks.
    /// Zero (the default) means any new critical issue blocks.</summary>
    public int NewCritical { get; set; }
}

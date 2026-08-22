namespace CodeRail.Evidence;

/// <summary>
/// Well-known validation-profile step names (<c>docs/HIGH_LEVEL_PLAN.md</c> §13's
/// <c>validation:</c> list) - shared between <c>IToolExecutor.Name</c> implementations
/// (<c>CodeRail.Core.Tooling</c>), the embedded default profile YAML
/// (<c>CodeRail.Core.Configuration</c>), and the engine's build-failure short-circuit rule
/// (<c>CodeRail.Core.Engine</c>).
/// </summary>
public static class ValidationSteps
{
    public const string Build = "build";
    public const string Test = "test";
    public const string CodeGuard = "codeguard";
    public const string Coverage = "coverage";
}

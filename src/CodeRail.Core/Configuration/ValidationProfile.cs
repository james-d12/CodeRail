using CodeRail.Policy;

namespace CodeRail.Configuration;

/// <summary>
/// A named, ordered set of validation steps plus the quality thresholds applied to their
/// results - the YAML shape from <c>docs/HIGH_LEVEL_PLAN.md</c> §13, loaded by
/// <see cref="ValidationProfileLoader"/>. Property names are deliberately plain, idiomatic C#
/// (<see cref="QualityProfile.CodeGuard"/> etc.) - <see cref="ValidationProfileLoader"/>
/// deserializes YAML with a strict camelCase naming convention, so the on-disk key is
/// <c>codeGuard</c> rather than docs §11's illustrative <c>codeguard</c>.
/// </summary>
public sealed class ValidationProfile
{
    public string Profile { get; set; } = "dotnet-default";
    public List<string> Validation { get; set; } = [];
    public QualityProfile Quality { get; set; } = new();
}

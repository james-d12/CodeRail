using System.Text.Json;
using CodeRail.Evidence;

namespace CodeRail.Tooling.Parsing;

/// <summary>
/// Parses CodeGuard's own <c>codeguard validate --format json</c> output directly into
/// CodeRail's evidence model. See <c>docs/HIGH_LEVEL_PLAN.md</c> §9.4. CodeGuard's JSON is
/// treated purely as an external wire contract here, not a shared assembly reference - "normalise,
/// don't duplicate" (§24.2): CodeRail doesn't take a package dependency on CodeGuard's own model
/// types, it just knows the shape of the JSON it emits.
/// </summary>
public static class CodeGuardJsonParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static CodeGuardResultSummary Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<RawResult>(json, Options)
            ?? throw new JsonException("CodeGuard JSON output deserialized to null.");

        var findings = (raw.Violations ?? [])
            .Select(v => new Finding("codeguard-violation", ParseSeverity(v.Severity), v.Message ?? "(no message)", v.File, v.Line, v.RuleId))
            .ToList();

        return new CodeGuardResultSummary(ParseStatus(raw.Status), findings, raw.RulesEvaluated, raw.RulesPassed, raw.RulesFailed, raw.RulesErrored);
    }

    private static ValidationStatus ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "passed" => ValidationStatus.Passed,
        "failed" => ValidationStatus.Failed,
        _ => ValidationStatus.PartiallyEvaluated
    };

    private static Severity ParseSeverity(string? severity) => severity?.ToLowerInvariant() switch
    {
        "info" => Severity.Info,
        "warning" => Severity.Warning,
        "error" => Severity.Error,
        "critical" => Severity.Critical,
        _ => Severity.Warning
    };

    private sealed record RawResult(
        string? Status,
        int RulesEvaluated,
        int RulesPassed,
        int RulesFailed,
        int RulesErrored,
        List<RawViolation>? Violations);

    private sealed record RawViolation(string? RuleId, string? Severity, string? Message, string? File, int? Line);
}

public sealed record CodeGuardResultSummary(
    ValidationStatus Status,
    IReadOnlyList<Finding> Findings,
    int RulesEvaluated,
    int RulesPassed,
    int RulesFailed,
    int RulesErrored);

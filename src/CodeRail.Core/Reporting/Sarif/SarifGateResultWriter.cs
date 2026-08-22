using System.Text.Json;
using System.Text.Json.Serialization;
using CodeRail.Evidence;
using CodeRail.Policy;

namespace CodeRail.Reporting.Sarif;

/// <summary>
/// SARIF 2.1.0 output (<c>docs/HIGH_LEVEL_PLAN.md</c> §20) - for integration with IDE/code-scanning
/// systems that consume the format (e.g. GitHub code scanning). Unlike the console/JSON writers,
/// which foreground <see cref="GateResult.BlockingFindings"/>, this emits every
/// <see cref="Finding"/> from every <see cref="ToolResult"/> - blocking or not - since a SARIF
/// consumer typically wants the full set of issues a tool found, not just what happened to block
/// this particular run's gate.
/// </summary>
public sealed class SarifGateResultWriter : IGateResultWriter
{
    private const string SchemaUri = "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Format => "sarif";

    public async Task WriteAsync(GateResult result, TextWriter writer, CancellationToken cancellationToken = default)
    {
        var findings = result.Tools.SelectMany(tool => tool.Findings).ToList();

        var rules = findings
            .Select(RuleId)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new SarifRule(id))
            .ToList();

        var results = findings.Select(ToSarifResult).ToList();

        var log = new SarifLog(
            SchemaUri, "2.1.0",
            [new SarifRun(new SarifTool(new SarifDriver("CodeRail", rules)), results)]);

        var json = JsonSerializer.Serialize(log, Options);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }

    /// <summary>SARIF requires every result to reference a rule id - CodeGuard findings already
    /// carry one, but findings without one (e.g. <c>test-failure</c>, <c>coverage-below-threshold</c>)
    /// fall back to their <see cref="Finding.Type"/>.</summary>
    private static string RuleId(Finding finding) => finding.RuleId ?? finding.Type;

    private static SarifResult ToSarifResult(Finding finding) => new(
        RuleId(finding),
        ToLevel(finding.Severity),
        new SarifMessage(finding.Message),
        finding.File is null ? null : [new SarifLocation(new SarifPhysicalLocation(
            new SarifArtifactLocation(finding.File),
            finding.Line is { } line ? new SarifRegion(line) : null))]);

    // SARIF's `level` has no 4th tier - Severity.Critical collapses into "error" alongside
    // Severity.Error.
    private static string ToLevel(Severity severity) => severity switch
    {
        Severity.Info => "note",
        Severity.Warning => "warning",
        _ => "error"
    };
}

internal sealed record SarifLog(
    [property: JsonPropertyName("$schema")] string Schema,
    string Version,
    IReadOnlyList<SarifRun> Runs);

internal sealed record SarifRun(SarifTool Tool, IReadOnlyList<SarifResult> Results);

internal sealed record SarifTool(SarifDriver Driver);

internal sealed record SarifDriver(string Name, IReadOnlyList<SarifRule> Rules);

internal sealed record SarifRule(string Id);

internal sealed record SarifResult(string RuleId, string Level, SarifMessage Message, IReadOnlyList<SarifLocation>? Locations);

internal sealed record SarifMessage(string Text);

internal sealed record SarifLocation(SarifPhysicalLocation PhysicalLocation);

internal sealed record SarifPhysicalLocation(SarifArtifactLocation ArtifactLocation, SarifRegion? Region);

internal sealed record SarifArtifactLocation(string Uri);

internal sealed record SarifRegion(int StartLine);

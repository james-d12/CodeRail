using System.Text.Json;
using CodeRail.Evidence;

namespace CodeRail.Tooling.Parsing;

/// <summary>
/// Parses the Sonar Web API's <c>GET /api/issues/search</c> response (<c>docs/HIGH_LEVEL_PLAN.md</c>
/// §9.6) into CodeRail's evidence model. Treated purely as an external wire contract, same as
/// <see cref="CodeGuardJsonParser"/> - no dependency on any Sonar client library.
/// </summary>
public static class SonarIssuesParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <param name="json">The raw <c>api/issues/search</c> response body.</param>
    /// <param name="projectKey">Sonar's <c>component</c> field on each issue is prefixed with the
    /// project key (e.g. <c>"my-project:src/Foo.cs"</c>) - stripped off here so
    /// <see cref="Finding.File"/> is a plain repo-relative path.</param>
    public static IReadOnlyList<Finding> Parse(string json, string projectKey)
    {
        var raw = JsonSerializer.Deserialize<RawSearchResult>(json, Options)
            ?? throw new JsonException("Sonar issues JSON deserialized to null.");

        var prefix = projectKey + ":";
        return (raw.Issues ?? [])
            .Select(issue => new Finding(
                "sonar-issue",
                ParseSeverity(issue.Severity),
                issue.Message ?? "(no message)",
                StripProjectKeyPrefix(issue.Component, prefix),
                issue.Line,
                issue.Rule))
            .ToList();
    }

    private static string? StripProjectKeyPrefix(string? component, string prefix) =>
        component is not null && component.StartsWith(prefix, StringComparison.Ordinal)
            ? component[prefix.Length..]
            : component;

    // Sonar's classic severity scale (BLOCKER/CRITICAL/MAJOR/MINOR/INFO) collapsed onto CodeRail's
    // four-value Severity - same "map onto the nearest bucket" approach as SarifGateResultWriter's
    // Severity->SARIF-level mapping.
    private static Severity ParseSeverity(string? severity) => severity?.ToUpperInvariant() switch
    {
        "BLOCKER" => Severity.Critical,
        "CRITICAL" => Severity.Error,
        "MAJOR" or "MINOR" => Severity.Warning,
        _ => Severity.Info
    };

    private sealed record RawSearchResult(List<RawIssue>? Issues);

    private sealed record RawIssue(string? Rule, string? Severity, string? Component, string? Message, int? Line);
}

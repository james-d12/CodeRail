namespace CodeRail.Tooling;

/// <summary>
/// Sonar server connection details, resolved from a validation profile's <c>quality.sonar</c>
/// block (docs §9.6) and carried on <see cref="ToolContext.Sonar"/> so <c>SonarExecutor</c> can
/// reach the right project/server. A small standalone type rather than reusing
/// <c>CodeRail.Policy</c>'s <c>SonarQualityThresholds</c> directly - <c>CodeRail.Tooling</c> and
/// <c>CodeRail.Policy</c> deliberately don't reference each other (docs §24, keeping the
/// namespace-as-project seams clean; see CLAUDE.md's dependency-direction note).
/// </summary>
/// <param name="ProjectKey">The Sonar project key (<c>/k:</c> when invoking the scanner).</param>
/// <param name="Organization">SonarCloud organization key (<c>/o:</c>); null for a self-hosted
/// SonarQube server, which has no notion of organizations.</param>
/// <param name="HostUrl">Self-hosted SonarQube server URL; null defaults to SonarCloud
/// (<c>https://sonarcloud.io</c>).</param>
public sealed record SonarConfig(string ProjectKey, string? Organization, string? HostUrl);

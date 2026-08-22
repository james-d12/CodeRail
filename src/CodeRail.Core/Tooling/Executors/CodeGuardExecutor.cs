using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using CodeRail.Evidence;
using CodeRail.Execution;
using CodeRail.Tooling.Parsing;
using Microsoft.Extensions.Logging;

namespace CodeRail.Tooling.Executors;

/// <summary>
/// Runs <c>codeguard validate --format json</c> and converts CodeGuard's own report into the
/// common evidence model. See <c>docs/HIGH_LEVEL_PLAN.md</c> §9.4 - CodeGuard is a natural first
/// integration since it already emits structured, deterministic findings.
/// </summary>
public sealed class CodeGuardExecutor(IProcessRunner processRunner, ILogger<CodeGuardExecutor> logger) : IToolExecutor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public string Name => ValidationSteps.CodeGuard;

    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(
                "codeguard", $"validate --path \"{context.RepoRoot}\" --format json",
                context.RepoRoot, timeout: Timeout, cancellationToken: cancellationToken);
        }
        catch (Win32Exception ex)
        {
            // `codeguard` isn't resolvable on PATH - a missing optional tool shouldn't take down
            // the whole validation run, so this is reported as an unevaluated step rather than
            // rethrown.
            logger.LogWarning(ex, "codeguard executable not found on PATH");
            return Unavailable(stopwatch.Elapsed,
                "The 'codeguard' executable was not found on PATH. Install it with `dotnet tool install -g CodeGuard` " +
                "or as a local tool (`dotnet tool install CodeGuard`).");
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            logger.LogWarning("codeguard produced no output on stdout (exit code {ExitCode})", result.ExitCode);
            return Unavailable(stopwatch.Elapsed, OutputTail.Last(result.StandardError));
        }

        try
        {
            var summary = CodeGuardJsonParser.Parse(result.StandardOutput);
            var metrics = new Dictionary<string, object>
            {
                ["rulesEvaluated"] = summary.RulesEvaluated,
                ["rulesPassed"] = summary.RulesPassed,
                ["rulesFailed"] = summary.RulesFailed,
                ["rulesErrored"] = summary.RulesErrored
            };
            return new ToolResult(ToolIds.CodeGuard, summary.Status, summary.Findings, metrics, [], stopwatch.Elapsed);
        }
        catch (JsonException ex)
        {
            // The exception message alone ("'C' is an invalid start of a value...") isn't
            // actionable - e.g. this fires when codeguard's own pre-flight rule validation fails
            // and it prints a plain-text report to stdout instead of JSON, ignoring --format.
            // The actual output tells a user what's really going on; the parser exception doesn't.
            logger.LogWarning(ex, "Failed to parse codeguard JSON output");
            return Unavailable(stopwatch.Elapsed, $"codeguard did not produce valid JSON output:\n{OutputTail.Last(result.StandardOutput)}");
        }
    }

    private static ToolResult Unavailable(TimeSpan duration, string message) =>
        new(ToolIds.CodeGuard, ValidationStatus.PartiallyEvaluated,
            [new Finding("codeguard-unavailable", Severity.Warning, message, null, null, null)],
            new Dictionary<string, object>(), [], duration);
}

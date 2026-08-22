using CodeRail.Evidence;
using CodeRail.Policy;

namespace CodeRail.Reporting.Console;

/// <summary>Human-readable console output shaped like <c>docs/HIGH_LEVEL_PLAN.md</c> §1's
/// example block.</summary>
public sealed class ConsoleGateResultWriter : IGateResultWriter
{
    public string Format => "console";

    public async Task WriteAsync(GateResult result, TextWriter writer, CancellationToken cancellationToken = default)
    {
        var verdict = result.Status switch
        {
            ValidationStatus.Passed => "PASSED",
            ValidationStatus.Failed => "FAILED",
            ValidationStatus.PartiallyEvaluated => "PARTIALLY EVALUATED",
            _ => result.Status.ToString()
        };
        await writer.WriteLineAsync($"QUALITY GATE: {verdict}");
        await writer.WriteLineAsync();

        foreach (var tool in result.Tools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tag = tool.Status == ValidationStatus.Passed ? "PASS" : tool.Status == ValidationStatus.Failed ? "FAIL" : "PARTIAL";
            await writer.WriteLineAsync($"{ToolDisplayNames.For(tool.Tool)}: {tag}");
        }

        if (result.BlockingFindings.Count > 0)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("Blocking findings:");
            foreach (var finding in result.BlockingFindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = finding.File is null ? string.Empty : $" ({finding.File}{(finding.Line is { } line ? $":{line}" : string.Empty)})";
                await writer.WriteLineAsync($"- {finding.Message}{location}");
            }

            await writer.WriteLineAsync();
            await writer.WriteLineAsync("Action required:");
            await writer.WriteLineAsync("Fix the findings above, then re-run `coderail validate`.");
        }
    }
}

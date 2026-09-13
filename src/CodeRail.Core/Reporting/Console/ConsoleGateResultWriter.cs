using CodeRail.Evidence;
using CodeRail.Policy;

namespace CodeRail.Reporting.Console;

/// <summary>Human-readable console output shaped like <c>docs/HIGH_LEVEL_PLAN.md</c> §1's
/// example block. Findings are grouped by the tool that produced them, deduplicated, and capped
/// per tool so a large run stays scannable - <c>--format json</c>/<c>--format sarif</c> remain the
/// place to get the full, ungrouped list.</summary>
public sealed class ConsoleGateResultWriter(bool useColor = false) : IGateResultWriter
{
    // Findings shown individually before a tool group collapses to an overflow line. Deliberately
    // small - this is a terminal summary, not a report.
    private const int MaxFindingsPerTool = 10;

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
        var verdictColor = result.Status switch
        {
            ValidationStatus.Passed => AnsiCodes.Green,
            ValidationStatus.Failed => AnsiCodes.Red,
            _ => AnsiCodes.Yellow
        };
        await writer.WriteLineAsync($"QUALITY GATE: {Colorize(verdict, verdictColor, bold: true)}");
        await writer.WriteLineAsync();

        foreach (var tool in result.Tools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(ToolLine(tool));
        }

        if (result.BlockingFindings.Count > 0)
        {
            var summary = result.Summary;
            await writer.WriteLineAsync();
            await writer.WriteLineAsync($"Blocking findings: {summary.Total} ({SeverityBreakdown(summary)})");

            var blocking = new HashSet<Finding>(result.BlockingFindings);
            foreach (var tool in result.Tools)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var toolFindings = tool.Findings.Where(blocking.Contains).ToList();
                if (toolFindings.Count == 0)
                {
                    continue;
                }

                await writer.WriteLineAsync();
                await writer.WriteLineAsync($"{ToolDisplayNames.For(tool.Tool)} ({toolFindings.Count}):");
                await WriteFindingsAsync(writer, toolFindings, cancellationToken);
            }

            await writer.WriteLineAsync();
            await writer.WriteLineAsync("Action required:");
            await writer.WriteLineAsync("Fix the findings above, then re-run `coderail validate`.");
        }
    }

    private async Task WriteFindingsAsync(TextWriter writer, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        var grouped = findings
            .GroupBy(f => (f.Message, f.File, f.Line, f.RuleId))
            .Select(g => (Finding: g.First(), Count: g.Count()))
            .ToList();

        var shown = 0;
        foreach (var (finding, count) in grouped)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shown >= MaxFindingsPerTool)
            {
                var remaining = grouped.Count - shown;
                await writer.WriteLineAsync(Colorize(
                    $"  … and {remaining} more — see --format json for the full list", AnsiCodes.Dim, bold: false));
                return;
            }

            var location = finding.File is null ? string.Empty : $" ({finding.File}{(finding.Line is { } line ? $":{line}" : string.Empty)})";
            var suffix = count > 1 ? $" (×{count})" : string.Empty;
            var mark = Colorize("✗", AnsiCodes.Red, bold: false);
            await writer.WriteLineAsync($"  {mark} {finding.Message}{suffix}{location}");
            shown++;
        }
    }

    private string ToolLine(ToolResult tool)
    {
        var (mark, tag, color) = tool.Status switch
        {
            ValidationStatus.Passed => ("✓", "PASS", AnsiCodes.Green),
            ValidationStatus.Failed => ("✗", "FAIL", AnsiCodes.Red),
            _ => ("!", "PARTIAL", AnsiCodes.Yellow)
        };
        var name = ToolDisplayNames.For(tool.Tool);
        var duration = tool.Duration > TimeSpan.Zero ? $"  ({FormatDuration(tool.Duration)})" : string.Empty;
        return $"  {Colorize(mark, color, bold: false)} {name,-10} {Colorize(tag, color, bold: true)}{duration}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 10 ? $"{duration.TotalSeconds:0.0}s" : $"{duration.TotalSeconds:0}s";

    private static string SeverityBreakdown(FindingSummary summary)
    {
        var parts = new List<string>();
        if (summary.Critical > 0)
        {
            parts.Add($"{summary.Critical} critical");
        }

        if (summary.Error > 0)
        {
            parts.Add($"{summary.Error} error");
        }

        if (summary.Warning > 0)
        {
            parts.Add($"{summary.Warning} warning");
        }

        if (summary.Info > 0)
        {
            parts.Add($"{summary.Info} info");
        }

        return parts.Count == 0 ? "0 blocking" : string.Join(", ", parts);
    }

    private string Colorize(string text, string color, bool bold) =>
        useColor ? $"{(bold ? AnsiCodes.Bold : string.Empty)}{color}{text}{AnsiCodes.Reset}" : text;

    private static class AnsiCodes
    {
        public const string Reset = "[0m";
        public const string Bold = "[1m";
        public const string Green = "[32m";
        public const string Red = "[31m";
        public const string Yellow = "[33m";
        public const string Dim = "[2m";
    }
}

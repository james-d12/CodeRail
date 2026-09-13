using CodeRail.Evidence;
using CodeRail.Policy;
using CodeRail.Reporting.Console;

namespace CodeRail.Reporting.Tests.Console;

public class ConsoleGateResultWriterTests
{
    private readonly ConsoleGateResultWriter _writer = new();

    [Fact]
    public async Task WriteAsync_RendersPassedVerdict_AndPerToolPassLines_WithNoFindingsSection()
    {
        var build = new ToolResult(ToolIds.DotnetBuild, ValidationStatus.Passed, [], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var test = new ToolResult(ToolIds.DotnetTest, ValidationStatus.Passed, [], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Passed, [build, test], [], DateTimeOffset.UtcNow);

        var output = await Render(gate);

        Assert.Contains("QUALITY GATE: PASSED", output);
        AssertToolLine(output, "Build", "PASS");
        AssertToolLine(output, "Tests", "PASS");
        Assert.DoesNotContain("Blocking findings:", output);
    }

    [Fact]
    public async Task WriteAsync_RendersFailedVerdict_WithBlockingFindingsAndLocation()
    {
        var finding = new Finding("architecture", Severity.Critical, "Domain references Infrastructure", "OrderService.cs", 42, "ARCH-001");
        var codeGuard = new ToolResult(ToolIds.CodeGuard, ValidationStatus.Failed, [finding], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [codeGuard], [finding], DateTimeOffset.UtcNow);

        var output = await Render(gate);

        Assert.Contains("QUALITY GATE: FAILED", output);
        AssertToolLine(output, "CodeGuard", "FAIL");
        Assert.Contains("Blocking findings: 1 (1 critical)", output);
        Assert.Contains("CodeGuard (1):", output);
        Assert.Contains("Domain references Infrastructure", output);
        Assert.Contains("(OrderService.cs:42)", output);
        Assert.Contains("Action required:", output);
    }

    [Fact]
    public async Task WriteAsync_OmitsLocationSuffix_WhenFindingHasNoFile()
    {
        var finding = new Finding("coverage-below-threshold", Severity.Error, "Line coverage 61% is below the required minimum 80%.", null, null, null);
        var coverage = new ToolResult(ToolIds.Coverage, ValidationStatus.Passed, [finding], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [coverage], [finding], DateTimeOffset.UtcNow);

        var output = await Render(gate);

        var line = Lines(output).Single(l => l.Contains("Line coverage 61%"));
        Assert.DoesNotContain("(", line);
    }

    [Fact]
    public async Task WriteAsync_CollapsesIdenticalFindings_WithACountSuffix()
    {
        var finding = new Finding("call-site", Severity.Error, "Expected at least one match for a 'call_site' selector, but found none.", "/repo", null, null);
        var codeGuard = new ToolResult(ToolIds.CodeGuard, ValidationStatus.Failed, [finding, finding, finding], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [codeGuard], [finding, finding, finding], DateTimeOffset.UtcNow);

        var output = await Render(gate);

        Assert.Contains("CodeGuard (3):", output);
        var matches = Lines(output).Count(l => l.Contains("call_site"));
        Assert.Equal(1, matches);
        Assert.Contains("(×3)", output);
    }

    [Fact]
    public async Task WriteAsync_CapsFindingsPerTool_WithAnOverflowLine()
    {
        var findings = Enumerable.Range(0, 12)
            .Select(i => new Finding("rule", Severity.Error, $"Finding {i}", null, null, null))
            .ToList();
        var codeGuard = new ToolResult(ToolIds.CodeGuard, ValidationStatus.Failed, findings, new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [codeGuard], findings, DateTimeOffset.UtcNow);

        var output = await Render(gate);

        var shown = Lines(output).Count(l => l.Contains("Finding "));
        Assert.Equal(10, shown);
        Assert.Contains("… and 2 more — see --format json for the full list", output);
    }

    [Fact]
    public async Task WriteAsync_EmitsNoAnsiCodes_ByDefault()
    {
        var gate = new GateResult(ValidationStatus.Passed, [], [], DateTimeOffset.UtcNow);

        var output = await Render(gate);

        Assert.DoesNotContain('', output);
    }

    [Fact]
    public async Task WriteAsync_EmitsAnsiCodes_WhenColorEnabled()
    {
        var gate = new GateResult(ValidationStatus.Passed, [], [], DateTimeOffset.UtcNow);
        var writer = new ConsoleGateResultWriter(useColor: true);

        var stringWriter = new StringWriter();
        await writer.WriteAsync(gate, stringWriter);

        Assert.Contains('', stringWriter.ToString());
    }

    private static void AssertToolLine(string output, string toolName, string tag)
    {
        Assert.Contains(Lines(output), l => l.Contains(toolName) && l.Contains(tag));
    }

    private static string[] Lines(string output) => output.Replace("\r\n", "\n").Split('\n');

    private async Task<string> Render(GateResult gate)
    {
        var stringWriter = new StringWriter();
        await _writer.WriteAsync(gate, stringWriter);
        return stringWriter.ToString();
    }
}
